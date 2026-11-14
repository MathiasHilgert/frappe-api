# HTTP API conventions

What every endpoint of the Api shares. The endpoints themselves, with their parameters and responses, are
described by the OpenAPI document: `/openapi/v1.json` on a running Api, browsable at `/docs/`, and committed as
[`docs/api/openapi/v1.json`](api/openapi/v1.json).

| Topic | Rule |
|-------|------|
| [Access](#access) | Anonymous under `/v1`, rate limited per client; administration writes not authenticated yet |
| [Languages](#languages) | Texts follow `Accept-Language`; `Content-Language` names what was served |
| [Errors](#errors) | RFC 9457 Problem Details with one contract: a category in `type`, stable codes in `errors` |
| [Caching](#caching) | Every endpoint declares its lifetime: cacheable answers carry a strong `ETag` and `304`, the rest `no-store` |

## Access

Version 1 is anonymous, under `/v1` (rate limited per client). Most of it reads; the administration endpoints also
write (holiday overrides and sync requests) and are not authenticated yet: they are open until Frappé
has authentication (`TODO(auth)` on each route). The OpenAPI document describes the real problem body (`Problem`) on
every 4xx and 5xx, the `Content-Language`, `ETag`, `Cache-Control`, `Location` and `Retry-After` headers, and every
query parameter's type and whether it is required. Every `GET` route, the OpenAPI document and the Scalar reference included, also answers `HEAD`
with the same headers and no body.

Behind a reverse proxy, the rate limiter needs to know the proxy: see [operations](operations.md#runtime-settings).

## Languages

Texts follow `Accept-Language` (`en`, `es`, `es-AR`, `es-MX`, `pt-BR`): its ranges are tried from the highest quality
down and the first one Frappé is written in wins (`fr, es-AR;q=0.8` answers in `es-AR`; English only when none is).
Each text then falls back from the requested tag to its base language, any Portuguese to `pt-BR`, then English.
`Content-Language` names the languages of Frappé's own texts actually served. Source data is not translated and not
declared: names and categories (a holiday's name, a subdivision's or locality's category) are served as their source
publishes them, in the source's language, so an answer made only of source data, proper nouns and identifiers has no
`Content-Language` even when those words are Spanish. A 429 is explained in that language too, with the seconds to
wait in `detail` and the error's `parameters.retryAfter` as well as `Retry-After`.

## Errors

Errors are RFC 9457 Problem Details with one contract:

- `type` is always the category, `urn:frappe:problem:` and a slug: `invalid-value` (400), `bad-request` (400, a
  request the framework cannot read), `not-found` (404), `method-not-allowed` (405), `conflict` (409),
  `request-too-large` (413), `unsupported-media-type` (415, a body in a media type the endpoint does not read),
  `rule-violation` (422), `too-many-requests` (429), `unexpected` (500) or `unavailable` (503, a dependency is not
  configured or not answering; the same request may succeed later, and it carries `Retry-After: 30`). It never names
  an error code.
- The input is validated first: a malformed request is the client's error (400) whether or not a dependency is
  available. Once the input is valid, 503 outranks every other error found by the same use case.
- `errors` lists every error with its stable `code` (`http.not_found`, `http.method_not_allowed`, `http.bad_request`,
  `http.unsupported_media_type`, ...), its `parameters` and its translated `message`. A dependency that is not
  configured and one that is not answering have different codes.
- `title` is the category's text; `detail` is the error's message, or how many errors there are when there are
  several; both translated. `instance` is the path and `traceId` the request's trace.

```json
{
  "type": "urn:frappe:problem:not-found",
  "title": "The resource does not exist.",
  "status": 404,
  "detail": "The URL does not match any endpoint of the API.",
  "instance": "/v1/planets",
  "errors": [
    { "code": "http.not_found", "parameters": {}, "message": "The URL does not match any endpoint of the API." }
  ],
  "traceId": "00-b8834e584557cbe47ecb4c3261f12f40-c5bef03486c56136-03"
}
```

An unknown route is a 404 `http.not_found` and a method a route does not answer a 405 `http.method_not_allowed` (with
`Allow`), never an empty body; so is a request the framework cannot read (400 `http.bad_request`: a parameter it
cannot bind, invalid JSON, an empty or mistyped body) and a body in a media type the endpoint does not read (415
`http.unsupported_media_type`, with the `contentType` sent and the `accepted` ones). Such a problem has one `errors`
member like any other, with Frappé's code and text: never the framework's own (its English text and .NET type names
would say how the server is built, not what the client must fix). Any other client error answered
without a body is a Problem too (`bad-request`, keeping its status). They follow `Accept-Language` like every other
problem.

## Caching

Every endpoint declares its lifetime (RFC 9111) in `Cache-Control`, and the OpenAPI document says which:

- Cacheable answers (reference data, calendars) are `public, max-age=...` and carry a strong `ETag` of their
  representation, language included; a request whose `If-None-Match` matches it (or is `*`) gets `304 Not Modified`
  without a body.
- Answers no cache may keep (personal data; administration reads, which must show the latest state) are `no-store`, with no validator.

Errors, 429s and health are never cached. The OpenAPI document (`/openapi/v1.json`) and the reference (`/docs/`) carry
no caching headers or validators: they change only when the Api is deployed.
