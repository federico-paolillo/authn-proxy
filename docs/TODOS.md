# OIDC authentication proxy implementation todos

Execute these slices in order. A slice is complete only when its listed tests
and validation pass; later slices may extend an earlier test fixture but should
not duplicate its assertions.

## 0. Assess the example inputs (completed during planning)

- [x] Locate `SsoExtensions` in `examples/ssoTokenExtension.txt` and
  `SsoTokenRefresher` in the misspelled `examples/ssoTockenRefresher.txt`.
- [x] Review every material setup and refresh mechanism for .NET 10 API,
  security, provider-neutrality, cache, cancellation, and failure-policy fit.
- [x] Record precise adopt/adapt/reject findings and target concerns in
  `docs/PLAN.md`.
- [x] Confirm the cited ASP.NET Core refresh-token feature request remains open,
  so a cohesive application refresher is still required for .NET 10.

Implementation rule: use the examples as design input only. Do not copy either
file wholesale; the plan identifies the small set of mechanisms worth adapting.

## 1. Establish dependencies and configuration contracts

- [ ] Add the official ASP.NET Core OpenID Connect package aligned with the
  repository's 10.0.x dependency versions and YARP 2.3.0; add no provider SDK.
- [ ] Define provider-neutral options for authority/client credentials, fixed
  paths, scopes, claim mapping, client authentication method, session/refresh/
  transaction lifetimes, cookie settings, and the canonical public HTTPS origin.
- [ ] Define the optional proxy contract with explicit enablement and routes
  containing only an ID, inbound path prefix, HTTPS destination, and optional
  prefix removal.
- [ ] Implement startup validation, including production HTTPS, `__Host-`
  cookie rules, relative local redirects, required secrets, coherent lifetimes,
  exact-origin syntax, proxy enablement consistency, route uniqueness/overlap,
  reserved paths, and trusted destination shape.
- [ ] Register `AddDistributedMemoryCache`, data protection, and
  `TimeProvider.System` through a concern-specific authentication extension.
- [ ] Compose the initial deployment for one active replica with process-local
  cache and ephemeral Data Protection keys; add no session or pending-login
  persistence or recovery options.
- [ ] Unit-test the validation rules without repeating framework validation.

Completion gate: valid configuration starts; each material unsafe or incomplete
configuration fails at startup with a secret-free diagnostic.

## 2. Add local certificates and containerized Keycloak

- [ ] Add the idempotent OpenSSL certificate script and gitignore all generated
  CA, key, certificate, PFX, and password artifacts.
- [ ] Generate localhost/service SANs, restrictive private-key permissions, and
  output formats needed by Kestrel and Keycloak; never modify the system/browser
  trust store.
- [ ] Add a multi-stage .NET 10 image using a non-root chiseled runtime and make
  it compatible with a read-only root filesystem.
- [ ] Add Compose services for the API and a pinned Keycloak release, publishing
  only `127.0.0.1:65100` and `127.0.0.1:65101`.
- [ ] Share the service network namespace (or an equally safe verified design)
  so browser, discovery, token exchange, and Keycloak back-channel logout use
  the same localhost HTTPS authorities without host changes.
- [ ] Add an importable realm with a confidential client, exact URIs, standard
  code flow, PKCE S256, offline access, back-channel logout, and an ID-token
  roles mapper. Disable implicit/direct grants, service accounts, wildcard
  redirects, and broad origins.
- [ ] Keep bootstrap/client/user credentials external and development-only.
- [ ] Change launch settings and `.http` requests from port 65000 to HTTPS 65100.

Completion gate: `docker compose config` and image builds pass, OpenSSL inspection
proves the expected SAN/expiry properties, and Keycloak's native import path
accepts the realm. All published ports are explicitly loopback-bound.

## 3. Store pending login transactions server-side

- [ ] Implement the cache-backed
  `ISecureDataFormat<AuthenticationProperties>` with 256-bit random handles,
  purpose-specific data protection, and short absolute expiration.
- [ ] Consume state on callback and reject missing, malformed, expired,
  tampered, or reused handles.
- [ ] Configure it as `OpenIdConnectOptions.StateDataFormat` without replacing
  framework nonce or correlation validation.
- [ ] Unit-test state round trip, one-time use, expiry, tampering, and cache
  isolation by data-protection purpose.

Completion gate: browser-visible state contains no serialized authentication
properties and only a live server cache entry can complete the flow. Losing the
entry or ephemeral protection key makes the callback fail safely and require a
new login.

