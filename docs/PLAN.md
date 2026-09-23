# OIDC authentication proxy implementation plan

## 1. Purpose and scope

Build this service into a standards-based backend-for-frontend (BFF) that uses
OpenID Connect Authorization Code Flow with PKCE. The browser must receive only
an opaque session cookie. ID, access, and refresh tokens remain server-side in
an `IDistributedCache`-backed session. The first cache implementation is
process memory, but authentication code must not depend on that implementation.
The initial deployment has one active proxy replica and deliberately volatile
sessions: a proxy restart, deployment, or cache loss ends sessions and pending
logins rather than attempting recovery.

The implementation covers:

- explicit login, callback, session introspection, user-initiated logout, and
  OpenID Connect back-channel logout;
- automatic access-token refresh without parsing access or refresh tokens;
- construction and renewal of the `ClaimsPrincipal` from validated ID tokens
  only;
- role extraction from ID-token claims, without role authorization policies;
- local HTTPS plus a reproducible Keycloak realm for development;
- provider-neutral operation with Keycloak and ZITADEL through discovery and
  standard OpenID Connect endpoints;
- an optional authenticated YARP module that forwards the current server-held
  access token to configured HTTPS upstreams; and
- focused unit and integration coverage plus flow-oriented, secret-free logs.

It does not cover concurrent replicas, durable session recovery, distributed
locking, return URLs, bearer authentication for browser APIs, frontend token
storage, access-token claim inspection, application role authorization, live
proxy-route reload, arbitrary YARP configuration, load balancing, token
exchange, or access tokens for multiple audiences.

## 2. Evidence and example assessment

The current repository is a .NET 10 solution containing `AuthnProxy.Api`,
`AuthnProxy.Application`, unit tests, and integration tests. The API currently
exposes only `/ping`. It targets SDK 10.0.401 and package version 10.0.12 for
ASP.NET Core dependencies. There is no existing authentication implementation,
container definition, certificate tooling, or `docs` directory.

The requested examples are present with different names from those in the
requirements:

- `examples/ssoTokenExtension.txt` contains `SsoExtensions` (the requested
  `SSoExtensions.txt`);
- `examples/ssoTockenRefresher.txt` contains `SsoTokenRefresher` (the requested
  `SsoTokenRefresher.txt`; the filename misspells "Token").

They are useful descriptions of the basic ASP.NET Core hooks, but neither is a
suitable implementation for this project. Adopt the framework concepts, adapt
the refresh orchestration, and reject the provider-specific configuration and
session/failure behavior. Do not copy either file wholesale.

### 2.1 `SsoExtensions` assessment

#### Good ideas to adopt or adapt

- **Adopt the framework handlers** (`AddAuthentication`,
  `AddOpenIdConnect`, and `AddCookie`, lines 22-30). Cookie authentication as
  the default authenticate/sign-in scheme and OIDC as the explicit remote
  scheme are the correct primitives. Adapt the challenge behavior: application
  APIs must return 401 and only `/login` explicitly challenges OIDC.
- **Adopt code flow and PKCE** (`UsePkce=true` and `ResponseType=code`, lines
  55-62). These match the requested confidential BFF flow.
- **Adapt `SaveTokens=true`** (line 60). Saving tokens in
  `AuthenticationProperties` is useful for refresh and `id_token_hint`, but is
  acceptable only after the cookie scheme uses `ITicketStore`; in this example
  no `SessionStore` is assigned, so the shown configuration leaves the entire
  ticket, including tokens, in the protected browser cookie. An unseen
  `.AddJfa()` implementation cannot be treated as evidence to the contrary.
- **Adopt bounded clock skew and lifetime validation** (lines 73-75), with
  values validated from provider-neutral options. Signature, issuer, audience,
  and lifetime validation remain enabled.
- **Adopt clearing the framework default scopes** (lines 79-80). Adapt the
  resulting list to `openid offline_access`: ZITADEL requires
  `offline_access` to request a refresh token, so the line 81 assertion that
  refresh tokens are always issued without it is not portable.
- **Adopt `UseTokenLifetime=false`** (line 87). The local session has its own
  bounded policy and refreshes access tokens; it should not expire merely when
  the initial access token does.
- **Adopt the cookie-validation hook** (`OnValidatePrincipal`, lines 162-165).
  It is the appropriate stable boundary for proactive refresh. Register a
  cohesive event type rather than building all behavior inside options setup.
- **Adopt HttpOnly and SameSite protections** (lines 139-142), then complete
  them with `Secure=Always`, `__Host-` naming, `Path=/`, no Domain, and a
  server-side ticket store.

#### Problems to reject or replace

- **Reject `.AddJfa()` as design input** (line 31). Its implementation was not
  supplied, it is not a .NET/ASP.NET Core OIDC primitive, and no required
  behavior may depend on it. `AddHttpContextAccessor` (line 33) is likewise
  unnecessary unless a concrete consumer is introduced.
- **Replace the default OIDC challenge scheme** (line 25). It redirects an
  ordinary unauthenticated API request to the IdP, whereas `/whoami` and other
  APIs must return 401 and let the frontend initiate `/login`.
- **Add real option validation.** `BindConfiguration(...).ValidateOnStart()`
  (lines 43-45) has no data-annotation or custom validator in the example, so it
  does not prove the required authority, credentials, URI, lifetime, and cookie
  invariants.
- **Reject all `Keycloak*` application options** (lines 57-59, 69-71, 144-150).
  Authority, credentials, and local session policy must be provider-neutral;
  Keycloak and ZITADEL use the same code path.
