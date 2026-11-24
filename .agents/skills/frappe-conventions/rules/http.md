# HTTP rules (HTTP-NN)

- HTTP-01 · Public routes live under `/v1` in the shared group
- HTTP-02 · Each module maps its own routes and OpenAPI tag; supporting areas keep a namespace, business resources are flat
- HTTP-03 · One `<Feature>Endpoints` class per feature, one record per file
- HTTP-04 · Every operation is named, summarized, described and declares its problems
- HTTP-05 · Numbers in descriptions come from the enforcing constants
- HTTP-06 · Handlers answer `Results<TSuccess, ErrorsProblemHttpResult>` via `ToTypedHttpResult`
- HTTP-07 · kebab-case plural nouns; actions as sub-resources
- HTTP-08 · Caller-scoped routes under `/me` or `/sessions/current`
- HTTP-09 · Status codes per verb: 201/202 + Location, 204, 200 PATCH
- HTTP-10 · PATCH is JSON Merge Patch only, all or nothing
- HTTP-11 · Optional `If-Match` with strong ETag; never 428
- HTTP-12 · Every endpoint declares `CacheFor` or `NoStore`
- HTTP-13 · Every GET answers HEAD
- HTTP-14 · Every list that can grow pages with platform `Page<T>`
- HTTP-15 · Lists are wrapped in an object named by the plural
- HTTP-16 · Query parameters bind as `string?` with `[DocumentedParameter]`
- HTTP-17 · camelCase JSON, strict numbers, snake_case codes
- HTTP-18 · Errors are RFC 9457 Problems with `urn:frappe:problem:<slug>`
- HTTP-19 · Error codes `<module>.<subject>.<reason>`, texts per language
- HTTP-20 · One fixed category precedence decides the status
- HTTP-21 · 429/503 carry Retry-After; database outage is 503
- HTTP-22 · Framework failures are Problems with `http.*` codes
- HTTP-23 · Accept-Language negotiated; source data never translated
- HTTP-24 · `/health/*` stays outside `/v1` and OpenAPI
- HTTP-25 · Every enumerated value is `{code, name}`
- HTTP-26 · Retryable creations accept `Idempotency-Key` (opt-in)
- HTTP-27 · HTTP `max-age` never exceeds the data's freshness
- HTTP-28 · Slow work answers 202 + Location when the client must follow it

## HTTP-01 · Public routes live under `/v1` in the shared group

- **Rule:** Map every public route inside the `/v1` group built in `ApiComposition`, which sets CORS, HEAD, language negotiation, rate limiting and
  the declared 429.
- **Why:** a route outside the group silently loses rate limiting, CORS and language handling.
- **Enforced by:** HttpConventionsTests.
- **Good:** `var v1 = app.MapGroup("/v1")...; v1.MapOrdersEndpoints();` **Bad:** `app.MapGet("/regions", ...)`.
- **Status:** in force.

## HTTP-02 · Each module maps its own routes and OpenAPI tag; supporting areas keep a namespace, business resources are flat

- **Rule:** A module exposes `Map<Module>Endpoints` in `<Module>Http.cs`, maps its own routes and sets one OpenAPI tag. The area decides, not the
  resource: a supporting module (a reference-data module like Orders, or one whose nouns are generic like Billing) keeps a module namespace
  (`/v1/orders`,
  `/v1/billing`); every resource of the business domain, its own catalogs included, is a flat plural noun at `/v1` (`/v1/shipping`,
  `/v1/business-types`). A collection's children are only its items; a catalog is a sibling collection (`/v1/business-types`, never
  `/v1/shipping/types`; AIP-122). No two modules own routes under the same first segment.
