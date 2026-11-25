# Security rules (SEC-NN)

- SEC-01 · Only `RequireSession()` routes authenticate
- SEC-02 · Sessions travel in the `__Host-` cookie or Bearer, never both ambiguously
- SEC-03 · Unsafe cookie requests need a console Origin
- SEC-04 · Credentialed CORS only to configured console origins
- SEC-05 · not-found outranks forbidden; malformed ids look unknown
- SEC-06 · flows of the session module never reveal whether an account exists
- SEC-07 · Secrets travel once and print as `***`
- SEC-08 · Metric tags never carry PII, typed text, origins or ids
- SEC-09 · Personal data travels in bodies, never URLs
- SEC-10 · 500 and health answers reveal nothing internal
- SEC-11 · One least-privilege database role per module and process
- SEC-12 · Secrets are verifiers or env values, never logged or committed
- SEC-13 · `/v1` is rate limited per client; costly routes add a named policy
- SEC-14 · Secret-guarding limits fail closed; enumeration limits fail open
- SEC-15 · `X-Forwarded-*` only from configured proxies
- SEC-16 · An open route that will need auth carries `// TODO(auth): <who>`
- SEC-17 · The cache never holds security state

## SEC-01 · Only `RequireSession()` routes authenticate

- **Rule:** Authenticate only routes that call `RequireSession()`; register no default scheme; anonymous routes never touch the session module.
- **Why:** anonymous traffic never costs a session lookup, and a session-module outage cannot break public routes.
- **Enforced by:** SessionEndpointsTests + review.
- **Good:** `.WithName("ChangePassword")...` on a route in `PasswordChangeEndpoints` behind the session scheme. **Bad:**
  `AddAuthentication(defaultScheme)` applied to every route.
- **Status:** in force.

## SEC-02 · Sessions travel in the `__Host-` cookie or Bearer, never both ambiguously

- **Rule:** A session is the `__Host-frappe-session` cookie (HttpOnly, Secure, SameSite=Lax) or a Bearer token, Bearer winning; a duplicated cookie
  never authenticates; failure is 401 `<module>.session.invalid` with `WWW-Authenticate`.
- **Why:** `__Host-` blocks subdomain cookie injection; a duplicate cookie is a fixation attempt.
- **Enforced by:** SessionEndpointsTests + LogInEndpointTests.
- **Good:** `.SetsSessionCookie()` on log-in. **Bad:** a session cookie without `Secure` or with a `Domain`.
- **Status:** in force.

## SEC-03 · Unsafe cookie requests need a console Origin

- **Rule:** An unsafe request (POST, PUT, PATCH, DELETE) carrying the session cookie needs an `Origin` among `Http:Console:Origins`, else 403
  `http.origin.forbidden`; Bearer requests are not checked.
- **Why:** blocks cross-site request forgery with the ambient cookie.
- **Enforced by:** ConsoleOriginTests.
- **Good:** PATCH `/me` from the console origin. **Bad:** relying on SameSite alone.
- **Status:** in force.

## SEC-04 · Credentialed CORS only to configured console origins

- **Rule:** Answer credentialed CORS only to origins in `Http:Console:Origins`, always with `Vary: Origin`.
- **Why:** a reflected or wildcard origin hands sessions to any site.
- **Enforced by:** HttpConventionsTests.
- **Good:** empty origin list by default. **Bad:** `AllowAnyOrigin().AllowCredentials()`.
- **Status:** in force.

## SEC-05 · not-found outranks forbidden; malformed ids look unknown

- **Rule:** A resource the caller may not see answers like one that does not exist; a non-UUID id answers like an unknown id.
- **Why:** status codes must not reveal which ids exist.
- **Enforced by:** ProblemDetailsMappingTests.
- **Good:** 404 for another tenant's resource. **Bad:** 403 that confirms the id exists; 400 for `/owners/abc`.
- **Status:** in force.

## SEC-06 · flows of the session module never reveal whether an account exists

- **Rule:** Registration never answers 409; log-in has one 401 for every failure; password reset and resend answer 202; code checks have one 422.
- **Why:** prevents account enumeration.
- **Enforced by:** OwnerEndpointsTests + PasswordResetEndpointsTests + LoginFailuresTests.
- **Good:** `TypedResults.Accepted((string?)null, new PasswordResetResponse(...))` for any email. **Bad:** `404 owner not found` on reset.
- **Status:** in force.

## SEC-07 · Secrets travel once and print as `***`

- **Rule:** A session token appears only in the log-in answer and the cookie; types holding secrets, tokens, passwords, codes or names override
  `ToString` to mask; never log request or message payloads.
- **Why:** logs, traces and exceptions are where secrets leak.
- **Enforced by:** FRP001 (`eng/analyzers/Frappe.Analyzers`) at warning in `eng/analyzers.globalconfig`: a record outside `tests/` with a public
  string member named `*Password`, `*Passphrase`, `*Secret`, `*Token`, `*Plaintext` or `*Email` overrides `ToString`. Review for codes, names,
  other types and logging.
- **Good:** `Password.ToString() => "***"`. **Bad:** a record with a token printed by `{@Command}`.
- **Status:** in force.

## SEC-08 · Metric tags never carry PII, typed text, origins or ids