- **Reject the hand-built partial OIDC configuration** (lines 64-67). Combining
  `Authority` with manually assigned public authorization/token endpoints
  bypasses useful discovery metadata and omits issuer, JWKS rotation,
  end-session, PAR, and back-channel capability data. Use one HTTPS authority
  and its discovery document.
- **Reject the PEM signing-key loader and fixed RSA key** (lines 76-77 and
  121-130). Discovery-provided signing keys and the configuration manager own
  rotation and algorithm agility. Synchronous file I/O and undisposed
  certificate/RSA objects during options setup are additional defects.
- **Do not use `MaxAge` as local session expiry** (lines 69-71). OIDC `max_age`
  requests a maximum age for IdP authentication; it does not express the
  application's idle/absolute session policy.
- **Reject `RequireHttpsMetadata=false`** (line 83) outside isolated tests. Both
  local development and production are planned for HTTPS.
- **Do not copy `AuthenticationMethod=FormPost`** (line 61). In ASP.NET Core 10
  this controls how the proxy sends the authorization request to the IdP; it is
  distinct from `ResponseMode=form_post`. It would make `/login` return an HTML
  auto-submit form rather than the required temporary Location redirect. Leave
  `AuthenticationMethod=RedirectGet` and explicitly use the default
  `ResponseMode=form_post` for the IdP callback.
- **Reject the `OnAuthenticationFailed` recovery** (lines 89-117). That remote
  callback event does not own later cookie refresh failures, handles only one
  exception type, introduces the explicitly excluded return-URL behavior,
  directly deletes a browser cookie rather than the authoritative ticket, and
  can create redirect loops. Refresh failure policy belongs to cookie
  validation and the session store.
- **Replace the cookie lifetime setup** (lines 144-151). It is coupled to
  Keycloak, creates a persistent `Max-Age` cookie rather than a browser-session
  cookie, and sliding renewal alone does not enforce an absolute lifetime.
- **Replace `OnRedirectToLogin`** (lines 153-160). It deletes only the cookie
  and neither sets the required 401 response nor removes a future server-side
  ticket. Challenge response semantics and ticket deletion need distinct
  owners.
- **Add all missing boundaries:** `IDistributedCache`, `ITicketStore`, cached
  pending state, opaque cookie reference, ID-token role normalization,
  user-initiated and back-channel logout, fixed redirects, and structured logs.

Verdict: **adapt the registration shape but reject this implementation**. Its
single extension method is useful as a small composition entry point, while the
actual option validation, handler configuration, session storage, events, and
logout behavior belong in separate cohesive modules.

### 2.2 `SsoTokenRefresher` assessment

#### Good ideas to adopt or adapt

- **Adopt a custom refresh component.** The file cites ASP.NET Core issue 8175
  (line 11); that framework feature request is still open and currently targeted
  beyond .NET 10. The OIDC handler saves refresh tokens but does not manage
  ongoing cookie-ticket refresh automatically.
- **Adopt `OnValidatePrincipal` orchestration** (called by the extension) and
  the early no-refresh return (lines 20-36). Adapt the threshold to a clear
  configurable lead time based on saved `expires_at` rather than a
  Keycloak-configured lifetime percentage.
- **Adopt `TimeProvider` and invariant timestamp handling** (lines 22-23 and
  31-32) for deterministic policy and tests.
- **Adopt current discovery and OIDC backchannel use** (lines 38-43). The token
  endpoint must come from current metadata and the handler's backchannel keeps
  transport configuration centralized.
- **Adopt opaque access/refresh token treatment.** The example never decodes
  either token and bases timing on `expires_at`/`expires_in`.
- **Adopt cloning validation parameters and using current issuer/signing keys**
  (lines 61-67), then complete the validation described below.
- **Adopt constructing the replacement principal only from a validated ID
  token** and marking the cookie ticket for renewal (lines 69-76).
- **Adopt the absence of a refresh lock.** It matches the explicit acceptance
  of races for concurrent requests.

#### Problems to reject or replace

- **Fix the refresh threshold** (lines 17 and 28-36). With a 70% constant, the
  condition refreshes after roughly 30% of the configured lifetime has elapsed,
  not after 70%. More importantly, a provider-configured lifetime is unnecessary
  when the ticket already has an absolute `expires_at`; use a named lead-time
  duration.
- **Reject silently accepting missing/malformed `expires_at`** (lines 22-26).
  Such a ticket cannot prove a usable access-token lifetime and must be treated
  as invalid rather than accepted indefinitely.
- **Remove `KeycloakAccessTokenLifetimeSeconds`** (line 28). Token response
  metadata and local options suffice and work with both providers.
- **Validate the saved refresh token before sending** (lines 43-50). A missing
  token is a terminal session condition, not a form value to send.
- **Make client authentication configurable and standard.** Lines 47-48 always
  use `client_secret_post`; deployments may require `client_secret_basic`.
  Select by standard configuration/discovery, never provider name.
- **Propagate cancellation through the complete exchange.** Lines 43-50 omit
  the request cancellation token from `PostAsync`, and line 58 omits it from
  response reading even though metadata retrieval correctly uses it.
- **Make every completed refresh failure fail closed.** Lines 52-55 reject the
  principal for every non-success response but do not ensure authoritative
  cleanup. `invalid_grant`, malformed responses, discovery failures, IdP `5xx`
  responses, timeouts, and transport failures all invalidate the session once
  refresh is due. Request-aborted cancellation is propagated instead because
  the client is gone and cleanup may no longer be reliable.
- **Validate the complete refresh response.** `OpenIdConnectMessage` plus
  `int.Parse` (lines 58-59 and 78) does not explicitly require a nonempty access
  token, positive bounded `expires_in`, compatible token type, or a valid JSON/
  protocol response. Malformed responses need a controlled terminal result,
  not incidental parsing exceptions.