- **Why:** hosts compose modules without knowing their routes; the document groups operations by module; a namespace disambiguates generic nouns
  (`/v1/billing/sessions`, like Stripe's `/v1/checkout/sessions`) while domain nouns are already unambiguous.
- **Enforced by:** `RouteOwnershipTests` (Api integration tests: fails when two OpenAPI tags own routes under the same first segment after `/v1`) +
  review for the namespace-or-flat choice.
- **Good:** `OrdersHttp.MapOrdersEndpoints(v1)` → `/v1/orders/...` tagged `Orders`; `ShippingHttp.MapShippingEndpoints(v1)` → `/v1/business-types`
  tagged
  `Business`. **Bad:** a module adding routes to another module's group or first segment; `/v1/shipping/types`.
- **Status:** in force.

## HTTP-03 · One `<Feature>Endpoints` class per feature, one record per file

- **Rule:** Put routes in `<Feature>/<Feature>Endpoints.cs`, an `internal static class` with `Map<Feature>Endpoints(RouteGroupBuilder)`; each
  request/response record in its own file.
- **Why:** a feature's HTTP surface is found by name and reviewed in one place.
- **Enforced by:** SA1402/SA1649 + review.
- **Good:** `Territory/TerritoryEndpoints.cs`, `Territory/AreaListResponse.cs`. **Bad:** response records nested in the endpoints class.
- **Status:** in force.

## HTTP-04 · Every operation is named, summarized, described and declares its problems

- **Rule:** Chain `WithName` (PascalCase operationId), `WithSummary`, `WithDescription` and `ProducesProblem` for every non-default status.
  The OpenAPI contract is generated from the hosts' real module lists, so a module route missing from it fails OpenApiContractTests.
- **Why:** the OpenAPI document is the client's only manual; clients generate code from operationIds.
- **Enforced by:** Spectral (planned) + OpenApiContractTests.
- **Good:** `.WithName("LogIn").WithSummary("Log in").WithDescription(...).ProducesProblem(401)`. **Bad:** a `MapGet` with no metadata.
- **Status:** in force.

## HTTP-05 · Numbers in descriptions come from the enforcing constants

- **Rule:** Build description numbers (limits, lengths, windows) from the constant the code enforces, never a literal.
- **Why:** a literal drifts from the real limit and the document lies.
- **Enforced by:** review.
- **Good:** `$"... ({Describe(DefaultRateLimit)} by default)"` in `OwnerEndpoints`. **Bad:** `"10 requests per minute"`.
- **Status:** in force.

## HTTP-06 · Handlers answer `Results<TSuccess, ErrorsProblemHttpResult>` via `ToTypedHttpResult`

- **Rule:** Endpoint delegates return `Results<TSuccess, ErrorsProblemHttpResult>` built with `ToTypedHttpResult`; `ResultHttpExtensions` is the only
  place that maps errors to status codes.
- **Why:** one mapper keeps status codes, precedence and Problem shape identical across modules.
- **Enforced by:** LayeringTests + ProblemDetailsMappingTests.
- **Good:** `(await handler.HandleAsync(q, ct)).ToTypedHttpResult(v => TypedResults.Ok(ToResponse(v)))`. **Bad:** `if (r.IsFailure) return
  Results.NotFound();`.
- **Status:** in force.

## HTTP-07 · kebab-case plural nouns; actions as sub-resources

- **Rule:** Paths are kebab-case plural nouns; an action is a sub-resource noun, never a verb.
- **Why:** predictable URLs; verbs belong to HTTP methods.
- **Enforced by:** Spectral (planned).
- **Good:** `POST /owners/{id}/verification`, `POST /shipment-syncs/{sourceId}/runs`. **Bad:** `POST /owners/{id}/verify`.
- **Status:** in force.

## HTTP-08 · Caller-scoped routes under `/me` or `/sessions/current`

- **Rule:** Routes about the caller live under `/me` or `/sessions/current`; the caller's own principal id and current session id come from the
  authenticated request, never from the URL.
  The ids of the caller's own sub-resources under `/me/...` are allowed when another principal's id answers 404 (SEC-05).
- **Why:** an id in the URL invites one caller to address another's data.
- **Enforced by:** review.
- **Good:** `PATCH /v1/billing/me`; `DELETE /v1/identity/me/sessions/{sessionId}` (a foreign session id answers 404).
  **Bad:** `PATCH /v1/billing/owners/{ownerId}` for the caller's own profile.
- **Status:** in force.

## HTTP-09 · Status codes per verb: 201/202 + Location, 204, 200 PATCH

- **Rule:** Creation answers 201 + Location; accepted async work 202 (see HTTP-28); no body 204; PATCH 200 with the whole resource. Use
  `WithoutLocation()` when a 201 has no addressable resource or a 202 must not be followed (HTTP-28, SEC-06).
- **Why:** clients follow standard semantics without reading prose.
- **Enforced by:** OpenAPI Location transformer + review.
- **Good:** `TypedResults.Created(links.GetPathByName(...), body)`. **Bad:** `200 OK` for a creation.
- **Status:** in force.

## HTTP-10 · PATCH is JSON Merge Patch only, all or nothing

- **Rule:** PATCH accepts only `application/merge-patch+json` (else 415 with `Accept-Patch`); unknown or duplicate members are 400; every member is
  validated before anything changes.
- **Why:** one patch format; partial writes would leave the resource half-changed.
- **Enforced by:** HttpConventionsTests + MePatchEndpointTests.
- **Good:** `.Accepts<MeMergePatch>(MergePatch.MediaType).AdvertisesMergePatch()`. **Bad:** accepting `application/json` on PATCH.
- **Status:** in force.

## HTTP-11 · Optional `If-Match` with strong ETag; never 428

- **Rule:** Resources a client edits send a strong ETag; `If-Match` is optional, a mismatch answers 412 `precondition-failed`; never require it with
  428.
- **Why:** lost-update protection without breaking clients that do not send it.
- **Enforced by:** MePatchEndpointTests.
- **Good:** `.SendsETag()` on GET, `.ReadsIfMatch()` on PATCH. **Bad:** a weak ETag or 428 when `If-Match` is missing.
- **Status:** in force.

## HTTP-12 · Every endpoint declares `CacheFor` or `NoStore`

- **Rule:** Each endpoint states `CacheFor(lifetime)` or `NoStore()`; personal, administrative and Billing answers are `no-store`; errors, 429 and
  health are never cached. A `no-store` answer may still carry an opaque strong `ETag` (not a cache validator, no 304): compute it with
  `RepresentationTag.Of` over the bytes sent and declare it with `SendsNoStoreETag`; a domain version used for `If-Match` (Billing's profile) stays
  the module's own concept.
- **Why:** an undeclared endpoint gets whatever a proxy guesses, including personal data.
- **Enforced by:** HttpConventionsTests + OpenAPI caching transformer.
- **Good:** `.CacheFor(CacheLifetimes.ReferenceData)`; `.NoStore()` on address suggestions; `GET /v1/shipping/{id}` with `NoStore()` +
  `RepresentationTag`. **Bad:** neither; a module copy of the tag recipe.
- **Status:** in force.

## HTTP-13 · Every GET answers HEAD

- **Rule:** Every GET also answers HEAD; the OpenAPI document lists only the GET.
- **Why:** clients and proxies probe cheaply without a second route.
- **Enforced by:** HttpConventionsTests.
- **Good:** the `/v1` group adds HEAD. **Bad:** a hand-written `MapMethods(..., ["HEAD"])`.
- **Status:** in force.

## HTTP-14 · Every list that can grow pages with platform `Page<T>`

- **Rule:** A list whose size is not fixed by nature pages with `Page<T>` from `Frappe.Http`: `cursor`, `limit` 1–200 default 50, body `{<plural>:
  [...], nextCursor}` with `nextCursor` absent on the last page; errors `platform.page_limit.*` and `platform.page_cursor.invalid`.
- **Why:** an unpaged list that grows becomes a slow, unbounded answer; one shape and one set of codes for every client.
- **Enforced by:** review.
- **Good:** `GET .../localities?limit=50&cursor=...` → `{"localities":[...],"nextCursor":"..."}`. **Bad:** `{"runs":[...]}` with every sync run;
  `orders.page_limit.invalid`.
- **Embedded collections:** a collection embedded in its parent's answer is bounded by a per-parent cap, never paged.
- **Status:** in force.

## HTTP-15 · Lists are wrapped in an object named by the plural

- **Rule:** A list answer is an object whose single array member is the resource's plural; never a bare array.
- **Why:** the wrapper can gain `nextCursor` or metadata without a breaking change.
- **Enforced by:** Spectral (planned).
- **Good:** `RegionListResponse(IReadOnlyList<RegionResponse> Regions)`. **Bad:** `Ok<IReadOnlyList<RegionResponse>>`.
- **Status:** in force.

## HTTP-16 · Query parameters bind as `string?` with `[DocumentedParameter]`

- **Rule:** Bind query parameters as `string?` documented with `[DocumentedParameter]`; the use case parses them and returns translated errors.
- **Why:** framework binding failures answer untranslated 400s outside the error catalog.
- **Enforced by:** review.
- **Good:** `[DocumentedParameter("1 to 200")] string? limit`. **Bad:** `int limit` bound by ASP.NET.
- **Platform parameters:** platform families (`limit`, `cursor`) are bound at the endpoint by `Frappe.Http` (`PageRequest.Bind`) with translated
  `platform.page_*` errors; the query receives typed values.
- **Status:** in force.

## HTTP-17 · camelCase JSON, strict numbers, snake_case codes

- **Rule:** JSON is camelCase with strict number handling; every enumerated code on the wire is a snake_case string inside `{code, name}` (HTTP-25).
- **Why:** one serialization contract for every client; numeric enums break when reordered.
- **Enforced by:** Spectral (planned) + OpenApiContractTests.
- **Good:** `"status": {"code": "pending_verification", "name": "..."}`. **Bad:** `"Status": 1`.
- **Status:** in force.

## HTTP-18 · Errors are RFC 9457 Problems with `urn:frappe:problem:<slug>`

- **Rule:** Every error is a Problem with `type = urn:frappe:problem:<category>`, `errors[]` of `{code, parameters, message}`, `instance` and
  `traceId`.
- **Why:** clients branch on `type` and `code`, never on prose.
- **Enforced by:** ProblemDetailsMappingTests.
- **Good:** `{"type":"urn:frappe:problem:not-found","errors":[{"code":"orders.region.not_found",...}]}`. **Bad:** `{"error":"Not found"}`.
- **Status:** in force.

## HTTP-19 · Error codes `<module>.<subject>.<reason>`, texts per language

- **Rule:** Codes are snake_case `<module>.<subject>.<reason>`, constants in `<Module>ErrorCodes`, texts in `Resources/Messages/<lang>.json` (every
  code has `en`), limits in `parameters`.
- **Why:** codes are a contract; texts change without breaking clients.
- **Enforced by:** ProblemDetailsMappingTests + review.
- **Good:** `OrdersErrorCodes.RegionNotFound = "orders.region.not_found"`. **Bad:** a code built inline or a limit baked into the message.
- **Status:** in force.

## HTTP-20 · One fixed category precedence decides the status

- **Rule:** When errors of several categories occur, the status follows: unavailable > too-many-requests > unauthenticated > not-found > forbidden >
  conflict > rule > invalid > precondition-failed; a malformed request stays 400 even with a dependency down.
- **Why:** the answer never depends on the order errors were collected and never leaks more than the caller may know.
- **Enforced by:** ProblemDetailsMappingTests.
- **Good:** `ResultHttpExtensions.ShapeOf`. **Bad:** an endpoint picking the status of the first error.
- **Status:** in force.

## HTTP-21 · 429/503 carry Retry-After; database outage is 503

- **Rule:** 429 and 503 send `Retry-After` and `parameters.retryAfter` (503: 30 s); a database outage answers 503 `platform.database.unavailable`; a
  constraint refusal stays 500.
- **Why:** clients back off correctly; a constraint refusal is a bug, not an outage.
- **Enforced by:** DatabaseOutageTests.
- **Good:** `503` + `Retry-After: 30`. **Bad:** 500 while Postgres is down.
- **Status:** in force.

## HTTP-22 · Framework failures are Problems with `http.*` codes

- **Rule:** Unknown route, 405, unbindable body and 415 answer Problems with `http.*` codes.
- **Why:** clients handle every failure with one parser.
- **Enforced by:** HttpConventionsTests.
- **Good:** `404 http.not_found`. **Bad:** an empty 404 from routing.
- **Status:** in force.

## HTTP-23 · Accept-Language negotiated; source data never translated

- **Rule:** Negotiate `Accept-Language` among en, es, es-AR, es-MX, pt-BR with fallback; `Content-Language` names only Frappé's own texts; data served
  as a source wrote it is not translated.
- **Why:** a translated proper noun or a wrong `Content-Language` misleads clients and caches.
- **Enforced by:** HttpConventionsTests.
- **Good:** `TerritoryEndpoints.ProperNouns`: no `Content-Language` for source names. **Bad:** translating a locality name.
- **Status:** in force.

## HTTP-24 · `/health/*` stays outside `/v1` and OpenAPI

- **Rule:** Health endpoints live under `/health/*`, outside `/v1` and the OpenAPI document, without rate limiting or language negotiation.
- **Why:** probes must not be throttled or depend on API conventions.
- **Enforced by:** HttpConventionsTests + review.
- **Good:** `HealthEndpoints` mapped on the app. **Bad:** `/v1/health`.
- **Status:** in force.

## HTTP-25 · Every enumerated value is `{code, name}`

- **Rule:** Every enumerated value on the wire, technical or not, is `{code, name}`: `code` the stable snake_case string, `name` translated by
  `Accept-Language`. Lists of enumerated values are lists of `{code, name}`. A change to an existing field is breaking (`!`, CON-02).
- **Why:** clients show a translated label without a local table, and branch on the code.
- **Enforced by:** review.
- **Good:** `"kind": {"code": "shipment", "name": "Envio"}`. **Bad:** `"kind": "shipment"`; `"currencies": ["ARS"]`.
- **Message keys:** the translated `name` of a value comes from `Resources/Messages/<lang>.json` under `<module>.<field>.<code>`; open,
  region-declared sets (calendar scope, covers) stay bare codes and say so in the OpenAPI description. A language is the one value that names itself:
  `{code, name}` with `code` the BCP 47 tag and `name` its own native name (`{"code": "es-AR", "name": "español (Argentina)"}`, `{"code": "ja",
  "name": "日本語"}`), the same whatever `Accept-Language`, because a speaker recognizes their own language in a picker and no catalogue translates every
  language into every other; the OpenAPI description says so.
- **Status:** in force.

## HTTP-26 · Retryable creations accept `Idempotency-Key` (opt-in)

- **Rule:** A creation command a client may retry enables the platform `Idempotency-Key` piece per endpoint: the first answer is stored 24 h and
  replayed for the same key; the header is documented in OpenAPI.
  A name-based request identity (derived from stable input rather than random) uses a shared UUIDv5 helper in the platform (planned); SHA-1 is
  acceptable there because it names, not protects.
- **Why:** a retried POST after a timeout must not create twice.
- **Enforced by:** review.
- **Good:** `POST /v1/...` with `Idempotency-Key: 7f3c...` answered twice with the same 201 and Location. **Bad:** a client-side dedupe guess or a
  per-module idempotency table.
- **Status:** in force.

## HTTP-27 · HTTP `max-age` never exceeds the data's freshness

- **Rule:** An endpoint's `max-age` never exceeds the freshness its data promises; data that must be fresh uses `no-cache` with ETag revalidation.
- **Why:** an HTTP cache that outlives the data cache serves changes the API already reflects.
- **Enforced by:** review.
- **Good:** reference data cached 1 day, refreshed daily. **Bad:** shipments cached by HTTP while an override must show at once.
- **Status:** in force.

## HTTP-28 · Slow work answers 202 + Location when the client must follow it

- **Rule:** A request does only what its answer needs within the latency budget; slow, retryable or third-party work goes to the Worker by event or
  job. Answer 202 with `Location` where the client follows the work;
  a 202 the client must not follow (anti-enumeration, SEC-06) has no Location (mark it `WithoutLocation()`).
- **Why:** requests stay fast and predictable; clients get a resource to poll.
- **Enforced by:** review.
- **Good:** `POST /shipment-syncs/{sourceId}/runs` → 202, `Location: .../runs`. **Bad:** running the sync inside the request.
- **Status:** in force.