- **Rule:** Metric tags take values from bounded sets only; never emails, names, typed text, origins or ids.
- **Why:** metrics are retained and widely readable; unbounded tags also explode cardinality.
- **Enforced by:** review.
- **Good:** `frappe.outcome = "dead_lettered"`. **Bad:** `origin = request.Headers.Origin`.
- **Status:** in force.

## SEC-09 · Personal data travels in bodies, never URLs

- **Rule:** Personal ids and data go in request bodies, never paths or query strings; URLs that must carry PII are redacted in traces.
- **Why:** URLs land in access logs, proxies, traces and browser history.
- **Enforced by:** review.
- **Good:** `POST /address-validations` with the address in the body. **Bad:** `GET /owners?email=...`.
- **Status:** in force.

## SEC-10 · 500 and health answers reveal nothing internal

- **Rule:** A 500 never carries the exception; health answers name no hosts, errors or connection strings.
- **Why:** internals help an attacker map the system.
- **Enforced by:** HttpConventionsTests.
- **Good:** `500 http.unexpected` with only `traceId`. **Bad:** `detail: ex.Message`.
- **Status:** in force.

## SEC-11 · One least-privilege database role per module and process

- **Rule:** Each module has one login role with data-only grants on its own schema, PUBLIC revoked, no access to the migrations history; the Worker
  connects as the narrow `<module>_worker` role, never the full module role: `RelayRoles` grants (relay and purge, MIG-12) plus table- or
  column-scoped extras only where it does more (`NotificationsDatabase.WorkerStatements`, `IdentityDatabase.GrantWorkerAsync`); DDL only in the
  Migrator.
- **Why:** a compromised or buggy module cannot read others' data or change schemas.
- **Enforced by:** review (`ModuleRoles`, `RelayRoles`) + `MigratorTests` + `WorkerCompositionTests`.
- **Good:** `frappe_identity` with DML on `identity` only; the Worker connecting as `frappe_identity_worker` and `frappe_notifications_worker`
  (compose.yaml). **Bad:** the Worker connecting as the full `frappe_identity` role, or the Api as the owner role.
- **Status:** in force.

## SEC-12 · Secrets are verifiers or env values, never logged or committed

- **Rule:** Role passwords are stored as SCRAM verifiers; secrets come only from environment or deploy files, are never logged or committed, and a
  missing one stops startup.
- **Why:** leaked repos and logs must not leak credentials; a missing secret must fail loudly.
- **Enforced by:** gitleaks (`security.yml`) + options validation at startup.
- **Good:** `Roles__Identity__Password: ${FRAPPE_IDENTITY_PASSWORD:?set it in .env}` in compose. **Bad:** a default key in `appsettings.json`.
- **Status:** in force.

## SEC-13 · `/v1` is rate limited per client; costly routes add a named policy

- **Rule:** All of `/v1` is limited per client in Valkey (120/min); costly routes add a named policy with a `DefaultRateLimit` and the section
  `<Module>:<Feature>:RateLimit`.
- **Why:** brute force and scraping cost the attacker, not the database.
- **Enforced by:** HttpConventionsTests + RateLimitTests.
- **Good:** `.RequireRateLimiting(LogInRateLimitPolicy)` with `<SessionModule>:Login:RateLimit`. **Bad:** a login route under only the global limit.
- **Status:** in force.

## SEC-14 · Secret-guarding limits fail closed; enumeration limits fail open

- **Rule:** When Valkey is down, limits that guard secrets (login failures, code attempts) refuse; limits that only slow enumeration let requests
  through.
- **Why:** a cache outage must not open brute force, nor take registration down.
- **Enforced by:** LoginFailuresTests + CodeCountersTests.
- **Good:** 503 `<module>.login.check_unavailable` while failures cannot be counted. **Bad:** skipping the failure counter when Valkey times out.
- **Status:** in force.

## SEC-15 · `X-Forwarded-*` only from configured proxies

- **Rule:** Honor `X-Forwarded-*` only from proxies in `Http:ForwardedHeaders`.
- **Why:** a spoofed client IP bypasses per-client rate limits.
- **Enforced by:** review.
- **Good:** `ApiComposition` clears the defaults and adds only `Http:ForwardedHeaders:KnownProxies`/`KnownNetworks`. **Bad:** trusting forwarded
  headers from any address.
- **Status:** in force.

## SEC-16 · An open route that will need auth carries `// TODO(auth): <who>`

- **Rule:** A route that must be restricted but cannot be yet carries `// TODO(auth): <who may call it>; open while Frappé has no authentication`
  above its mapping.
- **Why:** every open-by-necessity route stays findable for the access module.
- **Enforced by:** review.
- **Good:** `// TODO(auth): administration only; ...` in `<Module>AdminEndpoints`. **Bad:** an admin route open with no marker.
- **Status:** in force. Every admin route left open on purpose carries the marker.

## SEC-17 · The cache never holds security state

- **Rule:** Never cache sessions, permissions, credentials or any state an authorization decision reads; read them from their source every time.
- **Why:** a stale cached session or grant keeps access alive after revocation.
- **Enforced by:** review.
- **Good:** `RequireSession()` reading the session store per request. **Bad:** a `Cached*` decorator over the session port.
- **Status:** in force.