- **Allow an omitted ID token.** Lines 66-76 require and replace the principal
  from `message.IdToken`, but a conforming refresh response need not issue a new
  ID token. Retain the existing ID-token-derived principal when absent.
- **Complete new ID-token validation.** Lines 61-67 add issuer and signing keys
  but do not explicitly restore client-ID audience/authorized-party validation
  or refresh-response OIDC protocol rules. Use the client ID as the audience,
  current discovery keys/algorithms, issuer/lifetime checks, and nonce
  consistency if a nonce is present before rebuilding claims and roles.
- **Preserve tokens omitted by the provider.** `StoreTokens` (lines 82-88)
  replaces the entire collection with possibly null values, losing an existing
  refresh token or ID token when the provider does not rotate/reissue it. Merge
  the validated access token and expiry, use a rotated refresh token when
  present, otherwise retain the old one, and preserve unrelated properties.
- **Delete an unusable authoritative session.** `RejectPrincipal()` alone
  (lines 52-55 and 69-72) does not express ticket/index cleanup. The cookie event
  and `ITicketStore` must ensure invalid sessions are removed and the browser
  receives an expired cookie.
- **Add role normalization, safe diagnostics, and explicit outcomes.** A new ID
  token needs the same role mapping as initial login. The example emits no logs
  and exposes no distinction between not-due, refreshed, invalidated, and
  request-aborted outcomes, making flow behavior difficult to operate or test.

Verdict: **adapt the orchestration, replace the implementation**. Preserve the
cookie-validation hook, discovery/backchannel use, opaque-token treatment,
current-key validation, ID-token-only principal, renewal signal, and deliberate
absence of locking. Rebuild request validation, cancellation, failure policy,
token merging, ID-token protocol validation, session deletion, role mapping,
and logging in `Authentication/Refresh`.

## 3. Acceptance map

| Requirement | Planned owner | Completion evidence |
| --- | --- | --- |
| Code flow with PKCE | ASP.NET Core OIDC handler configuration | Integration test observes code response type, PKCE challenge, state, nonce, and correlation behavior |
| No tokens in frontend | `ITicketStore` session boundary and allowlisted responses | Integration test proves the cookie is an opaque reference and contains none of the known test tokens; endpoint contracts expose no token field |
| Volatile in-memory sessions through distributed abstraction | `DistributedCacheTicketStore` plus `AddDistributedMemoryCache` composition | Store unit tests and service-registration integration tests use only `IDistributedCache`; loss of a cache record safely makes the cookie anonymous |
| Pending login material in memory | `DistributedCacheStateDataFormat` | Unit tests prove random opaque state, bounded expiry, one-time consumption, and invalid/expired rejection |
| Correlation protection | Built-in OIDC correlation and nonce cookies | Integration test proves absent/tampered correlation fails; cookie options are asserted once at the framework boundary |
| Keycloak development IdP | Compose, certificate script, and importable realm | Native Compose rendering/build validation and an integration fixture using the imported realm configuration |
| Keycloak and ZITADEL compatibility | Standard discovery/options plus configurable claim/token-client policies | Provider contract fixtures cover both metadata/claim shapes; no provider SDK or provider-name branch exists |
| Principal from ID token only | OIDC handler and ID-token principal factory used during refresh | Unit tests use an opaque, non-JWT access token and prove identity/roles come only from the signed ID token |
| Minimal scopes | OIDC options and provider configuration | Options test proves default scopes are `openid offline_access`; any role scope is an explicit deployment override |
| Roles consumed, no role authorization | ID-token role normalizer and `/whoami` response | Unit tests cover supported role claim shapes; solution contains no role policy or role-gated endpoint |
| Token refresh | Cookie validation event and `OidcTokenRefresher` | Unit tests cover not-due, success, rotation, optional new ID token, each completed failure class invalidating the session, and request-aborted cancellation propagation |
| User-initiated logout | `POST /logout`, cookie sign-out, OIDC sign-out | Integration test proves local ticket removal and OIDC sign-out with fixed post-logout redirect and server-held ID-token hint |
| Back-channel logout | Logout-token validator, session index, and endpoint | Standards-focused unit tests plus endpoint integration tests cover signature/claims/replay and `sid`/`sub` invalidation |
| Transparent authenticated upstream proxying | Isolated YARP registration, route translation, authorization policy, and transforms | Integration tests prove local and proxied requests share one browser contract, only authenticated requests reach an upstream, the current access token replaces browser credentials, cookies do not cross the boundary, and upstream failures retain the session |
| Same-origin unsafe requests | Central origin-validation middleware | Unit and integration tests prove unsafe local and proxied requests require the configured public origin while OIDC and back-channel protocol ingress retain their protocol-specific validation |
| Minimal logs | Source-generated authentication log events | Tests assert representative event identity/severity/required safe fields and forbidden secrets, without pinning prose |
| Local HTTPS | OpenSSL script, Kestrel and Keycloak configuration | Certificate inspection and native container/configuration validation prove SANs, expiry, HTTPS endpoints, and loopback-only publication |

## 4. Architecture and ownership

Keep the existing projects. Authentication is fundamentally an ASP.NET Core
protocol and hosting concern, so place its framework adapters under
`AuthnProxy.Api/Authentication` rather than adding a mostly empty infrastructure
project. Keep pure models and policies independent from endpoint handlers, and
split public or independently testable types into concern-specific files.

Planned modules:

