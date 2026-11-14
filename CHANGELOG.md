# Changelog

## 1.0.0 (2026-11-14)


### Features

* **geo:** add provincial holidays from Calendarios Nacionales ([#19](https://github.com/MathiasHilgert/frappe-api/issues/19)) ([79ad85d](https://github.com/MathiasHilgert/frappe-api/commit/79ad85d6cb92fa4694908eb2997d13c2f090b5d2))
* **geo:** serve countries and currencies from CLDR ([#15](https://github.com/MathiasHilgert/frappe-api/issues/15)) ([b1992d2](https://github.com/MathiasHilgert/frappe-api/commit/b1992d24688c51e80b179c8b01b266bd3108091a))
* **geo:** serve national holidays per country and year ([#18](https://github.com/MathiasHilgert/frappe-api/issues/18)) ([14f81d8](https://github.com/MathiasHilgert/frappe-api/commit/14f81d8e02abeaa3fb6185cb047e76905cc1a792))
* **geo:** serve subdivisions, administrative areas and localities ([#17](https://github.com/MathiasHilgert/frappe-api/issues/17)) ([64eccc6](https://github.com/MathiasHilgert/frappe-api/commit/64eccc62eee5a8d17209ab6d45f3c24f67b27d04))
* **geo:** sync the Argentine territory from Georef through a generic snapshot pipeline ([#16](https://github.com/MathiasHilgert/frappe-api/issues/16)) ([71cdd65](https://github.com/MathiasHilgert/frappe-api/commit/71cdd65dd617098ebcab248982499aa71c6042df))
* **platform:** add an EF Core unit of work and a two-level cache backed by Valkey ([#10](https://github.com/MathiasHilgert/frappe-api/issues/10)) ([9e977b9](https://github.com/MathiasHilgert/frappe-api/commit/9e977b9c66d2b18ba0a9e6ea4e187a8261b6c55f))
* **platform:** add result, use case, localization and aggregate building blocks for every module ([#2](https://github.com/MathiasHilgert/frappe-api/issues/2)) ([a71cb1f](https://github.com/MathiasHilgert/frappe-api/commit/a71cb1f1a8bf107dc4844a108bba4cdbd696d8ca))
* **platform:** answer every HTTP request with problem details, caching and language conventions ([#3](https://github.com/MathiasHilgert/frappe-api/issues/3)) ([480ddf7](https://github.com/MathiasHilgert/frappe-api/commit/480ddf750fb8a10a1960108f8643c43d2f4e6bd3))
* **platform:** publish and consume integration events through a NATS outbox and inbox ([#11](https://github.com/MathiasHilgert/frappe-api/issues/11)) ([70eae36](https://github.com/MathiasHilgert/frappe-api/commit/70eae36d46b5ff4e97c5558848d894d5c884f077))
* **platform:** schedule cron jobs once across Worker replicas with clustered Quartz ([#12](https://github.com/MathiasHilgert/frappe-api/issues/12)) ([6d54f82](https://github.com/MathiasHilgert/frappe-api/commit/6d54f8227f41d9775e12f27be2830748c239afcc))
* **platform:** trace, measure and log every use case and outgoing call with OpenTelemetry ([#4](https://github.com/MathiasHilgert/frappe-api/issues/4)) ([3410f61](https://github.com/MathiasHilgert/frappe-api/commit/3410f610ac6f1483b946e71fd46653233bc04a98))


### Bug fixes

* **deploy:** keep restored packages in the image's restore layer ([#24](https://github.com/MathiasHilgert/frappe-api/issues/24)) ([b54181d](https://github.com/MathiasHilgert/frappe-api/commit/b54181d01b50bf1f4d273c7b93c489c8807c58fb))


### Continuous integration

* **github:** check compatibility and security, rebuild nightly and automate releases ([#14](https://github.com/MathiasHilgert/frappe-api/issues/14)) ([02def9e](https://github.com/MathiasHilgert/frappe-api/commit/02def9e61697a7da0b44c6ca6aad3f16837717dc))
* **github:** check pull request conventions, formatting and code quality on every pull request ([#1](https://github.com/MathiasHilgert/frappe-api/issues/1)) ([7174913](https://github.com/MathiasHilgert/frappe-api/commit/7174913374dd6d1fc03635c62c1eb95826e77d98))