## 4. Implement the distributed session ticket store

- [ ] Implement all .NET 10 `ITicketStore` operations with cancellation and an
  injected `IDistributedCache`.
- [ ] Generate opaque 256-bit session IDs, use versioned hashed cache keys,
  serialize with the framework ticket serializer, and data-protect cached
  ticket bytes so cache-only read or write access cannot expose tokens or forge
  trusted ticket data.
- [ ] Apply bounded ticket/idle expiry and make renewal update the authoritative
  server record.
- [ ] Add issuer+`sid` and issuer+`sub` indexes with hashed key material, bounded
  expiry, cleanup on removal, and lazy pruning of stale session IDs.
- [ ] Keep read/modify/write races simple; add no distributed lock.
- [ ] Post-configure the cookie scheme's `SessionStore` with the injected store.
- [ ] Unit-test store/retrieve/renew/remove, corrupt/expired records, index
  lifecycle, and stale index cleanup.

Completion gate: an `AuthenticationTicket` containing known fake tokens round
trips through the cache, while its corresponding cookie/reference and cache key
contain none of those token values. Cache or key loss ends the session safely;
restart survival and recovery are not implemented.

## 5. Configure cookie and OpenID Connect handlers

- [ ] Configure cookie authentication as the default authenticate/sign-in
  scheme and make API challenges return 401/403 rather than login redirects.
  Automatic login navigation remains a frontend-wide 401-interceptor concern,
  outside this repository.
- [ ] Set `__Host-authn-proxy`, Secure, HttpOnly, host-only, `Path=/`, and Lax
  SameSite cookie properties.
- [ ] Configure OIDC code flow, PKCE, discovery, HTTPS metadata, saved server-side
  tokens, `RedirectGet` authorization requests, `form_post` callback response
  mode, disabled UserInfo, explicit claim types, and provider-neutral PAR
  behavior.
- [ ] Configure Secure, HttpOnly, host-only, `SameSite=None` nonce/correlation
  cookies and retain the handler's built-in issuer, audience, signature, state,
  nonce, and correlation checks.
- [ ] Add one origin-validation policy before local and proxy endpoints: every
  method except `GET`, `HEAD`, and `OPTIONS` requires exactly one valid `Origin`
  equal to the configured public origin or returns 403 before dispatch.
- [ ] Exempt only `/signin-oidc`, `/signout-callback-oidc`, and
  `/backchannel-logout`; keep their OIDC or signed-token protections
  authoritative and do not add a custom CSRF header or Referer fallback.
- [ ] Add only `openid offline_access` by default; allow reviewed extra scopes
  through configuration.
- [ ] Add integration coverage for registration, challenge parameters, cookie
  flags, tampered/absent correlation, and a stale cookie whose missing or
  unreadable ticket authenticates as anonymous and is expired in the response.
- [ ] Test exact/missing/opaque/malformed/mismatched origins at the centralized
  boundary, safe-method behavior, and the three protocol exemptions without
  duplicating their protocol-validation tests.

Completion gate: the framework completes a test-issuer code/PKCE callback into a
server-side ticket, and a request without that cache record authenticates as
anonymous.

## 6. Add login and session introspection endpoints

- [ ] Map `GET /login` to an explicit OIDC challenge with the single configured
  post-login destination and no return-url input.
- [ ] Map `GET /whoami` to 401 for no/expired session and an allowlisted response
  for an authenticated session.
- [ ] Return only subject, optional display name, and normalized roles; never
  serialize the principal wholesale or expose authentication properties.
- [ ] Keep callback routes owned by the OIDC handler.
- [ ] Add endpoint integration tests for status codes, fixed redirects, response
  shape, and absence of all fake token values.

Completion gate: the primary flow reaches `/whoami` successfully after callback,
and all browser-facing bodies, headers, and cookies remain token-free.

## 7. Normalize ID-token roles without role authorization

- [ ] Require `sub` and build identity exclusively from the validated ID-token
  principal.
- [ ] Normalize a configured string/string-array role claim and a configured
  object-key role claim into repeated role claims.
- [ ] Keep both configured shapes in one normalizer; do not select by provider
  name or try a chain of extractors. Introduce configuration-selected
  `IRoleClaimExtractor` strategies only if a third material algorithm appears.
- [ ] Configure the Keycloak realm to emit the simple array claim and document
  the production ZITADEL claim name/value-shape settings in application
  configuration examples.
- [ ] Keep malformed values out of the principal with one aggregate, secret-free
  diagnostic.
