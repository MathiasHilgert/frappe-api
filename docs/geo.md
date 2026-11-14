# Geo

What the Geo module answers, where its data comes from, how it is configured and observed, and how a country is added.

| Topic | Summary |
|-------|---------|
| [What Geo answers](#what-geo-answers) | Countries, currencies, territory, holidays and business days, over HTTP and `IGeoQueries` |
| [Sources](#sources) | Where each kind of data comes from, its terms and its sync schedule |
| [Configuration](#configuration) | Source locations and cron schedules |
| [Administration](#administration) | Manual corrections, sync history and runs, subdivision aliases |
| [Metrics](#metrics) | Freshness, coverage, "not covered" requests and cache effectiveness |
| [Adding a country](#adding-a-country) | What a country package declares and where the hosts compose it |

## What Geo answers

Every endpoint is under `/v1/geo`; parameters and responses are in the committed contract
([`docs/api/openapi/v1.json`](api/openapi/v1.json)) and in Scalar at `/docs/` on a running Api. Errors, languages and
caching follow [`http-api.md`](http-api.md).

- **Catalogue.** The 249 ISO 3166-1 countries and the ISO 4217 currencies in use: `countries`, `countries/{code}`,
  `countries/{code}/currencies`, `currencies`, `currencies/{code}`. A country also says what Geo covers for it
  (`capabilities`: holiday years per scope, business-day policies, time zone).
- **Territory.** A covered country's subdivisions (ISO 3166-2), their administrative areas and localities:
  `countries/{code}/subdivisions`, `subdivisions/{code}`, `subdivisions/{code}/areas?level=`, `areas/{id}`,
  `subdivisions/{code}/localities?q=&limit=&cursor=`, `localities/{id}`. A known country Geo holds no territory of
  answers 404 `geo.territory.not_covered`, never an empty list, as holidays do: an empty list would read as "this country
  has no subdivisions". A flag on the list would have said the same, but in a second, easy to ignore, way. A
  subdivision of such a country (`subdivisions/US-CA`, its areas, its localities) is 404 `geo.territory.not_covered`
  too, never "does not exist"; an unknown subdivision of a covered country is 404 `geo.subdivision.not_found`.
- **Holidays.** A jurisdiction's calendar of a year (`countries/{code}/holidays?year=&scope=`, and the same under
  `subdivisions/{code}`), with the calendars it covers (`covers`) and the credit their sources require
  (`attributions`). `scope` is `general` by default, or a sector calendar the country declares (Argentina: `banking`);
  a sector's calendar lists its source's facts as published (the BCRA's rows, all `holiday`), since two sources' fields
  are never merged: reconciling them with the general calendar is what `calendar/{date}` answers.
- **Business days.** Whether a date is a business day (`.../calendar/{date}?policy=`) and the next ones
  (`.../business-days?from=&count=&policy=`, at most 60). The `policy` is `banking`, `labor` or `commercial`; which
  observances each treats as non-working is the country's rule. Both answers carry `covers` and `attributions` like
  holiday answers, for every calendar read (the next business days may read two years): Calendarios Nacionales' terms
  require the link in every answer that uses its data.

Geo keeps holidays only for countries with explicit coverage. Any other country, or a year Geo holds no calendar for,
answers 404 `geo.holidays.not_covered` (or `geo.business_days.not_covered` when the country has no rules for the
policy), never "no holidays". The error names the calendar actually missing (`jurisdiction`, `year`, `scope`): for a
subdivision, the country's when that is the one Geo lacks. A scope the country does not declare is 400
`geo.calendar_scope.invalid` (with the `scopes` it does), never "not covered", since no calendar of it can exist. Other modules ask the same questions in process through `IGeoQueries`
(`Frappe.Geo.Contracts`, see [ADR 0002](adr/0002-synchronous-geo-queries.md)), which answers `NotCovered` instead, and
learn about calendar changes from the `HolidaysChanged` integration event.

Calendar and business-day answers are cached (memory, then Valkey); clients may reuse an answer for five minutes
(`max-age=300`). A calendar change evicts its entries in every replica about a second after it commits while messaging
is up: the outbox relays `HolidaysChanged` through NATS, the Worker consumes it and the cache backplane carries the
eviction. While NATS or the Worker is down, eviction waits for them, so business-day answers may be stale for up to the
cache entry's lifetime (10 minutes, the platform default); the event waits in the outbox and evicts once they are back. Holiday listings (`.../holidays`) read storage directly and are never stale.

### Errors

Errors follow [`http-api.md`](http-api.md): a stable code in `errors[].code`, its facts in `parameters`, every problem of
the request at once where they can be found together. Codes are never renamed; a new distinction gets a new code. For
parameters (query, route or body field) the rule is:

- absent or blank and required: `geo.parameter.required`, naming it in `parameter`;
- present but unreadable (not a date, not digits): the parameter's `.invalid` or `.invalid_format` code;
- a readable number outside its bounds: its `.out_of_range` code (`holiday_year`, `business_day_count`, `area_level`,
  `page_limit`; each also has its `.invalid`).

An invalid observance (`geo.observance.invalid`) names its `date` (ISO `yyyy-MM-dd`, or the value given when it is not
a date) and a stable `reason` each language translates: `date_invalid`, `name_empty`, `name_too_long`, `kind_unknown`,
`moved_from_invalid`, `moved_from_not_allowed`, `community_missing`, `community_not_allowed`, `community_too_long`,
`community_unknown`, `citation_empty`, `citation_too_long`, `url_invalid`, `url_too_long` (with `max`, `value` or
`communities` where they explain it). Codes and other values in messages are quoted parameters, never words of the
sentence.

## Sources

The Worker syncs every source on its own schedule, and once when it starts. A run downloads the source's files
conditionally (ETag, Last-Modified, SHA-256), validates the whole download and applies it in one transaction; a
refused or failed run changes nothing, and every run is audited in `geo.sync_runs`.

| Data | Source | Terms | Default schedule |
|------|--------|-------|------------------|
| Countries, currencies, names | [Unicode CLDR](https://cldr.unicode.org/), embedded snapshot (`Frappe.Geo.Global`) | Unicode License v3 | None: changes ship with a release |
| Argentina: territory | [Georef](https://infra.datos.gob.ar/georef/) full downloads (provinces, departments, municipalities, localities) | See the source | Mondays 04:00 |
| Argentina: national holidays | [argentina.gob.ar](https://www.argentina.gob.ar/) `holidays-{year}-es.json` | CC BY 4.0, credited in every answer | Daily 05:00 |
| Argentina: provincial holidays | [Calendarios Nacionales](https://calendariosnacionales.com/) `{year}/provincias/{slug}.json` | [Terms](https://calendariosnacionales.com/terminos-y-condiciones/): a visible link in every answer, no abusive syncing (hence weekly) | Mondays 05:30 |
| Argentina: banking holidays | [BCRA](https://www.bcra.gob.ar/) `consulta-feriados-bancarios-{year}/` (scope `banking`) | No license published; the publisher is credited and linked | Daily 05:15 |

Next year's holiday file is optional until it is published; the current year's is required.

The BCRA's page names the Comunicaciones its year's list follows for the list as a whole, not per row (2026: C 101352,
amended by C 102390 and A 8487), so every row cites them all (`BCRA, Comunicaciones C 101352, C 102390 y A 8487`) and
links the first one's text; a page that names none leaves the rows without a legal basis rather than guessing one. On
a day the general calendar also names, business-day answers show the general observance, with its kind and legal
basis; when that observance was suppressed by hand, the general calendar no longer names the day and the BCRA's row is
shown. The 6 November sector rule cites CCT 18/75, art. 50. Provincial rows keep the source's link to their norm: SAIJ
links (`saij.gob.ar/view-document?guid=123456789-0abc-...`) look made up but are SAIJ's own id format (read backwards,
`...-230-5000avorpyel` is `leyprova 0005-032`, Salta's Ley 5.032).

The Migrator brings the catalog to the embedded snapshot after every migration, as a diff: a country or currency the
snapshot drops is retired, never deleted, so a code another module stored keeps resolving.

## Configuration

Every key is validated when the host starts; a missing or invalid one stops it naming the key. Crons are Quartz
expressions, seconds first. In environment variables, `:` becomes `__` (`Geo__Argentina__Holidays__Cron`).

| Key | Default | Meaning |
|-----|---------|---------|
| `ConnectionStrings:Geo` | (required) | Geo's database. |
| `Geo:Argentina:Georef:BaseUrl` | `https://infra.datos.gob.ar/georef/` | Georef's downloads (absolute, ending in `/`). |
| `Geo:Argentina:Territory:Cron` | `0 0 4 ? * MON` | Territory sync. |
| `Geo:Argentina:Holidays:BaseUrl` | `https://www.argentina.gob.ar/sites/default/files/` | National holiday files. |
| `Geo:Argentina:Holidays:Cron` | `0 0 5 * * ?` | National holiday sync. |
| `Geo:Argentina:ProvincialHolidays:BaseUrl` | `https://calendariosnacionales.com/ar/v1/` | Provincial holiday files. |
| `Geo:Argentina:ProvincialHolidays:Cron` | `0 30 5 ? * MON` | Provincial holiday sync; keep it weekly (the source's terms). |
| `Geo:Argentina:BankingHolidays:BaseUrl` | `https://www.bcra.gob.ar/` | BCRA banking holiday pages. |
| `Geo:Argentina:BankingHolidays:Cron` | `0 15 5 * * ?` | Banking holiday sync. |
| `Geo:SyncRequests:Cron` | `0/15 * * * * ?` | How often the Worker starts the runs requested through the API. |
| `Geo:MessagePurge:Cron` | `0 30 3 * * ?` | Purge of Geo's old outbox and inbox rows ([operations](operations.md#message-retention)). |

## Administration

These endpoints are **not authenticated yet**: each carries a `TODO(auth)` in the code, and they must not be exposed
publicly until authentication lands.

| Endpoint | What it does |
|----------|--------------|
| `PUT .../holidays/overrides?scope=` | Sets an observance by hand (`date`, `name`, `kind` required; `movedFrom`, `community` and `legalBasis` optional, with `legalBasis.citation` required in a basis and `legalBasis.url` optional), or replaces the source's of the same date and name. Every problem of the body comes at once, also when the date itself cannot be read. |
| `PUT .../holidays/suppressions?scope=` | Leaves the source's observance of `date` and `name` out, whatever the source says later; 422 `geo.observance.not_in_source` when the source has none (a hand-added one is removed with `DELETE`). |
| `DELETE .../holidays/overrides?date=&name=&scope=` | Removes an override or a suppression, so the source's observance counts again. |
| `GET holiday-syncs` | Every holiday source that ran, with its last outcome. |
| `GET holiday-syncs/{sourceId}/runs` | A source's latest 50 runs, with the override conflicts each found. |
| `POST holiday-syncs/{sourceId}/runs` | Asks for a run now: 202 with a `requestId` and `Location` to the source's runs. Requests made before the run starts share one run, which the Worker starts within `Geo:SyncRequests:Cron`. |
| `GET`, `POST subdivisions/{code}/aliases` | A subdivision's aliases; adds a manual one (201 with `Location`), which the territory sync never removes. An alias holds letters, digits, spaces and `' . , - ( ) &` (400 `geo.subdivision_alias.invalid`); one this subdivision already has by search form is 409 `geo.subdivision_alias.duplicate`, and one another subdivision of the country is known by is 409 `geo.subdivision_alias.taken`. Two administrators adding the same alias to two subdivisions of a country at once: a unique index on the country's manual aliases lets the first in, and the second gets the same 409 `geo.subdivision_alias.taken`. The index also counts a retired subdivision's manual aliases, so one of them answers the same 409 naming the retired code until an administrator removes it (`DELETE` accepts a retired subdivision's code for this). Not guarded by the database: an addition racing a territory sync that brings the same alias, or a subdivision's name or code, to another subdivision; source aliases may repeat across subdivisions. |
| `DELETE subdivisions/{code}/aliases/{alias}` | Removes a manual alias, matched ignoring case and accents; a source alias answers 409 `geo.subdivision_alias.imported`. |

`...` is `countries/{code}` or `subdivisions/{code}`. Corrections apply only to a calendar Geo holds (404
`geo.holidays.not_covered` otherwise) and win over later syncs; a sync that changes an overridden observance records a
conflict. Two writers on one calendar: the second gets 409 `geo.holiday_calendar.changed_concurrently`. A
`community` must be one the country declares (Argentina: `armenian`, `jewish`, `islamic`); a `legalBasis` needs a
`citation` (up to 200 characters) and its `url` is an absolute `http(s)` address (up to 500). Unknown JSON fields are
ignored, as everywhere in the API (the platform has no convention to reject them).

## Metrics

Geo's business metrics are on meter `Frappe.Geo`, exported by both hosts with every `Frappe.*` meter; traces come
from the platform's instrumentation. No tag carries personal data, typed text or a subdivision: only country codes,
source ids, scopes, outcomes and error codes.

| Metric | Type, unit | Tags | Reported by |
|--------|------------|------|-------------|
| `frappe.geo.sync.runs` | Counter, `{run}` | `frappe.geo.source`, `frappe.outcome` (`applied`, `unchanged`, `not_published`, `refused`, `failed`) | Worker |
| `frappe.geo.sync.last_success.age` | Observable gauge, `s` | `frappe.geo.source` | Worker |
| `frappe.geo.sync.override_conflicts` | Counter, `{conflict}` | `frappe.geo.source` | Worker |
| `frappe.geo.holidays.covered_through` | Observable gauge, `{year}` | `frappe.geo.country`, `frappe.geo.calendar.scope` | Worker |
| `frappe.geo.holidays.not_covered` | Counter, `{request}` | `frappe.geo.country`, `frappe.error.code` | Api (HTTP) and any host serving `IGeoQueries` |

- **Freshness.** `last_success.age` is the time since a source's last applied or unchanged run, read from
  `geo.sync_resources`; a source that never succeeded reports nothing. Alert when it exceeds about twice the source's
  schedule (for example more than 2 days for a daily source, 8 for a weekly one).
- **Coverage.** `covered_through` is the last year Geo holds a country-level calendar for, per scope. Alert when it is
  the current year late in the year: next year's calendar is not published or not synced yet.
- **Gaps in coverage.** `not_covered` counts questions answered "not covered" by country: requests for countries or
  years Geo does not cover yet.
- The gauges read the database at most once a minute; when it cannot be read they report nothing for that collection.
- **Cache effectiveness.** FusionCache's own instrumentation (meter `ZiggyCreatures.Caching.Fusion`, enabled by the
  platform) counts `fusioncache.cache.hit` and `fusioncache.cache.miss`: the hit ratio of the whole shared cache, of
  which Geo's calendars are one user (Geo adds no cache instrument of its own).

## Addresses

Geo suggests addresses while a user types and validates the chosen one into a normalized address:
street, number, postal code, locality, coordinates, and the ISO codes of the country and the
subdivision.

- **Provider.** Google Places API (New), behind `IAddressProvider`. The Api composes it with
  `AddGooglePlacesAddresses()`; the core never names Google, so another provider can replace it.
- **Key.** `Geo:Google:Places:ApiKey`. Without it every host still starts, and the address
  endpoints answer `503` with `geo.addresses.not_configured` and `Retry-After` (a provider outage or a rejected key answers `geo.addresses.unavailable`).
- **Endpoints.** `GET /v1/geo/address-suggestions?q=&session=&country=&language=` and
  `POST /v1/geo/address-validations` with the body `{ "reference", "session", "language" }`
  (`reference` is a suggestion's `id` or a stored snapshot's `reference`; `language` is optional).
  Both answer `Cache-Control: no-store` and are not authenticated yet (`TODO(auth)`). The
  reference travels in the body because URLs end up in access logs, traces and the `instance` of
  problems; bodies do not.
- **Sessions.** The client generates one UUID per search and sends it with every suggestion
  request and with the final validation, so the provider bills the search as one session.
- **Opaque ids.** Suggestion ids and address references hide the provider's ids. A malformed one
  answers `400` without calling the provider. They are not signed: a well-formed made-up reference
  only makes Geo ask the provider for a place that does not exist (`404`, or `503` when Google
  rejects the id for a reason it does not tie to the place).
- **Not found or unavailable.** Only an error Google ties to the place itself (`404 NOT_FOUND`, or
  `400 INVALID_ARGUMENT` blaming the place id) answers `404 geo.address.not_found`; a rejected or
  expired key, an exhausted quota, any other 400 or an answer that is not Google's is `503`, so a
  stored address never looks gone because the call failed. Google's error bodies are
  `google.rpc.Status` (<https://google.aip.dev/193>), with an `ErrorInfo` reason such as
  `API_KEY_INVALID`. `IGeoQueries.ResolveAddressAsync` answers `NotFound`, `Unavailable`,
  `InvalidReference` or `InvalidLanguage` the same way.
- **Subdivision codes.** Places answers province names, never ISO 3166-2 codes. Geo maps the name
  through its subdivision names and aliases (imported and manual); an unmapped one comes back as
  `null`.
- **Storage.** Geo stores no address. The validation answer is the snapshot the consuming module
  stores. Google Places' terms restrict storing Places content; reviewing that storage with legal
  counsel before production is the operator's responsibility. If needed, a provider whose terms
  allow storing the result (such as the Geocoding API) can replace this one without changing callers.
- **Privacy.** No log carries what the user typed or the resolved address. Place Details puts the place id in its path,
  so its HttpClient span shows `url.full` as `.../v1/places/REDACTED` (the Api registers
  `OutgoingRequestTelemetry.Redact` as the HttpClient instrumentation's enrichment).

## Adding a country

A country is a package under `src/Modules/Geo/Countries/Frappe.Geo.Countries.<Country>` (territory, holidays, sector
calendars, business-day rules); it never references another country's package, and the core names no country or
source. An adapter only declares: the core's one sync pipeline fetches, validates, applies and audits every source. Use `Frappe.Geo.Countries.Argentina` as the model.

1. **Sources.** Implement `ISnapshotSource<TerritorySnapshot>` or `ISnapshotSource<HolidaySnapshot>` per source: a
   stable `Id`, its files (`Resources`, each required or optional) and `Read`, which turns the downloaded files into a
   snapshot or refuses them. A holiday snapshot carries the source's `Attribution` (name, link, license) as its terms require.
2. **Registration.** An `Add<Country>(this IGeoSources geo)` extension binds and validates each source's options
   (`Geo:<Country>:<Source>:BaseUrl`) and registers each source with its job key, configuration section and default
   cron (`geo.Territory<TSource>(...)`, `geo.Holidays<TSource>(...)`). Respect each source's terms in its schedule.
3. **Calendar rules.** An `Add<Country>Calendar` extension declares the country's weekend, per policy which
   observances are non-working and which sector calendars (scopes) it needs, and the communities its community
   non-working days apply to. The scopes the policies name are the only ones the country answers besides `general`.
4. **Hosts.** The Worker composes the sources (`AddGeoSyncs(geo => geo.Add<Country>())`); every host that answers
   business-day questions (Api and Worker) composes the rules (`Add<Country>Calendar()`).
5. **Tests.** Trimmed real files as fixtures in `tests/Modules/Geo/Frappe.Geo.UnitTests/Fixtures/`, unit tests of
   `Read`, and a sync through the real pipeline against an in-process source in the integration tests.
6. **Docs.** Add its sources, terms, schedules and keys to this page.