```text
AuthnProxy.Api/Authentication/
  Configuration/       bound options, validation, authentication registration
  Endpoints/            /login, /logout, /whoami, /backchannel-logout
  Sessions/             ITicketStore, protected ticket codec, sid/sub indexes
  Transactions/         cache-backed OIDC state data format
  Claims/               validated ID-token principal and role normalization
  Refresh/              token request/response and cookie validation event
  Logout/               logout-token validation and session invalidation
  Diagnostics/          source-generated, secret-free log events
AuthnProxy.Api/Proxying/
  Configuration/       small route contract, validation, YARP translation
  Transforms/          access-token injection and credential-boundary handling
  Diagnostics/         source-generated, secret-free proxy events
```

Add `InternalsVisibleTo` only where it permits focused tests of internal policy.
Do not create interfaces for framework types that are already replaceable, such
as `IDistributedCache`, `TimeProvider`, `IOptionsMonitor<T>`, `HttpMessageHandler`,
or the OIDC configuration manager.

### 4.1 Composition root

`Program.cs` remains small. A single `AddProxyAuthentication(configuration)`
extension registers cache, data protection, option validation, session/state
stores, refresh/logout services, cookie authentication, and OpenID Connect.
`MapProxyAuthentication()` maps only application-owned endpoints; the OIDC
handler continues to own callback and signed-out callback paths. Separate
`AddProxying(configuration)` and `MapProxying()` extensions conditionally
register and map YARP without creating an authentication-to-YARP dependency.

Middleware order is:

1. exception handling;
2. forwarded headers only if an explicit trusted-proxy configuration is later
   supplied (do not trust arbitrary forwarding headers);
3. HTTPS redirection outside integration-test hosting;
4. centralized origin validation;
5. authentication;
6. authorization when proxying is enabled; and
7. application and proxy endpoints.

Every generated proxy route uses one fixed infrastructure policy requiring an
authenticated session. The proxy configuration cannot select anonymous or
arbitrary policies. Do not add role policies, custom authorization handlers, or
role-gated application endpoints. Application-owned endpoints that need a
session continue to check `HttpContext.User.Identity.IsAuthenticated` or call
authentication explicitly and return 401.

## 5. Protocol and endpoint design

Use stable routes:

| Route | Owner and behavior |
| --- | --- |
| `GET /login` | Starts an explicit OIDC challenge with a fixed post-login path. It accepts no return URL. |
| `/signin-oidc` | Framework-owned callback. The handler validates state, correlation, nonce, issuer, audience, signature, and code response before issuing a local session. |
| `GET /whoami` | Returns 401 without a valid server ticket; otherwise returns an allowlisted identity model containing `subject`, optional display name, and roles. |
| `POST /logout` | Removes the local server ticket and invokes cookie plus OIDC sign-out schemes with a fixed post-logout path. |
| `/signout-callback-oidc` | Framework-owned signed-out callback, ending at the configured frontend home path. |
| `POST /backchannel-logout` | Accepts form-encoded `logout_token`, validates it, invalidates matching server sessions, and returns 200 or 400 with `Cache-Control: no-store`. |

The login challenge uses a temporary 302 redirect, not a cacheable 301. The
frontend starts login by assigning the browser location to `/login`; it must not
fetch tokens or attempt to interpret the authorization response. Logout uses a
top-level POST (for example, a form submission) so a third-party site cannot
trigger it with an ordinary link. The configured frontend home is a local,
startup-validated relative path, eliminating open-redirect input.

For an automatic login experience, a frontend-wide response interceptor may
handle an API `401` by assigning `window.location` to `/login`, with loop
prevention and exemptions for requests that should not initiate navigation.
Returning a redirect from an API request is not a substitute: `fetch` or XHR
follows it within that request and does not navigate the top-level document.
Frontend implementation is outside this repository.

Cookie authentication is the default authenticate and sign-in scheme. API
challenge/forbidden events return 401/403 rather than redirecting to HTML login.
The OIDC scheme is challenged only by `/login`.

Apply one origin policy before both application and proxy endpoints so browser
clients do not need to know which component owns a route. For every method other
than `GET`, `HEAD`, and `OPTIONS`, require exactly one syntactically valid
`Origin` header whose serialized origin equals the configured `PublicOrigin`.
Reject a missing, opaque (`null`), malformed, or mismatched origin with 403
before invoking the endpoint or an upstream. Do not use a custom CSRF header,
fall back to `Referer`, or add permissive CORS behavior.

Exclude only protocol ingress that cannot originate at the frontend:
`/signin-oidc`, `/signout-callback-oidc`, and `/backchannel-logout`. The OIDC
callback remains protected by state, correlation, and nonce; back-channel logout
remains protected by its signed logout token and replay checks. The exemption is
owned centrally and cannot be extended through proxy-route configuration.

## 6. OIDC configuration

Bind and validate one provider-neutral configuration section. It contains:

- authority, client ID, client secret (external secret source only), callback
  paths, fixed post-login/post-logout paths, and the canonical `PublicOrigin`;
- additional scopes, with `openid offline_access` as the default complete set;
- configurable name and role claim names plus the generic role-value shape;
- session absolute/idle lifetime, refresh lead time, and pending transaction
  lifetime;
- cookie name and optional data-protection application name; and
- standard client authentication method where refresh interoperability requires
  choosing `client_secret_basic` or `client_secret_post`.

Fail startup for missing/invalid authority, secret, client ID, non-HTTPS
production authority, absolute callback/home URLs where local paths are
required, nonpositive/contradictory lifetimes, or a cookie name that does not
have the `__Host-` prefix. `PublicOrigin` must be an absolute HTTPS origin with
no path other than `/`, query, fragment, or user information; isolated tests may
use an HTTP loopback origin.

