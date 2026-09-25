# API conventions

Normative rules for every HTTP endpoint of this API. "MUST", "SHOULD" and
"MAY" are used as in [RFC 2119](https://www.rfc-editor.org/rfc/rfc2119).
The style is "Stripe plus standards": Stripe's resource shapes, which API
consumers already know, and IETF standards wherever one exists.

Helpers that implement these rules live in
[`internal/foundation/rest`](../internal/foundation/rest/doc.go) (lists,
cursors, pagination, expansion, naming check) and
[`internal/foundation/identifier`](../internal/foundation/identifier/identifier.go)
(prefixed IDs). Use them instead of re-implementing a rule.

## Contents

1. [Contract: OpenAPI 3.1](#1-contract-openapi-31)
2. [Versioning](#2-versioning)
3. [Paths and resources](#3-paths-and-resources)
4. [Naming](#4-naming)
5. [Resource shape and the object field](#5-resource-shape-and-the-object-field)
6. [Identifiers](#6-identifiers)
7. [Lists and pagination](#7-lists-and-pagination)
8. [Filtering and search](#8-filtering-and-search)
9. [Expanding related resources](#9-expanding-related-resources)
10. [Data types](#10-data-types)
11. [Errors](#11-errors)
12. [Mutations and idempotency](#12-mutations-and-idempotency)
13. [Localization](#13-localization)
14. [Caching](#14-caching)
15. [Checklist for a new endpoint](#15-checklist-for-a-new-endpoint)

## 1. Contract: OpenAPI 3.1

The OpenAPI 3.1 document Huma generates from the Go operation and struct
definitions is the API contract. It is served at `/openapi.json` (and
`/openapi.yaml`), with a browsable reference at `/docs`, when
`HTTP_DOCUMENTATION_ENABLED=true` (the default outside production).

- Operations MUST be registered through Huma (`huma.Get`, `huma.Register`,
  ...) in the module's `adapters/http` package, never as raw
  `http.Handler`s, so they appear in the contract.
- Every input and output field SHOULD carry `doc:"..."` and, where useful,
  `example:"..."` tags; validation belongs in schema tags (`minimum`,
  `maxLength`, `pattern`, `enum`, ...), so the contract documents it and
  Huma enforces it.
- Clients and SDKs are generated from the document; a change that breaks
  the document breaks clients (see [Versioning](#2-versioning)).
- Huma's schema link transformer is disabled: responses carry no `$schema`
  property and no `Link: rel="describedby"` header. Resources identify
  themselves with `object`, and `Link` is reserved for pagination.

## 2. Versioning

- Every endpoint lives under the major version prefix `/v1`
  (`httpserver.Server.V1`). Health probes (`/health/live`,
  `/health/ready`) and documentation are unversioned.
- Within a major version, changes MUST be backward compatible: adding
  optional request parameters, adding response properties, adding enum
  values to response fields that are documented as extensible, and adding
  endpoints are compatible. Removing or renaming anything, changing a
  type, making an optional input required, or changing a default is
  breaking. Clients MUST ignore unknown response properties
  ([Zalando rule 108](https://opensource.zalando.com/restful-api-guidelines/#108)).
- A breaking change goes to a new major version (`/v2`), served next to
  the previous one during a migration window.
- Deprecation: a deprecated operation or field is marked `deprecated` in
  OpenAPI (`huma.Operation.Deprecated`, or the `deprecated:"true"` tag),
  and deprecated operations SHOULD answer with the `Deprecation`
  ([RFC 9745](https://www.rfc-editor.org/rfc/rfc9745)) and `Sunset`
  ([RFC 8594](https://www.rfc-editor.org/rfc/rfc8594)) headers, plus a
  `Link: <...>; rel="deprecation"` to the migration notes.

## 3. Paths and resources

- Paths are namespaced by module: `/v1/<module>/<collection>`, for example
  `/v1/geo/countries`. A module with a single, obviously named collection
  MAY drop the namespace when the collection name is the module name.
- Collections are plural nouns; a single resource is
  `/<collection>/{id}`. Verbs never appear in paths, except for
  non-CRUD actions, which are `POST /<collection>/{id}/<action>`
  (Stripe: `POST /v1/payment_intents/{id}/cancel`).
- Collections are flat, top-level resources, filtered with query
  parameters (Stripe, GitHub):
  `GET /v1/geo/subdivisions?country=AR`, not
  `GET /v1/geo/countries/AR/subdivisions`. Every resource has exactly one
  canonical URL; nested aliases MUST NOT be added.
- Nesting is reserved for true sub-resources that cannot exist or be
  addressed without their parent (for example
  `/v1/orders/{id}/line_items` when line items have no identity outside
  their order).
- Path parameters that legitimately contain `/` (IANA time zone ids such
  as `America/Argentina/Cordoba`) use a trailing Go 1.22 `ServeMux`
  wildcard: `huma.Get(api, "/time_zones/{id...}", ...)` with
  ``ID string `path:"id"` ``. Huma on humago passes the pattern to
  `http.ServeMux`, which binds the rest of the path, slashes included
  (covered by `TestTrailingWildcardPathParametersKeepSlashes`). The
  wildcard MUST be the last segment. Fallback when a path cannot end with
  the wildcard: require the client to percent-encode the slash
  (`America%2FArgentina%2FCordoba`) or accept the value as a query
  parameter (`?id=America/Argentina/Cordoba`).
- No trailing slashes, no file extensions (`.json`).

## 4. Naming

- JSON properties, query parameters and path segments are `snake_case`
  ([Zalando rule 118](https://opensource.zalando.com/restful-api-guidelines/#118),
  Stripe): `created_at`, `has_more`, `/v1/geo/time_zones`,
  `?country_code=AR`.
- Every exported Go field of a request or response body MUST carry an
  explicit snake_case `json` tag. Huma derives property names from `json`
  tags and falls back to the Go field name (`CreatedAt`) without one.
- Enforcement: `rest.CheckNaming` runs in the composition root after all
  modules registered their operations and fails startup (and therefore
  every test that builds the application) listing each non snake_case
  schema property, path segment, and query or path parameter name.
  `expand[]` is the single allowed exception (its `[]` suffix); header
  parameters keep HTTP naming (`If-None-Match`).
- Booleans read as predicates (`is_active`, `has_more`); timestamps end in
  `_at`, dates in `_on` or `_date`; counts end in `_count`.
- Enum values are lowercase snake_case strings (`"pending_review"`).
- Names are English and never abbreviated (`description`, not `desc`),
  except universally known acronyms, written lowercase (`id`, `url`).

## 5. Resource shape and the object field

Every resource and every list MUST carry a string `object` property naming
its type, first in the object (Stripe):

```json
{
  "object": "dish",
  "id": "dish_06f3vdz0q9x7k2m4n8p5r1s3tw",
  "name": "Milanesa napolitana",
  "price": { "amount": 1250000, "currency": "ARS" },
  "created_at": "2026-09-25T14:03:11Z",
  "updated_at": "2026-09-25T14:03:11Z"
}
```

- `object` is the singular snake_case resource type (`dish`, `country`,
  `time_zone`); declare it with an `enum` tag holding the single value,
  so the contract states it: ``Object string `json:"object" enum:"dish"` ``.
- It lets clients and expanded payloads (see [Expanding](#9-expanding-related-resources))
  tell an id string from an embedded resource, and a list from a resource.
- Optional values are `null`, never omitted, in responses: a property is
  always present, so clients can tell "no value" from "old API version".
- Responses never wrap a single resource in another envelope (no
  `{"data": {...}}` for single resources).

## 6. Identifiers

Two kinds of identifiers, chosen per resource:

| Kind | Used for | Format | Example |
| ---- | -------- | ------ | ------- |
| Prefixed opaque ID | Business entities created by this API | `<prefix>_<26 char base32 UUIDv7>` | `dish_06f3vdz0q9x7k2m4n8p5r1s3tw` |
| Natural key | Reference data defined by a public standard | The standard's code, unchanged | `AR` (ISO 3166-1 alpha-2), `AR-X` (ISO 3166-2), `3860259` (GeoNames), `America/Argentina/Cordoba` (IANA) |

Prefixed IDs (`identifier.New("dish")`, `identifier.Parse("dish", value)`):

- The prefix is 2 to 16 lowercase letters or digits and names the entity
  type, like Stripe's `cus_` and `pi_`. It makes an ID self-describing in
  logs, tickets and URLs, and `Parse` rejects an ID of another type before
  any query.
- The suffix is a UUIDv7 ([RFC 9562](https://www.rfc-editor.org/rfc/rfc9562))
  in lowercase Crockford base32, 26 characters. UUIDv7 is time ordered
  (index friendly, no hot spots from random UUIDv4 inserts), needs no
  coordination between replicas and is stored natively in a Postgres
  `uuid` column; the prefix is presentation only and never stored.
  Crockford base32 is URL safe, shorter than hex, avoids the ambiguous
  letters `i`, `l`, `o`, `u`, and preserves the time order in the string.
- Only the canonical form is accepted (lowercase, exact length, zero
  trailing bits); anything else is `identifier.ErrInvalid`, which an
  adapter maps to `404 Not Found` for a path parameter (the resource
  cannot exist) or `422` with an `errors[]` entry for a body or query
  value (see the status rule in [Errors](#11-errors)).
- Clients MUST treat IDs as opaque strings; the embedded creation time is
  not a contract.
- Use cases create IDs (`identifier` is allowed in the application layer
  by go-arch-lint); the domain holds them as plain values.

Natural keys are used unchanged, in the standard's own case (`AR`, not
`ar`), for reference data this API does not own. Their `object` still
names the type (`"object": "country", "id": "AR"`).

## 7. Lists and pagination

Every collection endpoint returns the list envelope (`rest.List[T]`,
built with `rest.NewPage`):

```http
GET /v1/geo/cities?country=AR&limit=2

HTTP/1.1 200 OK
Link: </v1/geo/cities?country=AR&cursor=eyJzIjoi...&limit=2>; rel="next"
```

```json
{
  "object": "list",
  "url": "/v1/geo/cities",
  "data": [
    { "object": "city", "id": "3435910", "name": "Buenos Aires" },
    { "object": "city", "id": "3860259", "name": "Cordoba" }
  ],
  "has_more": true,
  "next_cursor": "eyJzIjoi...SDWSjMWgBKzObw9VWqkLLBH18moBYvzzY9U1rOHl6d8"
}
```

- `data` is always an array (`[]` when empty, never `null`); `url` is the
  collection path without query; `next_cursor` is `null` on the last page.
- Pagination is cursor (keyset) based, never offset based: offsets skip or
  repeat rows when data changes and get slower with depth
  ([AIP-158](https://google.aip.dev/158),
  [Zalando rule 160](https://opensource.zalando.com/restful-api-guidelines/#160)).
- Parameters (`rest.PageParameters`): `limit`, 1 to 100, default 10;
  `cursor`, the previous page's `next_cursor`. Clients follow
  `next_cursor` (or the `Link` header) until `has_more` is `false`.
  Every other parameter MUST be repeated unchanged on following pages;
  the `Link` URL does that for the client.
- `Link: <...>; rel="next"` ([RFC 8288](https://www.rfc-editor.org/rfc/rfc8288))
  is sent only when a next page exists, with a relative URL keeping every
  query parameter of the request (GitHub style), so generic HTTP clients
  paginate without reading the body. The URL and the list `url` use the
  request path exactly as it was escaped on the wire (`%3E`, `%2C`, `%3B`,
  `%20` stay encoded), so the header stays a valid RFC 8288 `URI-Reference`.
- There is no `total_count`: counting is as expensive as the query itself
  on large tables. An endpoint MAY add one as an opt-in parameter when a
  product need justifies it.
- The handler fetches `limit + 1` rows ordered by a unique key (for
  example `(name, id)`); the extra row only signals `has_more`.

Cursors (`rest.CursorCodec`):

- Format: `base64url(JSON payload) "." base64url(HMAC-SHA256)`, unpadded.
  The payload holds a format version, a scope and the keyset position.
- Opaque to clients, which MUST NOT parse or build them; the format can
  change at any time.
- Signed with `HTTP_CURSOR_SECRET` (at least 32 bytes, shared by every
  replica, required in staging and production; development without it
  uses a random per-process secret). Rotation: set the new secret and move
  the old one to `HTTP_CURSOR_PREVIOUS_SECRETS` (comma-separated); cursors
  are signed with the current secret and verified with any of them, so
  outstanding cursors keep working until the old secret is removed. A cursor is client input that ends up
  in a `WHERE` clause: the signature rejects forged positions, probing of
  sort keys, and cursors issued for another collection, without a
  database round trip. It is not encrypted: it only contains values the
  client already received.
- The scope is derived automatically by `rest.PageParameters` and
  `rest.NewPage`, never built by hand: a principal/tenant slot (empty
  until authentication exists), the escaped request path, and the
  canonical query (keys and each key's values sorted), which includes
  every filter and `order_by` and excludes only `cursor`, `limit` and
  `expand[]`. A cursor therefore only works on the exact listing (and,
  later, principal) that issued it, whatever order the client writes the
  parameters in; a client may change `limit` or `expand[]` between pages.
- A malformed, tampered, old-version or other-listing cursor is
  `400 Bad Request` with `errors[0].location = "query.cursor"`; the client
  restarts from the first page. Removing a secret from the rotation list
  has the same effect on cursors it signed.
- Cursors do not expire, but they are not bookmarks: a position may point
  to rows that no longer exist, and paging continues from the next
  existing row.

## 8. Filtering and search

- Filters are query parameters named after the property they filter,
  exact match by default: `?country=AR&is_active=true`.
- Multiple values of one filter: comma-separated, meaning OR
  (`?status=pending,paid`). Different filters combine with AND.
- Ranges use suffixes: `created_at_gte`, `created_at_lt` (RFC 3339 values).
- Sorting, when an endpoint supports more than its default order,
  is `?order_by=name` / `?order_by=-created_at`; the default order is
  documented per endpoint and always ends with the unique key.
- Filters are declared as Huma query fields with validation tags; unknown
  values fail with `422`.
- Field selection and partial responses (`?fields=`, sparse fieldsets) are
  not supported for now: every response returns the full resource. Use
  `expand[]` for related data; a selection mechanism will be designed if a
  measured payload problem justifies it.
- Free-text or fuzzy search is a separate sub-resource per collection,
  `GET /v1/<module>/<collection>/search?query=...` (Stripe's
  `/v1/customers/search`), returning the same list envelope. Search
  results MAY be less fresh than the collection (for example, served by an
  index) and are ordered by relevance. The parameter is always `query`.

## 9. Expanding related resources

Related resources are returned as their ID by default. A client MAY ask
for them inline with repeated `expand[]` parameters (Stripe):

```http
GET /v1/geo/cities/3860259?expand[]=country&expand[]=subdivision.country
```

```json
{
  "object": "city",
  "id": "3860259",
  "name": "Cordoba",
  "country": { "object": "country", "id": "AR", "name": "Argentina" },
  "subdivision": {
    "object": "subdivision",
    "id": "AR-X",
    "country": { "object": "country", "id": "AR", "name": "Argentina" }
  }
}
```

- The property keeps its name; its value is either the ID string or the
  embedded resource, told apart by `object`. In OpenAPI it is declared as
  `oneOf` the ID and the resource schema.
- Paths are dot-joined snake_case property names, at most 4 levels deep
  and at most 20 per request (`rest.MaximumExpansionDepth`,
  `rest.MaximumExpansions`).
- Each operation declares an allowlist (`rest.NewExpansions(...)`); any
  other path is `422 Unprocessable Entity` with one `errors[]` entry per
  offending value (`location: "query.expand[]"`).
- On lists, paths are relative to each item (`expand[]=country`), not
  prefixed with `data.`.
- Expansion MUST NOT cause N+1 queries: resolve each expanded path with
  one batched query per page.

## 10. Data types

- Timestamps: [RFC 3339](https://www.rfc-editor.org/rfc/rfc3339) strings
  in UTC with a `Z` suffix, second precision or finer:
  `"2026-09-25T14:03:11Z"`. Inputs MAY carry an offset; they are
  normalized to UTC. Go: `time.Time` (Huma declares `format: date-time`).
- Dates without time: `"2026-09-25"` (RFC 3339 `full-date`).
- Durations: integer seconds in a property ending in `_seconds`, unless a
  domain needs ISO 8601 durations.
- Money: an object with the amount as an integer in the currency's minor
  unit and the [ISO 4217](https://www.iso.org/iso-4217-currency-codes.html)
  code, never a float:
  `{"amount": 1250000, "currency": "ARS"}` is 12,500.00 ARS. Zero-decimal
  currencies (`JPY`, `CLP`) use the unit itself (Stripe).
- Countries: ISO 3166-1 alpha-2 (`AR`); subdivisions: ISO 3166-2
  (`AR-X`); languages and locales: BCP 47 (`es-419`); time zones: IANA
  (`America/Argentina/Cordoba`).
- Large integers that can exceed 2^53 are strings.
- Booleans are `true`/`false`, never `0`/`1` or `"yes"`.

## 11. Errors

Every error is [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)
`application/problem+json`, produced by returning `huma.Error4xx...` or
`huma.NewError` from a handler (never by writing a body by hand):

```json
{
  "title": "Not Found",
  "status": 404,
  "detail": "No dish with id dish_06f3vdz0q9x7k2m4n8p5r1s3tw."
}
```

- `title` is the status reason; `detail` is a human readable, localized
  explanation of this occurrence, safe to show to an end user. Neither
  contains internal details (SQL, stack traces, other tenants' data).
- Field level problems go in `errors[]`, one entry per problem, with
  `location` (`body.price.amount`, `query.limit`, `path.id`), `message`
  and, when safe, the rejected `value`.
- Status rule for client input, applied everywhere:
  - `400`: the input is syntactically unusable: a malformed, tampered or
    other-listing cursor, an unparseable body or invalid JSON.
  - `422`: the input is well formed but its values are unacceptable:
    schema validation (range, pattern, enum, required), an unknown
    `expand[]` value, an invalid identifier in a body or query field, an
    unknown filter value, a business rule.
  - `404`: an invalid or unknown identifier in the path, since no such
    resource can exist.
- Status codes:

| Status | When |
| ------ | ---- |
| `400 Bad Request` | Syntactically unusable input: malformed or foreign cursor, unparseable body or JSON. |
| `401 Unauthorized` | Missing or invalid credentials. |
| `403 Forbidden` | Authenticated but not allowed. |
| `404 Not Found` | The resource does not exist, its path identifier is invalid, the caller may not know it exists, or it was deleted (see `410`). |
| `406 Not Acceptable` | `Accept` names no representation the operation can produce (only `application/json` and `application/problem+json` exist today). |
| `409 Conflict` | State conflict (duplicate natural key, concurrent update, idempotency key reuse in flight). |
| `410 Gone` | Not used by default: deleted resources answer `404`. A resource MAY answer `410` only if it documents a tombstone retention period during which deletions are remembered. |
| `412 Precondition Failed` | `If-Match` did not match. |
| `415 Unsupported Media Type` | The request body's `Content-Type` is not accepted (for example not `application/json`, or not `application/merge-patch+json` on `PATCH`). |
| `422 Unprocessable Entity` | Well formed input with unacceptable values: schema validation (Huma's validation error), unknown `expand[]` value, invalid identifier or filter value in body or query, business rule. |
| `429 Too Many Requests` | Rate limited, with `Retry-After` and the rate limit headers below. |
| `500 Internal Server Error` | Unexpected failure; never includes the cause. Correlate with `X-Request-ID`. |
| `503 Service Unavailable` | Temporarily unavailable, with `Retry-After` when known. |

Validation failure (limit out of range):

```json
{
  "title": "Unprocessable Entity",
  "status": 422,
  "detail": "validation failed",
  "errors": [
    { "message": "expected number >= 1", "location": "query.limit", "value": 0 }
  ]
}
```

Invalid cursor:

```json
{
  "title": "Bad Request",
  "status": 400,
  "detail": "The pagination cursor is invalid or belongs to another listing. Restart from the first page.",
  "errors": [{ "message": "invalid cursor", "location": "query.cursor" }]
}
```

Invalid expansion:

```json
{
  "title": "Unprocessable Entity",
  "status": 422,
  "detail": "One or more expand[] values are invalid.",
  "errors": [
    { "message": "not expandable on this operation", "location": "query.expand[]", "value": "owner" },
    { "message": "must be dot-joined snake_case field names", "location": "query.expand[]", "value": "Bad" }
  ]
}
```

- Error responses are always `Cache-Control: no-store` and carry
  `X-Request-ID`.
- Rate limits are announced with the `RateLimit` and `RateLimit-Policy`
  fields of the IETF
  [RateLimit header fields for HTTP](https://datatracker.ietf.org/doc/draft-ietf-httpapi-ratelimit-headers/)
  draft, for example `RateLimit-Policy: "default";q=100;w=60` and
  `RateLimit: "default";r=42;t=18`, plus `Retry-After` on `429`. The
  current middleware still emits the older draft's separate
  `RateLimit-Limit`, `RateLimit-Remaining` and `RateLimit-Reset` fields;
  moving it to `RateLimit` and `RateLimit-Policy` is pending.
- A stable, machine readable `type` URI per error kind (RFC 9457 section
  3.1.1) MAY be added when clients need to branch on a specific failure;
  until then clients branch on `status` and `errors[].location`.

## 12. Mutations and idempotency

- Create: `POST /<collection>` returns `201 Created`, the full resource
  and `Location: /v1/<collection>/{id}`.
- Partial update: `PATCH /<collection>/{id}` with
  [JSON Merge Patch](https://www.rfc-editor.org/rfc/rfc7396) semantics
  (`application/merge-patch+json`), returning `200` and the full
  resource. `PUT` is only used for full replacement where that is the
  natural operation.
- Delete: `DELETE /<collection>/{id}` returns `204 No Content`; deleting
  an already deleted resource returns `404`.
- Optimistic concurrency: responses carry an `ETag`; mutations MAY
  require `If-Match` and answer `412` on mismatch
  ([RFC 9110 section 13.1.1](https://www.rfc-editor.org/rfc/rfc9110#section-13.1.1)).
- Idempotency (upcoming, not implemented yet): every non-idempotent
  mutation (`POST`, and `PATCH` where it is not naturally idempotent) will
  accept an `Idempotency-Key` header
  ([IETF draft](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/),
  Stripe). The key is a client generated UUID; the first response for a
  key is stored for 24 hours and replayed for retries with the same key
  and payload; the same key with a different payload is `422`, and a
  retry while the first request is in flight is `409`. Clients SHOULD
  already send it; until the foundation middleware ships it is ignored.

## 13. Localization

- Clients send `Accept-Language`
  ([RFC 9110 section 12.5.4](https://www.rfc-editor.org/rfc/rfc9110#section-12.5.4));
  the server negotiates one of `I18N_SUPPORTED_LOCALES` (falling back to
  the source locale) and answers with `Content-Language` and
  `Vary: Accept-Language` on every `/v1` response.
- Localized: user facing strings, such as `detail` of errors and
  translatable resource fields (names, descriptions). Not localized:
  property names, enum values, codes and identifiers.
- A resource returns one language, the negotiated one. Endpoints that
  manage translations expose them explicitly (for example a
  `translations` map keyed by BCP 47 tag), never through
  `Accept-Language`.

## 14. Caching

See `internal/foundation/httpserver/doc.go` for the helpers.

- Default: `Cache-Control: no-store` on every `/v1` response; errors are
  always `no-store`.
- Readable resources SHOULD declare a policy and an `ETag`
  (`httpserver.NotModified(ctx, etag, policy)`), answering
  `If-None-Match` with `304 Not Modified` before computing the body.
  Prefer `httpserver.ETagFromVersion` (from a version column or
  `updated_at`) over hashing the body.
- `Private(max_age)` / `Revalidate()` for caller or tenant specific data
  (adds `Vary: Authorization`); `Public(max_age, stale_while_revalidate)`
  only for data identical for every caller, such as reference data.
- `If-Modified-Since` is not supported; use ETags.

## 15. Checklist for a new endpoint

- [ ] Path is `/v1/<module>/<snake_case_plural>`, flat, one canonical URL.
- [ ] Every body field has a snake_case `json` tag and a `doc` tag.
- [ ] Resources carry `object`; business entities use `identifier`
      prefixed IDs, reference data its natural key.
- [ ] Collections return `rest.ListOutput[T]` via `rest.NewPage` (the
      cursor scope is derived from the request), ordered by a unique key.
- [ ] `expand[]` is validated with an allowlist and batch loaded.
- [ ] Timestamps RFC 3339 UTC, money minor units plus ISO 4217.
- [ ] Errors are `huma.Error...` problems with `errors[]` locations.
- [ ] Read endpoints declare a cache policy and ETag where it pays off.
- [ ] Mutations document their idempotency behavior.

## References

- [Stripe API reference](https://docs.stripe.com/api): object field,
  prefixed IDs, list envelope, `expand[]`, search, idempotency keys.
- [Google AIP](https://google.aip.dev/): AIP-122 resource names, AIP-158
  pagination, AIP-180 backwards compatibility.
- [Zalando RESTful API guidelines](https://opensource.zalando.com/restful-api-guidelines/):
  naming, compatibility, pagination, problem JSON.
- [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details,
  [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110) HTTP semantics,
  [RFC 8288](https://www.rfc-editor.org/rfc/rfc8288) web linking,
  [RFC 3339](https://www.rfc-editor.org/rfc/rfc3339) timestamps,
  [RFC 9562](https://www.rfc-editor.org/rfc/rfc9562) UUIDv7.