- [ ] Add no role policies, authorization handlers, or role-gated endpoints.
- [ ] Unit-test both provider claim shapes and malformed/empty inputs.

Completion gate: `ClaimsPrincipal.IsInRole` and `/whoami` reflect ID-token roles
for both provider fixtures while an opaque JWT-looking access token has no
effect on identity.

## 8. Implement token refresh

- [ ] Add an OIDC cookie validation event and a cohesive token refresher using
  current discovery metadata and the configured standard client authentication
  method.
- [ ] Decide refresh from saved `expires_at`; never decode the access or refresh
  token.
- [ ] Send the refresh grant with cancellation, handle protocol/network errors,
  and preserve exception causes.
- [ ] Save new access token/expiry, retain or rotate the refresh token correctly,
  and renew the server ticket.
- [ ] Validate a returned ID token with current issuer/signing/audience/lifetime
  rules before replacing the principal; retain the prior ID-token principal if
  the response legitimately omits an ID token.
- [ ] On every completed refresh failure—including `invalid_grant`, invalid
  protocol data or ID token, discovery failure, IdP `5xx`, timeout, or transport
  failure—reject the principal, delete the authoritative ticket and indexes,
  expire the cookie when possible, and return 401.
- [ ] Propagate request-aborted `OperationCanceledException` instead of
  translating it into an authentication result; do not promise cleanup after
  the client has gone.
- [ ] Do not add refresh locking or single-flight behavior.
- [ ] Unit-test not-due, successful refresh, rotation, optional ID token, every
  completed failure class, and request-aborted cancellation with fixed time,
  opaque tokens, and locally signed ID tokens.

Completion gate: refresh changes only the server ticket, never a browser-visible
contract. Every completed failure removes the session; only request-aborted
cancellation bypasses the authentication outcome.

## 9. Add optional authenticated upstream proxying

- [ ] Keep proxy code under an isolated API-layer `Proxying` concern with its
  own registration and mapping extensions; authentication/session code must not
  reference YARP.
- [ ] When explicitly enabled, translate each validated route into one in-memory
  YARP route, cluster, and HTTPS destination and call `MapReverseProxy()`; when
  disabled, register and map no proxy routes.
- [ ] Require the same fixed authenticated-session authorization policy on every
  generated route and place authorization after authentication. Add no anonymous,
  configurable, or role-based route policy.
- [ ] Preserve inbound paths by default and use YARP's prefix-removal transform
  only when the route requests it. Support all HTTP methods and no general
  rewrite, method-policy, live-reload, affinity, or load-balancing configuration.
- [ ] After cookie validation and any due refresh, retrieve the current saved
  access token and replace the outbound `Authorization` header with exactly one
  bearer value. Return 401 without contacting the upstream if the session or
  token is unavailable.
- [ ] Remove inbound `Cookie` before forwarding and every upstream `Set-Cookie`
  response header. Never expose credentials or ticket properties through proxy
  errors, transforms, or logs.
- [ ] Preserve ordinary methods, query strings, bodies, statuses, and
  non-credential headers. Treat upstream/network failure as a proxy failure,
  not as session invalidation.
- [ ] Unit-test enablement contradictions, route/destination validation, prefix
  translation, and fixed policy assignment.
- [ ] Integration-test disabled mapping, authentication/token rejection before
  upstream contact, current-token forwarding, browser authorization replacement,
  cookie stripping in both directions, preserved/stripped paths, ordinary HTTP
  content, uniform origin enforcement, and session retention on upstream failure
  with a task-owned loopback HTTPS upstream.

Completion gate: enabled routes transparently proxy authenticated browser
requests with the current server-held access token, while unauthenticated or
invalid requests never contact the upstream and no browser credential crosses
the proxy boundary.

## 10. Implement user-initiated logout

- [ ] Map authenticated `POST /logout`; do not add a logout GET.
- [ ] Sign out both cookie and OIDC schemes, delete the authoritative ticket and
  indexes, and use only the fixed local post-logout path.
- [ ] Let the OIDC handler use discovery and the saved server-side ID token hint;
  never build a provider URL or send the hint to frontend code.
- [ ] Fall back to local-only logout when the provider has no advertised
  end-session endpoint.
- [ ] Integration-test local deletion, OIDC sign-out parameters, fixed redirect,
  absent-endpoint fallback, and token non-disclosure.

Completion gate: the prior cookie can no longer resolve a ticket, and supported
providers receive a standards-based RP-initiated logout request with the hint.

