# Geo

What the Geo module answers, where its data comes from, how it is configured and observed, and how a country is added.

| Topic | Summary |
|-------|---------|
| [What Geo answers](#what-geo-answers) | Countries and currencies, over HTTP |
| [Sources](#sources) | Where each kind of data comes from, its terms and its sync schedule |
| [Configuration](#configuration) | Source locations and cron schedules |
| [Metrics](#metrics) | Freshness of each source and the outcome of its runs |
| [Adding a country](#adding-a-country) | What a country package declares and where the hosts compose it |

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

The Worker syncs every source on its own schedule, and once when it starts. A run downloads the source's files
conditionally (ETag, Last-Modified, SHA-256), validates the whole download and applies it in one transaction; a
refused or failed run changes nothing, and every run is audited in `geo.sync_runs`.

| Data | Source | Terms | Default schedule |
|------|--------|-------|------------------|
| Countries, currencies, names | [Unicode CLDR](https://cldr.unicode.org/), embedded snapshot (`Frappe.Geo.Global`) | Unicode License v3 | None: changes ship with a release |
| Argentina: territory | [Georef](https://infra.datos.gob.ar/georef/) full downloads (provinces, departments, municipalities, localities) | See the source | Mondays 04:00 |

The Migrator brings the catalog to the embedded snapshot after every migration, as a diff: a country or currency the
snapshot drops is retired, never deleted, so a code another module stored keeps resolving.

## Configuration

Every key is validated when the host starts; a missing or invalid one stops it naming the key. Crons are Quartz
expressions, seconds first. In environment variables, `:` becomes `__` (`Geo__Argentina__Territory__Cron`).

| Key | Default | Meaning |
|-----|---------|---------|
| `ConnectionStrings:Geo` | (required) | Geo's database. |
| `Geo:Argentina:Georef:BaseUrl` | `https://infra.datos.gob.ar/georef/` | Georef's downloads (absolute, ending in `/`). |
| `Geo:Argentina:Territory:Cron` | `0 0 4 ? * MON` | Territory sync. |

## Metrics

Geo's business metrics are on meter `Frappe.Geo`, exported by both hosts with every `Frappe.*` meter; traces come
from the platform's instrumentation. No tag carries personal data, typed text or a subdivision: only country codes,
source ids, scopes, outcomes and error codes.

| Metric | Type, unit | Tags | Reported by |
|--------|------------|------|-------------|
| `frappe.geo.sync.runs` | Counter, `{run}` | `frappe.geo.source`, `frappe.outcome` (`applied`, `unchanged`, `not_published`, `refused`, `failed`) | Worker |
| `frappe.geo.sync.last_success.age` | Observable gauge, `s` | `frappe.geo.source` | Worker |

- **Freshness.** `last_success.age` is the time since a source's last applied or unchanged run, read from
  `geo.sync_resources`; a source that never succeeded reports nothing. Alert when it exceeds about twice the source's
  schedule (for example more than 2 days for a daily source, 8 for a weekly one).
- The gauges read the database at most once a minute; when it cannot be read they report nothing for that collection.

## Adding a country

A country is a package under `src/Modules/Geo/Countries/Frappe.Geo.Countries.<Country>`; it never references another
country's package, and the core names no country or source. An adapter only declares: the core's one sync pipeline
fetches, validates, applies and audits every source. Use `Frappe.Geo.Countries.Argentina` as the model.

1. **Sources.** Implement `ISnapshotSource<TerritorySnapshot>` per source: a stable `Id`, its files (`Resources`, each
   required or optional) and `Read`, which turns the downloaded files into a snapshot or refuses them.
2. **Registration.** An `Add<Country>(this IGeoSources geo)` extension binds and validates each source's options
   (`Geo:<Country>:<Source>:BaseUrl`) and registers each source with its job key, configuration section and default
   cron (`geo.Territory<TSource>(...)`). Respect each source's terms in its schedule.
3. **Hosts.** The Worker composes the sources (`AddGeoSyncs(geo => geo.Add<Country>())`).
4. **Tests.** Trimmed real files as fixtures in `tests/Modules/Geo/Frappe.Geo.UnitTests/Fixtures/`, unit tests of
   `Read`, and a sync through the real pipeline against an in-process source in the integration tests.
5. **Docs.** Add its sources, terms, schedules and keys to this page.