Configure the OIDC handler to:

- use authorization code response type and PKCE;
- use `AuthenticationMethod=RedirectGet` so `/login` sends the browser to the
  authorization endpoint with a temporary redirect;
- use the standard `form_post` response mode so the authorization response is
  posted independently by the IdP to the callback and is not left in browser
  history or ordinary query logs;
- require HTTPS metadata except in explicitly isolated tests;
- use discovery rather than configured vendor endpoint paths;
- leave pushed authorization requests at the ASP.NET Core interoperable
  `UseIfAvailable` behavior;
- disable UserInfo retrieval so it cannot augment identity from the access
  token;
- save tokens into `AuthenticationProperties`, which are server-side because
  the cookie scheme uses `ITicketStore`;
- disable inbound claim renaming and use explicit name/role claim types;
- retain built-in state, correlation, and nonce validation; and
- record callback success/failure through OIDC events without logging protocol
  payloads.

The provider must issue the required roles in the ID token. `profile` and
`email` are not requested by default. If a deployment needs them, it adds them
explicitly and documents why. `offline_access` is the only non-identity default
scope because refresh tokens are a stated requirement.

## 7. Pending OIDC transaction state

ASP.NET Core normally protects authentication properties into the browser
`state` parameter. To satisfy the server-side pending-transaction requirement,
replace only `OpenIdConnectOptions.StateDataFormat` with a purpose-specific
`ISecureDataFormat<AuthenticationProperties>` implementation:

1. generate a 256-bit random Base64url handle;
2. serialize and data-protect the authentication properties under a dedicated
   purpose string;
3. store that value in `IDistributedCache` with a short absolute expiry;
4. send only the random handle as `state`; and
5. retrieve and remove it on callback (one-time on the normal path).

Unknown, malformed, expired, or already consumed handles fail authentication.
The synchronous secure-data-format contract may use the cache's synchronous
methods; isolate that compromise in this adapter. Do not replace the built-in
nonce or correlation checks. Configure nonce and correlation cookies as Secure,
HttpOnly, host-only cookies with `Path=/` and `SameSite=None`, which is required
for their inclusion on the selected cross-site `form_post` callback. The local
HTTPS requirement is therefore mandatory even during development.

Protecting cached transaction properties is required even though their storage
is volatile. It prevents a cache-only reader from inspecting trusted properties
and makes cache modification fail authentication; restart survival is a
separate availability concern.

## 8. Server-side session design

Implement `DistributedCacheTicketStore : ITicketStore` and assign it to
`CookieAuthenticationOptions.SessionStore` through post-configuration, because
the store has injected dependencies.

The store:

- creates a 256-bit random session ID and uses a versioned, hashed cache key;
- serializes the complete `AuthenticationTicket` (principal plus saved token
  properties) with the framework ticket serializer;
- protects serialized data with a dedicated data-protection purpose so a later
  external cache is not a plaintext token repository;
- applies the ticket's absolute expiry and the configured idle policy;
- supports store, renew, retrieve, and remove with cancellation; and
- updates the logout indexes when a ticket is created, renewed, or removed.

Purpose-specific protection keeps a cache-only compromise from exposing saved
ID, access, and refresh tokens or silently modifying trusted principal and
ticket data. A dedicated, unpublished cache is still a high-value credential
store and is not a substitute for confidentiality and integrity protection.
Protection does not promise durability: with the initial ephemeral key ring,
records from an earlier process are intentionally unreadable and expire as
stale cache data.

The cookie contains only the data-protected session-store reference generated
by the cookie handler. Set its name to `__Host-authn-proxy`, `Secure=Always`,
`HttpOnly=true`, `Path=/`, no Domain, and `SameSite=Lax`. Keep it a browser
session cookie; server expiry remains authoritative. Do not expose the raw
session ID in responses or logs. If its referenced cache record is missing or
unreadable, authenticate as anonymous and expire the stale cookie when the
response can still be sent.

Back-channel logout cannot enumerate `IDistributedCache`, so maintain secondary
index entries:

- `(issuer, sid) -> local session ID(s)` for exact provider sessions; and
- `(issuer, sub) -> local session ID(s)` for providers/tokens without `sid`.

Hash issuer/subject/session index material before using it in cache keys. Give
indexes no longer a lifetime than their tickets and prune stale IDs during
lookup. Read/modify/write races are accepted by the stated single-replica,
no-concurrency-hardening requirement. Do not add distributed locks.

These indexes are retained because prompt provider-initiated, administrator,
and global logout is a requirement. A logout token identifies sessions by
issuer plus `sid` or `sub`, not by the random local ticket handle, so some
equivalent lookup is unavoidable while `IDistributedCache` remains the storage
boundary.

## 9. Principal and role handling

The initial principal is the principal produced from the OIDC handler's
validated ID token. The access token and refresh token are opaque strings and
are never decoded, passed to a JWT handler, or used as claim sources.

Set `sub` as the stable required subject identifier. The response may use a
configured ID-token name claim when present, but login does not require profile
claims. Normalize roles from a configured ID-token claim into repeated claims
of the configured role claim type. Support only the two material JSON shapes
needed by the providers:

