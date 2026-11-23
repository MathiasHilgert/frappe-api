# Changelog

## [0.2.1](https://github.com/MathiasHilgert/frappe-api/compare/v0.2.0...v0.2.1) (2026-11-23)


### Bug fixes

* **http:** pin proxy trust, retry probe listeners and separate cache key spaces ([#14](https://github.com/MathiasHilgert/frappe-api/issues/14)) ([aa06903](https://github.com/MathiasHilgert/frappe-api/commit/aa069037256607cb12c31fd8912dfcc10ca660ef))
* **messaging:** quote relay role names and count relay, consumer and purge failures ([#13](https://github.com/MathiasHilgert/frappe-api/issues/13)) ([c32d350](https://github.com/MathiasHilgert/frappe-api/commit/c32d3504bf645a33ef9fab38b97e40ff7d0dc97c))
* **persistence:** dispatch events once, roll back failed begins and harden provisioning ([#12](https://github.com/MathiasHilgert/frappe-api/issues/12)) ([3e23321](https://github.com/MathiasHilgert/frappe-api/commit/3e23321252ff64632c19b6019a995d0e1f590544))
* **worker:** run cron in UTC, report scheduler state and bound local telemetry retention ([#15](https://github.com/MathiasHilgert/frappe-api/issues/15)) ([13d27d5](https://github.com/MathiasHilgert/frappe-api/commit/13d27d5fd0f800e7f39312750fcda5e8ce829e4a))


### Build and dependencies

* **agents:** fix the forge gate and slots and neutralize rule examples ([#16](https://github.com/MathiasHilgert/frappe-api/issues/16)) ([5d97fd8](https://github.com/MathiasHilgert/frappe-api/commit/5d97fd8c4b609324a2865b3519bb2e23b73b9ab6))

## [0.2.0](https://github.com/MathiasHilgert/frappe-api/compare/v0.1.0...v0.2.0) (2026-11-23)


### Features

* **api:** compose the Api and Migrator hosts and the local stack ([#9](https://github.com/MathiasHilgert/frappe-api/issues/9)) ([5536e8a](https://github.com/MathiasHilgert/frappe-api/commit/5536e8a1f29363e2ecf5720f4c7c3bb1e6e1ae29))
* **worker:** run scheduled jobs and consumers in the Worker host ([#8](https://github.com/MathiasHilgert/frappe-api/issues/8)) ([66bb28b](https://github.com/MathiasHilgert/frappe-api/commit/66bb28b3162d885c2706423815e29bfff22fa548))

## 0.1.0 (2026-11-23)


### Features

* **http:** serve problem details, rate limits, idempotency, paging and OpenAPI ([#5](https://github.com/MathiasHilgert/frappe-api/issues/5)) ([00bde15](https://github.com/MathiasHilgert/frappe-api/commit/00bde1532cba6fbe007cf4f219a75d0189d439bb))
* **messaging:** publish and consume integration events through an outbox ([#7](https://github.com/MathiasHilgert/frappe-api/issues/7)) ([e263d10](https://github.com/MathiasHilgert/frappe-api/commit/e263d10c22f9f4ecee7a3196feb8756c19f47f57))
* **persistence:** add module databases, the unit of work and telemetry ([#6](https://github.com/MathiasHilgert/frappe-api/issues/6)) ([6817bd4](https://github.com/MathiasHilgert/frappe-api/commit/6817bd44c0b1969835ba91d6b3613bde55593bfe))
* **platform:** add results, use cases, localization, health and caching ([#3](https://github.com/MathiasHilgert/frappe-api/issues/3)) ([cfb1e1f](https://github.com/MathiasHilgert/frappe-api/commit/cfb1e1f15add5f26bd23fc3bacae54666d8af2d2))


### Documentation

* **agents:** add the forge workflow, code conventions and roadmap ([#2](https://github.com/MathiasHilgert/frappe-api/issues/2)) ([c1b5a64](https://github.com/MathiasHilgert/frappe-api/commit/c1b5a648335a54e4dcc0d319f36e2038723671be))


### Build and dependencies

* **repo:** set up the solution, tooling and continuous integration ([#1](https://github.com/MathiasHilgert/frappe-api/issues/1)) ([b5fa159](https://github.com/MathiasHilgert/frappe-api/commit/b5fa159eeda17d8419139c74328c30557e81ad4d))
