# Questions and answers on the OIDC proxy plan

This assessment is based on `docs/PLAN.md`, the todo list (currently named
`docs/TODOS.md`), and both files under `examples/`.

## 1. Can an unauthenticated request redirect the browser to the IdP automatically?

### Question

Can the original default-challenge idea from `SsoExtensions` be retained so
that an unauthenticated request automatically sends the browser to the identity
provider? If so, can this replace the planned `401` response while retaining
`/whoami` for session introspection? How does this relate to
`AuthenticationMethod=FormPost` and `ResponseMode=form_post`?

### Answer

It is possible only when the request is a **top-level browser navigation**. If
such a request challenges the OIDC scheme, the handler can return a temporary
redirect and the browser will navigate to the IdP. This is the conventional
server-rendered web-application behavior.

It does not work transparently for the usual SPA API request made with
`fetch` or XHR. A `401` has no standard instruction that makes a browser
navigate. Replacing it with a `302` makes `fetch` process the redirect as part
of that fetch operation; it does not replace the top-level document. The
resulting cross-origin request can then encounter CORS restrictions or return
IdP HTML to code that expected JSON. Redirecting unsafe API operations also
does not preserve and safely replay the operation after login. Fetches and
document navigations are deliberately distinct browser request modes, and
.NET 10 likewise defaults known API endpoints to `401`/`403` rather than cookie
login redirects. See the [browser fetch-mode
distinction](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Sec-Fetch-Mode),
[Fetch redirect behavior](https://developer.mozilla.org/en-US/docs/Web/API/Response/redirected),
and [.NET 10 API endpoint authentication
behavior](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/api-endpoint-auth?view=aspnetcore-10.0).

The simple, standard way to get the desired user experience is therefore:

1. APIs, including `/whoami`, return `401` for an absent or invalid session.
2. One frontend response interceptor handles `401` by assigning
   `window.location` to `/login`.
3. `/login` explicitly challenges the OIDC scheme and returns the temporary
   redirect to the IdP.

This is still automatic from the user's perspective, but the frontend changes
the top-level location explicitly. It also provides one place to prevent
redirect loops and to exempt requests for which a login navigation would be
inappropriate. `/whoami` remains useful and unchanged.

`AuthenticationMethod` and `ResponseMode` govern opposite legs of the OIDC
round trip:

- `AuthenticationMethod=RedirectGet` makes the proxy send the authorization
  request to the IdP through an ordinary browser redirect. This is what
  `/login` should use.
- `AuthenticationMethod=FormPost` would instead make the proxy return an HTML
  auto-submit form for that outbound authorization request. It is unnecessary
  here and does not solve API-fetch navigation.
- `ResponseMode=form_post` tells the IdP to return its authorization response
  by posting a form to `/signin-oidc`. It is independent of
  `AuthenticationMethod` and can remain enabled with `RedirectGet`.

ASP.NET Core 10 defaults these options to `RedirectGet` and `form_post`,
respectively, as shown in the [framework's OIDC options
source](https://github.com/dotnet/aspnetcore/blob/v10.0.1/src/Security/Authentication/OpenIdConnect/src/OpenIdConnectOptions.cs).

**Recommendation for the plan:** retain explicit `/login`, API `401`
responses, `AuthenticationMethod=RedirectGet`, and
`ResponseMode=form_post`. Mention that a frontend-wide `401` interceptor may
initiate `/login` when an automatic user experience is wanted. Do not restore
OIDC as the default challenge for all API calls.

## 2. Can every refresh failure invalidate the session?

### Question

Can availability deliberately be reduced by treating every refresh failure,
including a timeout, transport failure, discovery failure, or IdP `5xx`, as
session loss instead of retaining an unexpired access token for a later retry?

### Answer

Yes. Retrying later is an availability policy, not an OIDC correctness
requirement. The project can adopt a deliberately fail-closed policy: once a
refresh is due, any unsuccessful refresh attempt deletes the authoritative
ticket, expires the browser cookie when a response can still be sent, and
causes the request to receive `401`.

That policy removes the need for a transient-versus-terminal session-retention
matrix. It does **not** remove the need to validate successful token responses,
propagate cancellation, preserve exception causes, or delete the server-side
ticket correctly. A request-aborted `OperationCanceledException` should be
propagated rather than translated into an authentication result: the client is
already gone, and cancellation can also prevent reliable cleanup. This narrow
exception is request-lifecycle handling, not an attempt to preserve session
availability.

The operational cost is substantial even if the code saving is modest:

- a brief IdP, DNS, discovery, or network outage logs out every user whose
  request enters the refresh window;
- a user may lose a session while the old access token is still valid;
- redirecting immediately back to login while the IdP is unavailable can
  produce a login loop unless the frontend handles repeated failure; and
- an outage near a common token-expiry boundary can create a burst of new
  login attempts.

OAuth distinguishes invalid grants from temporary failures, so preserving an
otherwise usable token is a conventional resilience choice rather than
needless protocol machinery. The distinction is visible in [OAuth 2.0 token
endpoint errors](https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2).

**Recommendation for the plan:** accept the requested simplification. Replace
the transient-retention policy with “any completed refresh failure invalidates
the local session,” retain the request-abort cancellation carve-out, and reduce
the refresh tests and log outcomes accordingly. Record the outage-wide logout
and possible login-loop behavior as an intentional tradeoff.

## 3. Must cached session and transaction data be data-protected?

### Question

Is it necessary to data-protect serialized cache records when the intended
Memcached cluster is dedicated to `authn-proxy`, placed in the same Compose
deployment, and not exposed elsewhere? Would removing protection simplify the
design safely?

### Answer

It is not mandatory for OIDC interoperability or for `ITicketStore`. The proxy
can serialize a ticket directly into a trusted cache and still function.

Without protection, however, that cache is a plaintext credential repository.
The ticket contains the principal and saved ID, access, and refresh tokens.
Read access to the cache can therefore disclose bearer credentials; write
access can alter trusted claims or ticket properties. Not publishing a
Memcached port reduces exposure, but it does not protect against a compromised
container on the same network, an accidental future network attachment,
operator access, a cache diagnostic or dump, or a Memcached vulnerability.
“Dedicated and unpublished” is useful access control, but it is not equivalent
to cryptographic confidentiality and integrity.

Purpose-specific ASP.NET Core Data Protection means that a cache-only
compromise yields ciphertext and that modified records fail to unprotect. It
does not help once both the proxy and its key ring are compromised. These are
exactly the confidentiality, integrity, and component-isolation properties for
which [ASP.NET Core Data
Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction?view=aspnetcore-10.0)
is intended.

The incremental application complexity is small: the design already needs
Data Protection for authentication cookies and the planned protected OIDC
state. The ticket codec adds one purpose string plus `Protect`/`Unprotect`
around the framework serializer. Key persistence is a separate availability
choice. An ephemeral key ring can intentionally make old cache entries
unreadable after restart, which is compatible with the stated willingness to
lose sessions.

Pending OIDC transaction records contain trusted authentication properties
and should remain protected as well. Losing them on restart is acceptable;
allowing cache readers or writers to inspect or forge them is a different,
security-relevant decision.

**Recommendation for the plan:** keep purpose-specific protection for session
and transaction records. It buys meaningful defense in depth at little design
cost and is independent of whether sessions survive restarts. If it is removed,
the cache must explicitly be classified and operated as a high-value secret
store, and cache write access becomes part of the authentication trust
boundary.

## 4. Should role extraction use polymorphic provider implementations?

### Question

Can role extraction use an interface such as `IExtractRoles`, with one
implementation per IdP selected by configuration, or a chain of extractors
that tries each supported representation?

### Answer

Polymorphism is possible, but implementations should represent **claim
shapes**, not IdP brands, and they should consume only already validated
ID-token claim data. An interface taking an arbitrary `JwtToken` risks making
validation ownership unclear and encourages reparsing tokens. Access and
refresh tokens must remain opaque.

If the extraction rules grow, a reasonable design would be an
`IRoleClaimExtractor` strategy selected by a validated configuration value
such as `StringOrArray` or `ObjectKeys`. Exactly one configured strategy should
run. This preserves the plan's useful property that the same IdP can be
configured differently and that adding an IdP using an existing shape requires
no code change.

A chain of responsibility is less suitable. Trying formats until one succeeds
makes configuration errors ambiguous, makes extractor ordering observable, and
can silently accept a shape that the deployment was not intended to trust.
Selecting a provider-named implementation has similar drawbacks: Keycloak and
ZITADEL claim layouts are configurable, so provider name does not uniquely
determine the extraction rule.

For the two very small shapes currently required, a single role normalizer
with a switch on a validated shape enum remains the most cohesive and simplest
implementation. Introducing an interface, dependency-injection registration,
and multiple classes would add structure without removing branching or making
the current rules clearer.

**Recommendation for the plan:** keep the configured claim name and
claim-shape model and the single normalizer for now. If a third materially
different extraction algorithm appears, split the existing shape cases into
configuration-selected `IRoleClaimExtractor` strategies. Do not select by IdP
name and do not use a try-every-extractor chain.

## 5. Is a secondary session index mandatory for back-channel logout?

### Question

Must back-channel logout maintain `(issuer, sid)` and `(issuer, sub)` indexes,
or is there a simpler solution? If the index is unavoidable, what is lost by
dropping back-channel logout entirely?

### Answer

Some equivalent lookup structure is necessary under the current constraints.
The logout token identifies provider sessions by `iss` plus `sid` and/or
`sub`; it does not contain the proxy's random local `ITicketStore` handle.
`IDistributedCache` offers key lookup but no portable enumeration or query.
The proxy therefore needs a mapping from the provider identifier to the local
ticket handle. Calling it something other than a secondary index or embedding
it in a different cache layout does not remove that responsibility. A
queryable session database could replace the explicit index, but would be a
larger design.

This follows the back-channel logout protocol itself: after validating a
logout token, the RP must locate and clear the sessions identified by `iss`
and `sub` and/or `sid`; how it performs that lookup is application-specific.
See [OpenID Connect Back-Channel Logout
1.0](https://openid.net/specs/openid-connect-backchannel-1_0.html#Backchannel).

Dropping back-channel logout permits removal of the secondary indexes, logout
token validator, replay cache, endpoint, related configuration, and most of
the associated tests. User-initiated `POST /logout` can remain and still clear
the current local session and request RP-initiated logout at the IdP.

What is lost is IdP-initiated/global logout. In particular:

- logging out at the IdP or through another relying party does not promptly
  end this proxy's local session;
- an administrator's provider-side session termination is not automatically
  reflected here unless the provider also revokes credentials and a later
  refresh observes that fact;
- a copied proxy session cookie remains useful until local expiry or another
  local invalidation event; and
- local identity and roles can remain accepted until the session is otherwise
  revalidated.

This matters especially because the design requests `offline_access`. The
back-channel logout specification says offline refresh tokens normally should
not be revoked merely because the provider session logged out. It is therefore
unsafe to assume that the next refresh will always discover the logout.
Shorter local absolute and idle lifetimes can bound, but not eliminate, this
gap.

**Recommendation for the plan:** if prompt global/provider-initiated logout is
a product or security requirement, keep back-channel logout and the indexes;
the proposed index is the simplest storage-independent approach. If local
logout plus bounded session expiry is sufficient, drop back-channel logout
explicitly. This is the largest legitimate design simplification among these
questions, but it changes observable logout semantics rather than merely
reducing availability.

## 6. Can session survival across cache or proxy failures be abandoned?

### Question

Can the design be simplified if sessions and pending login transactions are
allowed to disappear whenever the cache or proxy restarts, and users simply
log in again after failures or service changes?

### Answer

Yes. The plan can explicitly target one active proxy replica with no session
durability guarantee:

- keep `IDistributedCache` as the application boundary but initially register
  the process-local distributed-memory implementation;
- keep server-side `ITicketStore`, because it is what prevents tokens from
  being placed in the browser cookie;
- allow session tickets, pending OIDC transactions, replay entries, and any
  retained indexes to disappear with the cache;
- use an ephemeral Data Protection key ring, so a proxy restart can invalidate
  old cookie references and protected cache records; and
- do not add distributed locks, durable cache recovery, cache migration, or
  session restoration.

A callback whose pending transaction was lost should fail safely and require a
new login. Stale browser cookies after a restart should resolve as anonymous
and be cleared. Normal idle and absolute expiry rules are still security
controls and should not be removed merely because the store is volatile.

There is an important distinction between **failover** and **simultaneously
active replicas**. If multiple replicas serve requests at the same time, they
must be able to read the same tickets and Data Protection payloads, or routing
must provide strict affinity. Otherwise authentication fails nondeterministically
as consecutive requests land on different replicas; asking the user to log in
again cannot fix it. Sharing a cache without sharing the Data Protection key
ring is also insufficient. ASP.NET Core documents shared/persistent key rings
for containers and web farms in its [Data Protection configuration
guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0#persisting-keys-when-hosting-in-a-docker-container).

A dedicated external Memcached deployment can still be introduced later
through `IDistributedCache`. It may let tickets outlive a proxy process, but the
application need not promise that behavior. If keys are intentionally
ephemeral, cached tickets from an earlier process become harmless stale data
and expire naturally.

**Recommendation for the plan:** declare a single-active-replica, volatile-
session deployment model for the initial implementation. Keep the cache
abstraction, ticket store, Data Protection, and bounded expirations, but omit
durability and recovery work. State explicitly that a restart, cache loss, or
deployment ends sessions and in-progress logins. Revisit shared cache and key
ring configuration only before enabling concurrent replicas.