## 11. Implement OpenID Connect back-channel logout

- [ ] Retain the feature so provider, administrator, and global logout promptly
  ends local sessions; do not assume `offline_access` refresh will observe
  provider-session logout.
- [ ] Add a form-only POST endpoint for `logout_token` with `Cache-Control:
  no-store` responses.
- [ ] Validate signature/algorithm, issuer, client audience, `iat`, `exp`, `jti`,
  logout event, `sid`/`sub`, and prohibited `nonce` using current OIDC metadata.
- [ ] Accept typed and untyped valid logout tokens for provider compatibility,
  while rejecting `alg=none` and all invalid required claims.
- [ ] Add hashed `(issuer,jti)` replay entries bounded by token expiry.
- [ ] Invalidate the exact issuer+`sid` sessions, or all issuer+`sub` sessions
  when `sid` is absent; use the required secondary indexes because the logout
  token does not carry the local ticket handle, and treat no live match as
  success.
- [ ] Return 200 on success and 400 on invalid request without leaking validation
  details.
- [ ] Unit-test every normative validation rule and session-selection policy;
  integration-test form binding, cache invalidation, replay, and response
  semantics.

Completion gate: Keycloak- and ZITADEL-shaped valid logout tokens remove the
intended server ticket(s), and invalid/replayed tokens cannot affect sessions.

## 12. Add flow-oriented safe logging

- [ ] Define source-generated events for login, callback, session lifecycle,
  refresh success/invalidation/request-abort outcomes, local logout, and
  back-channel acceptance/rejection, plus proxy rejection/completion/failure.
- [ ] Retain the generic host's console logging provider, use `ILogger<T>` only,
  and control verbosity through the existing `Logging` configuration.
- [ ] Use generated flow IDs and only a short one-way local-session correlation
  when essential.
- [ ] Ensure tokens, codes, cookies, state, nonce, secrets, raw provider/user
  identifiers, claims, and protocol bodies are never message parameters.
- [ ] Test one representative event per policy class for event ID, severity,
  required safe fields, and forbidden sentinel secrets; do not assert prose.

Completion gate: operators can follow a successful and failed flow from event
IDs, and sentinel secrets never appear in captured logs.

## 13. Prove provider-neutral behavior and finish wiring

- [ ] Add Keycloak and ZITADEL contract fixtures for discovery, ID-token role
  shapes, token refresh responses, RP-initiated logout metadata, and back-channel
  logout tokens.
- [ ] Verify all differences are expressed through standard discovery,
  registration, scopes, client authentication, or claim-shape configuration.
- [ ] Search for and remove any authority-hostname/provider-name branch and any
  access-token parsing.
- [ ] Keep `Program.cs` as a small composition root and preserve `/ping` unless
  the implementation gives a concrete reason to change it.
- [ ] Confirm the production registration checklist requires a publicly
  reachable HTTPS back-channel URI and roles in the ID token.

Completion gate: the same executable/configuration model accepts both provider
fixture contracts and references no Keycloak/ZITADEL runtime SDK.

## 14. Run the completion audit

- [ ] Run `mise run format`, `mise run build`, and `mise run test` with no errors.
- [ ] Run `docker compose config` and `docker compose build`.
- [ ] Inspect generated certificates with OpenSSL and run the Keycloak realm
  through its native import path.
- [ ] Confirm unit tests start no external processes and every integration
  fixture owns its dependencies and uses loopback-bound ephemeral test ports.
- [ ] Confirm restart/cache/key loss ends sessions and pending logins safely,
  and that no durability, recovery, or concurrent-replica support was added.
- [ ] Search the plan and implementation for stale transient-refresh retention,
  default OIDC challenges, provider-selected role extractors, or assumptions
  that refresh replaces back-channel logout.
- [ ] Confirm proxying cannot expose the full YARP schema, accept anonymous
  routes or non-HTTPS destinations, forward browser credentials, bypass uniform
  origin validation, or introduce a YARP dependency into authentication code.
- [ ] Confirm there are no smoke, browser end-to-end, deployment, or redundant
  structural tests.
- [ ] Audit every row in the acceptance map in `docs/PLAN.md` against current
  code and test/configuration output; gather stronger evidence for any row that
  is merely plausible.
- [ ] Inspect `git diff` for unrelated changes, committed/generated secrets,
  certificate artifacts, provider-specific branches, and accidental token
  exposure.

Completion gate: every acceptance-map row has authoritative evidence, every
applicable canonical command passes, and no requested behavior remains planned
but unimplemented.
