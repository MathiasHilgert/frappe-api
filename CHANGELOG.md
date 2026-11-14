# Changelog

## 0.1.0 (2026-11-14)


### Features

* **geo:** add provincial holidays from Calendarios Nacionales ([#19](https://github.com/MathiasHilgert/frappe-api/issues/19)) ([79ad85d](https://github.com/MathiasHilgert/frappe-api/commit/79ad85d6cb92fa4694908eb2997d13c2f090b5d2))
* **geo:** answer business-day questions per country, region and policy ([#31](https://github.com/MathiasHilgert/frappe-api/issues/31)) ([8068572](https://github.com/MathiasHilgert/frappe-api/commit/806857229a030f75cf400b3adc1e1d5c3567a03a))
* **geo:** autocomplete and validate addresses through Google Places ([#33](https://github.com/MathiasHilgert/frappe-api/issues/33)) ([cd1d32d](https://github.com/MathiasHilgert/frappe-api/commit/cd1d32d16c9515cc3199f66c4aafd509681e4f4f))
* **geo:** let administrators override holidays and trigger syncs ([#21](https://github.com/MathiasHilgert/frappe-api/issues/21)) ([d5aba2b](https://github.com/MathiasHilgert/frappe-api/commit/d5aba2ba0a9c0a5d8a5005230d73d20bfbebdc6e))
* **geo:** let other modules query Geo synchronously in process ([#32](https://github.com/MathiasHilgert/frappe-api/issues/32)) ([54484bb](https://github.com/MathiasHilgert/frappe-api/commit/54484bb98f307ac74651455d0229b5cd9e006a00))
* **geo:** measure address searches, costs and abandoned sessions ([#34](https://github.com/MathiasHilgert/frappe-api/issues/34)) ([09355cf](https://github.com/MathiasHilgert/frappe-api/commit/09355cf9e1eb0bf4b11ceff97878decb26de1cd9))
* **geo:** serve countries and currencies from CLDR ([#15](https://github.com/MathiasHilgert/frappe-api/issues/15)) ([b1992d2](https://github.com/MathiasHilgert/frappe-api/commit/b1992d24688c51e80b179c8b01b266bd3108091a))
* **geo:** serve national holidays per country and year ([#18](https://github.com/MathiasHilgert/frappe-api/issues/18)) ([14f81d8](https://github.com/MathiasHilgert/frappe-api/commit/14f81d8e02abeaa3fb6185cb047e76905cc1a792))
* **geo:** serve subdivisions, administrative areas and localities ([#17](https://github.com/MathiasHilgert/frappe-api/issues/17)) ([64eccc6](https://github.com/MathiasHilgert/frappe-api/commit/64eccc62eee5a8d17209ab6d45f3c24f67b27d04))
* **geo:** serve the banking calendar and each country's calendar rules ([#20](https://github.com/MathiasHilgert/frappe-api/issues/20)) ([180d5a5](https://github.com/MathiasHilgert/frappe-api/commit/180d5a53d9dccaae2d0b2b5825290ab813d610f8))
* **geo:** sync the Argentine territory from Georef through a generic snapshot pipeline ([#16](https://github.com/MathiasHilgert/frappe-api/issues/16)) ([71cdd65](https://github.com/MathiasHilgert/frappe-api/commit/71cdd65dd617098ebcab248982499aa71c6042df))
* **platform:** add an EF Core unit of work and a two-level cache backed by Valkey ([#10](https://github.com/MathiasHilgert/frappe-api/issues/10)) ([9e977b9](https://github.com/MathiasHilgert/frappe-api/commit/9e977b9c66d2b18ba0a9e6ea4e187a8261b6c55f))
* **platform:** add result, use case, localization and aggregate building blocks for every module ([#2](https://github.com/MathiasHilgert/frappe-api/issues/2)) ([a71cb1f](https://github.com/MathiasHilgert/frappe-api/commit/a71cb1f1a8bf107dc4844a108bba4cdbd696d8ca))
* **platform:** answer every HTTP request with problem details, caching and language conventions ([#3](https://github.com/MathiasHilgert/frappe-api/issues/3)) ([480ddf7](https://github.com/MathiasHilgert/frappe-api/commit/480ddf750fb8a10a1960108f8643c43d2f4e6bd3))
* **platform:** publish and consume integration events through a NATS outbox and inbox ([#11](https://github.com/MathiasHilgert/frappe-api/issues/11)) ([70eae36](https://github.com/MathiasHilgert/frappe-api/commit/70eae36d46b5ff4e97c5558848d894d5c884f077))
* **platform:** schedule cron jobs once across Worker replicas with clustered Quartz ([#12](https://github.com/MathiasHilgert/frappe-api/issues/12)) ([6d54f82](https://github.com/MathiasHilgert/frappe-api/commit/6d54f8227f41d9775e12f27be2830748c239afcc))
* **platform:** trace, measure and log every use case and outgoing call with OpenTelemetry ([#4](https://github.com/MathiasHilgert/frappe-api/issues/4)) ([3410f61](https://github.com/MathiasHilgert/frappe-api/commit/3410f610ac6f1483b946e71fd46653233bc04a98))


### Bug fixes

* **deploy:** keep restored packages in the image's restore layer ([#24](https://github.com/MathiasHilgert/frappe-api/issues/24)) ([b54181d](https://github.com/MathiasHilgert/frappe-api/commit/b54181d01b50bf1f4d273c7b93c489c8807c58fb))
* **migrator:** wait for the migration lock without holding a transaction open ([#26](https://github.com/MathiasHilgert/frappe-api/issues/26)) ([b139748](https://github.com/MathiasHilgert/frappe-api/commit/b1397486e60bd918ef884b6bdacafeb97d6cc0e4))


### Continuous integration

* **github:** check compatibility and security, rebuild nightly and automate releases ([#14](https://github.com/MathiasHilgert/frappe-api/issues/14)) ([02def9e](https://github.com/MathiasHilgert/frappe-api/commit/02def9e61697a7da0b44c6ca6aad3f16837717dc))
* **github:** check pull request conventions, formatting and code quality on every pull request ([#1](https://github.com/MathiasHilgert/frappe-api/issues/1)) ([7174913](https://github.com/MathiasHilgert/frappe-api/commit/7174913374dd6d1fc03635c62c1eb95826e77d98))
* **github:** define every pull request label with a color and a description ([#27](https://github.com/MathiasHilgert/frappe-api/issues/27)) ([c5bcdbe](https://github.com/MathiasHilgert/frappe-api/commit/c5bcdbefd1aadc636910ebb2b8a3ecd8a878045b))
* **github:** keep releases at 0.x until the first product version ([#28](https://github.com/MathiasHilgert/frappe-api/issues/28)) ([4bdf670](https://github.com/MathiasHilgert/frappe-api/commit/4bdf67044006a2904bd8febeee1fbe6695d611d8))
* **github:** open the release pull request with a GitHub App so it gets its checks ([#35](https://github.com/MathiasHilgert/frappe-api/issues/35)) ([6367df9](https://github.com/MathiasHilgert/frappe-api/commit/6367df954991c18e5b98ae9ed98764436f00c144))
