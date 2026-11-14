# Geo

What the Geo module answers, where its data comes from, how it is configured and observed, and how a country is added.

| Topic | Summary |
|-------|---------|
| [What Geo answers](#what-geo-answers) | Countries and currencies, over HTTP |
| [Sources](#sources) | Where each kind of data comes from, its terms and its sync schedule |
| [Configuration](#configuration) | Source locations and cron schedules |

## What Geo answers

Every endpoint is under `/v1/geo`; parameters and responses are in the committed contract
([`docs/api/openapi/v1.json`](api/openapi/v1.json)) and in Scalar at `/docs/` on a running Api. Errors, languages and
caching follow [`http-api.md`](http-api.md).

- **Catalogue.** The 249 ISO 3166-1 countries and the ISO 4217 currencies in use: `countries`, `countries/{code}`,
  `countries/{code}/currencies`, `currencies`, `currencies/{code}`.

### Errors

Errors follow [`http-api.md`](http-api.md): a stable code in `errors[].code`, its facts in `parameters`, every problem of
the request at once where they can be found together. Codes are never renamed; a new distinction gets a new code. A
code Geo cannot read (`ARG` as a country) is the parameter's `.invalid_format` code; a well-formed one Geo does not know
is the resource's `.not_found` code.

## Sources

| Data | Source | Terms | Default schedule |
|------|--------|-------|------------------|
| Countries, currencies, names | [Unicode CLDR](https://cldr.unicode.org/), embedded snapshot (`Frappe.Geo.Global`) | Unicode License v3 | None: changes ship with a release |

The Migrator brings the catalog to the embedded snapshot after every migration, as a diff: a country or currency the
snapshot drops is retired, never deleted, so a code another module stored keeps resolving.

## Configuration

Every key is validated when the host starts; a missing or invalid one stops it naming the key.

| Key | Default | Meaning |
|-----|---------|---------|
| `ConnectionStrings:Geo` | (required) | Geo's database. |