- a string or array of strings (the Keycloak realm maps its roles this way);
- an object whose property names are role keys (ZITADEL's project-role claim).

This is claim-shape configuration, not a provider-name conditional. Reject or
ignore malformed role values consistently and log only an aggregate diagnostic,
never the claim contents. ZITADEL must be configured to put user roles in the
ID token; Keycloak must use a realm/client-scope mapper that puts the agreed
claim in the ID token. Neither requires the proxy to understand an access-token
format.

Keep these two small cases in one normalizer selected by the validated claim
shape option. Do not add provider-named implementations or a chain that tries
extractors until one accepts the value. If a third materially different
algorithm is required later, split the cases into `IRoleClaimExtractor`
strategies and select exactly one by claim-shape configuration.

## 10. Refresh behavior

Use a scoped cookie validation event backed by `OidcTokenRefresher`. On requests
with a valid ticket:

1. read the saved `expires_at` value without decoding the access token;
2. return unchanged when outside the refresh window;
3. when due, send the refresh token to the token endpoint from current discovery
   metadata, using the configured standard client authentication method;
4. require a successful protocol response with an access token and expiry;
5. preserve the existing refresh token if none is returned, or store the rotated
   one if present;
6. if a new ID token is returned, validate signature, issuer, audience, lifetime,
   and protocol requirements with current provider metadata, then rebuild the
   principal from that ID token only;
7. if no new ID token is returned, retain the existing ID-token-derived
   principal;
8. replace tokens in `AuthenticationProperties`, replace the principal when
   needed, and set `ShouldRenew` so the server ticket is rewritten; and
9. propagate request cancellation and preserve exception causes.

Failure policy:

- any completed unsuccessful refresh—including `invalid_grant`, invalid
  protocol data, an invalid refreshed ID token, discovery failure, IdP `5xx`,
  timeout, or transport failure—rejects the principal and deletes the
  authoritative ticket and indexes;
- expire the browser cookie when the response can still be sent and return 401;
- propagate a request-aborted `OperationCanceledException` instead of
  translating it into an authentication result, because the client is already
  gone and cancellation may prevent reliable cleanup; and
- concurrent refreshes may race, as explicitly allowed. There is no lock or
  single-flight mechanism.

This deliberately trades availability for a smaller fail-closed policy. A
brief IdP, discovery, DNS, or network outage can log out every session entering
its refresh window, discard an access token that was still valid, and cause a
burst of new login attempts. A frontend that immediately navigates to `/login`
after every 401 must prevent repeated login failure from becoming a redirect
loop. These outcomes are accepted rather than retaining a ticket for retry.

The refresh service is responsible only for token lifecycle. It does not call a
downstream API and never returns tokens through an endpoint.

## 11. Logout behavior

### 11.1 User-initiated logout

`POST /logout` requires a valid same-origin session. It invokes both the cookie
and OIDC sign-out schemes. The cookie handler removes the server ticket; the
OIDC handler uses discovery's `end_session_endpoint` when advertised and the
saved server-side ID token as `id_token_hint`. The only redirect destination is
the configured local post-logout path.

If the provider does not advertise RP-initiated logout, local logout still
succeeds and returns to the fixed local path. No code constructs a Keycloak or
ZITADEL logout URL. Token revocation is not added unless separately required;
the stated requirement is session logout, and offline refresh-token revocation
semantics differ from back-channel session termination.

### 11.2 Back-channel logout

The endpoint accepts only an `application/x-www-form-urlencoded` POST with a
`logout_token`. Validate it against current OIDC discovery metadata and client
configuration:

- signed token and allowed ID-token signing algorithm; never `none`;
- issuer, client ID audience, signature, `iat`, and `exp`;
- required `jti` and `events` object containing
  `http://schemas.openid.net/event/backchannel-logout`;
- at least one of `sid` or `sub`; and
- absence of `nonce`.

Treat `typ=logout+jwt` as preferred but not mandatory because the standard notes
that requiring it breaks existing providers. Cache a hash of each accepted
`(issuer, jti)` until token expiry to reject replay. On `sid`, invalidate the
matching provider session; with only `sub`, invalidate all indexed sessions for
that issuer and subject. A valid token that matches no live local session still
succeeds. Return 200 for success and 400 for invalid input, always with
`Cache-Control: no-store` and without validation details that aid an attacker.

Do not replace back-channel invalidation with “refresh will notice later.” The
requested `offline_access` refresh token can remain valid after the provider
session ends, so IdP-side logout, another relying party's logout, or an
administrator terminating the provider session might otherwise leave the local
session usable until its bounded local expiry.

## 12. Local TLS and Keycloak

Add an idempotent `scripts/create-development-certificates.sh` that uses
`openssl` to create a local development CA and localhost leaf certificate with
SANs needed by the Compose services. Emit PEM certificate/key files for
Keycloak, a PFX for Kestrel, and a CA bundle used by the containers. Write them
only beneath a gitignored `.certs` directory, use restrictive key permissions,
and never install the CA into the host trust store automatically.

Add a multi-stage API `Dockerfile` whose runtime is the supported .NET 10
chiseled/non-root image. Run as non-root with a read-only root filesystem and
only explicitly writable temporary storage.

Add `compose.yaml` with:

- API HTTPS at `127.0.0.1:65100` and Keycloak HTTPS at
  `127.0.0.1:65101`;
- no unqualified or non-loopback published port;
- a pinned Keycloak image version, an imported realm, and externalized bootstrap
  and client secrets;
- the API and Keycloak sharing the Keycloak service's network namespace so the
  browser, OIDC discovery, and Keycloak back-channel callback can all use the
  same `https://localhost:65100/65101` authorities without host DNS or firewall
  changes; and
- task-local volumes only, with no dependency on a prestarted stack.

The importable development realm contains one enabled confidential OIDC client,
standard code flow only, PKCE S256, exact callback and post-logout URIs, the
back-channel logout URI, offline access support, and an ID-token mapper that
emits a simple `roles` claim. Disable implicit flow, direct-access grants,
service accounts, wildcard redirects, and broad web origins. Development users
and secrets must be conspicuously non-production and injected rather than
committed as usable credentials.

Update launch settings and the `.http` file from the existing out-of-policy
65000 port to HTTPS 65100.

## 13. ZITADEL and provider compatibility contract

Production uses the same application settings and code path as Keycloak. The
ZITADEL application registration must be a web/confidential OIDC application
with exact callback and post-logout redirect URIs, code flow with PKCE, refresh
tokens, roles in the ID token, and the proxy's public HTTPS back-channel logout
URI. Configure its project-role claim name and `object-keys` value shape in the
proxy. Add a ZITADEL-specific role scope only when the tenant does not assert
roles through application token settings; do not add `profile`, `email`, or API
audience scopes without an application requirement.

At startup, discovery must provide authorization, token, signing-key, and
issuer data. RP-initiated logout is used when `end_session_endpoint` is present.
Back-channel support is a deployment gate for the production provider
registration, not a vendor API call in application code.

No implementation module may compare provider names, authority hostnames, or
issuer strings to choose Keycloak/ZITADEL behavior. Differences are expressed
only through standard discovery metadata, client registration, scopes, client
authentication method, and claim-shape configuration.

## 14. Optional authenticated upstream proxying

Use YARP 2.3.0 as an isolated API-layer add-on. Bind a deliberately small
`Proxying` configuration contract rather than exposing YARP's full configuration
schema:

- `Enabled` explicitly controls registration and endpoint mapping;
- each route has a stable `Id`, an absolute inbound `PathPrefix`, one absolute
  HTTPS `Destination`, and an optional `StripPrefix` flag that defaults to
  false; and
- `Enabled=true` requires at least one route, while `Enabled=false` requires the
  route collection to be empty.

Fail startup for duplicate IDs, duplicate or overlapping path prefixes,
collisions with application-owned or OIDC paths, non-HTTPS destinations, or
destination URIs containing user information, a query, or a fragment. Paths are
startup configuration, not user input. A route accepts all HTTP methods; do not
add a method-policy DSL or arbitrary YARP transforms.

Translate each route at startup into one YARP `RouteConfig`, `ClusterConfig`,
and destination using `LoadFromMemory`. Assign the same fixed
authenticated-session authorization policy to every generated route and call
`MapReverseProxy()` only when the module is enabled. `StripPrefix=false`
preserves the complete inbound path. When true, use YARP's prefix-removal
transform to remove exactly the configured match prefix while preserving the
remaining path and query. Do not add a custom `IProxyConfigProvider`, runtime
reload, affinity, or load balancing.

Cookie authentication retrieves the server ticket and its validation event
performs any due refresh before authorization and forwarding. The request
transform authenticates with the cookie scheme, reads `access_token` from the
resulting `AuthenticationProperties`, and sets exactly one outbound
`Authorization: Bearer` value. A missing session or token, or a session
invalidated during refresh, returns 401 without contacting the upstream.

Treat the proxy as a credential boundary:

- replace, never combine with, a browser-supplied `Authorization` header;
- remove the inbound `Cookie` header before forwarding;
- remove every upstream `Set-Cookie` response header; and
- never include access tokens, cookies, ticket properties, or upstream protocol
  bodies in transforms, errors, or logs.

Forward ordinary methods, query strings, request and response bodies, statuses,
and non-credential headers. Origin validation occurs uniformly before routing,
so clients use the same request contract for local and proxied APIs. An upstream
or network failure follows YARP's normal failure response and does not delete or
reject the local session.

Every destination must accept the single access token issued for the login
session, including its audience and scopes. Additional reviewed OIDC scopes or
provider-side audience configuration may satisfy that contract. Upstreams that
need different tokens require a later token-exchange or multi-token design; the
route configuration cannot manufacture or select tokens.

## 15. Logging

Use `Microsoft.Extensions.Logging` source-generated messages with stable event
IDs. Emit one representative event at these boundaries:

- login challenge started and callback succeeded/failed;
- session created, renewed, expired/removed (avoid logging every successful
  lookup);
- refresh attempted, succeeded, invalidated a session, or was request-aborted;
- local logout started/completed;
- back-channel token accepted/rejected plus number of sessions invalidated; and
- proxy request rejected before forwarding or completed/failed upstream.

Use a generated flow ID and, where essential, a short one-way hash of the local
session handle. Never log cookies, state values, nonce, authorization codes,
client secrets, ID/access/refresh/logout tokens, raw `sub`/`sid`/`jti`, complete
claims, or protocol response bodies. Configuration errors may name a missing
setting but not its value.

Retain the generic host's standard console logging provider and configure levels
through the existing `Logging` settings. Application code depends only on
`ILogger<T>`/source-generated logging methods; do not write directly to
`Console`, add a second logging stack, or duplicate application allowlists in
logging infrastructure.

## 16. Verification strategy

Test each policy once at its lowest stable owner.

### Unit tests

- option validation and fixed redirect-path rules;
- public-origin validation, including HTTPS production requirements and
  rejection of non-origin URI components;
- proxy enablement invariants, IDs, path collisions/overlap, HTTPS destinations,
  prefix translation, and fixed authorization-policy assignment;
- role normalization for string, array, ZITADEL object-key, empty, and malformed
  ID-token claims;
- pending-state create/consume/expiry/tamper behavior;
- ticket serialization/protection, renewal, expiry, index maintenance, and stale
  index cleanup using an in-memory `IDistributedCache`;
- refresh decision and failure matrix with an injected HTTP handler, fixed
  `TimeProvider`, opaque non-JWT access/refresh tokens, and locally signed ID
  tokens, proving every completed failure removes the session and request-abort
  cancellation propagates;
- logout-token signature and every required claim rule, replay, `sid` lookup,
  `sub` fan-out, and no-match success; and
- representative log events for required safe fields and forbidden secrets.

### Integration tests

- `/whoami` returns 401 without a ticket and an allowlisted identity with one;
- cookie sign-in stores the ticket server-side, exposes only an opaque cookie,
  becomes anonymous when the cache record is removed, and clears the stale
  browser cookie when a response can be sent;
- `/login` invokes only the OIDC scheme, uses a fixed redirect destination, and
  emits code/PKCE/state/nonce/correlation parameters using `RedirectGet` for the
  authorization request and `form_post` for the callback response mode;
- callback POST rejection for invalid/absent correlation and successful callback
  wiring with a standards-based in-process test issuer;
- `/logout` removes the local session and invokes RP-initiated sign-out without
  exposing the ID token;
- back-channel form POST invalidates an indexed ticket and obeys 200/400 plus
  `no-store` semantics;
- unsafe local and proxied requests accept the exact public origin and return
  403 before dispatch for missing, opaque, malformed, or mismatched origins;
- safe methods need no origin, while OIDC and back-channel protocol ingress
  remain governed by their protocol-specific validation;
- disabled proxying maps no proxy routes; unauthenticated, tokenless, and
  refresh-invalidated requests never contact a task-owned loopback HTTPS
  upstream;
- authenticated proxy requests forward the current access token and ordinary
  HTTP content, overwrite browser authorization, do not forward cookies, and do
  not return upstream cookies;
- preserved and stripped-prefix routes forward the expected path, and an
  upstream failure does not invalidate the session; and
- application service registration uses the distributed cache abstraction and
  provider-neutral OIDC implementation.

Provider contract fixtures should represent Keycloak's array role claim and
ZITADEL's project-role object claim, discovery metadata, logout token shape, and
optional end-session behavior. The proxy fixture owns an HTTPS upstream on a
loopback-bound ephemeral port. Do not create browser end-to-end, smoke, or
deployment tests. Integration fixtures own any process/container they start,
use Docker-assigned ephemeral test ports bound to `127.0.0.1`, and never require
the development Compose stack to be running.

### Canonical validation

Run all applicable checks before handoff:

```text
mise run format
mise run build
mise run test
docker compose config
docker compose build
```

Also inspect generated certificates with `openssl x509` and validate the realm
through Keycloak's native import/startup path in a task-owned integration
fixture. Do not add a bespoke realm/Compose validator where native tools already
prove the invariant.

## 17. Security and operational notes

- The initial deployment supports exactly one active proxy replica, an
  in-process distributed-memory cache, and an ephemeral Data Protection key
  ring. Restart, deployment, or cache loss intentionally ends local sessions,
  pending OIDC transactions, replay records, and logout indexes. A callback
  whose transaction disappeared fails safely and requires a new login; a stale
  session cookie resolves as anonymous and is cleared when possible.
- Normal idle and absolute expiry remain required security controls despite the
  volatile store. Do not add cache recovery, cache migration, session
  restoration, or durable key persistence for the initial implementation.
- Before enabling concurrent replicas, introduce a shared cache and shared
  Data Protection key ring (or prove strict affinity) and revisit atomic index
  updates and concurrent refresh behavior. A shared cache without shared keys
  is insufficient; users cannot repair nondeterministic cross-replica failures
  merely by logging in again.
- A later external cache may let records outlive the proxy, but that is not a
  supported durability guarantee. Authentication code continues to depend only
  on `IDistributedCache` so changing the registration does not change its
  boundary.
- A production back-channel URI must be reachable by ZITADEL over HTTPS. It is
  unauthenticated at HTTP level because the signed logout token authenticates
  the request.
- Session and transaction cache entries contain security material even though
  they are protected. Apply cache access controls, bounded retention, and
  secret-safe telemetry when an external cache is introduced.
- Cookie clearing alone cannot revoke a copied server session reference;
  authoritative ticket deletion, expiry, and back-channel invalidation are the
  required controls.
- `PublicOrigin` is a security boundary and must describe the browser-visible
  proxy origin exactly. Before deploying behind another proxy, configure and
  trust forwarding headers explicitly rather than deriving trust from arbitrary
  client-supplied forwarding headers.
- Proxy routes and destinations are trusted startup configuration. Do not enable
  runtime route input or non-HTTPS destinations because the forwarded access
  token is a bearer credential.

## 18. Primary references

- [ASP.NET Core OIDC web authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0)
- [ASP.NET Core `ITicketStore`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.authentication.cookies.iticketstore?view=aspnetcore-10.0)
- [ASP.NET Core refresh-token support issue 8175](https://github.com/dotnet/aspnetcore/issues/8175)
- [YARP configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-files?view=aspnetcore-10.0)
- [YARP authentication and authorization](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0)
- [YARP transforms](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/transforms?view=aspnetcore-10.0)
- [OpenID Connect Back-Channel Logout 1.0](https://openid.net/specs/openid-connect-backchannel-1_0.html)
- [OpenID Connect RP-Initiated Logout 1.0](https://openid.net/specs/openid-connect-rpinitiated-1_0.html)
- [Keycloak OpenID Connect documentation](https://www.keycloak.org/securing-apps/oidc-layers)
- [ZITADEL back-channel logout](https://zitadel.com/docs/guides/integrate/back-channel-logout)
- [ZITADEL RP-initiated logout](https://zitadel.com/docs/guides/integrate/login/oidc/logout)
- [ZITADEL roles in tokens](https://zitadel.com/docs/guides/integrate/retrieve-user-roles)
