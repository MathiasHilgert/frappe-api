# Changelog

## [0.5.0](https://github.com/MathiasHilgert/frappe-api/compare/v0.4.1...v0.5.0) (2026-11-25)


### ⚠ BREAKING CHANGES

* **identity:** authorize sessions through access and rename denial codes ([#65](https://github.com/MathiasHilgert/frappe-api/issues/65))

### Features

* **access:** add the access module, its rules and the use-case gate ([#62](https://github.com/MathiasHilgert/frappe-api/issues/62)) ([f57d0e2](https://github.com/MathiasHilgert/frappe-api/commit/f57d0e2743a495df4d7267e7c6a67a168a600354))
* **access:** require every use case to declare who may run it ([#63](https://github.com/MathiasHilgert/frappe-api/issues/63)) ([d53e856](https://github.com/MathiasHilgert/frappe-api/commit/d53e856949e296dcd7de080d52242b9cddffe89d))
* **identity:** authorize sessions through access and rename denial codes ([#65](https://github.com/MathiasHilgert/frappe-api/issues/65)) ([7c9abe3](https://github.com/MathiasHilgert/frappe-api/commit/7c9abe3ee5afe1bdba020f48d2d00857992892c2))


### Refactoring

* **access:** enter the access module through one AccessModule ([#74](https://github.com/MathiasHilgert/frappe-api/issues/74)) ([b2502c6](https://github.com/MathiasHilgert/frappe-api/commit/b2502c61e330fbbcd5841bb9b9ab71f6ea2a0100))
* **identity:** enter the identity module through one IdentityModule ([#75](https://github.com/MathiasHilgert/frappe-api/issues/75)) ([7dba50a](https://github.com/MathiasHilgert/frappe-api/commit/7dba50a69b572d389babb7a737e4ff319409c3c7))
* **identity:** take the caller from the session instead of command fields ([#64](https://github.com/MathiasHilgert/frappe-api/issues/64)) ([b666890](https://github.com/MathiasHilgert/frappe-api/commit/b66689095985669a23107260036d2d663d1111e8))
* **modules:** move plug-ins to extensions with names that say what they do ([#78](https://github.com/MathiasHilgert/frappe-api/issues/78)) ([040eb3b](https://github.com/MathiasHilgert/frappe-api/commit/040eb3be0d0ef2ac6d66bd951518be56db9ce8cd))
* **modules:** put each module's metrics in telemetry, one class per feature ([#79](https://github.com/MathiasHilgert/frappe-api/issues/79)) ([9213b2c](https://github.com/MathiasHilgert/frappe-api/commit/9213b2c615c27ab86e38ff572529efccacfbca36))
* **notifications:** enter the notifications module through one NotificationsModule ([#76](https://github.com/MathiasHilgert/frappe-api/issues/76)) ([04f8b7d](https://github.com/MathiasHilgert/frappe-api/commit/04f8b7d302f50d6796b03d5e2f266c4cc666cc44))
* **notifications:** finish the vocabulary rename and check module READMEs against code ([#90](https://github.com/MathiasHilgert/frappe-api/issues/90)) ([e653453](https://github.com/MathiasHilgert/frappe-api/commit/e653453008778c7b5f67a1ebb2ee0680ea41e52a))
* **notifications:** name each feature the same in every layer ([#87](https://github.com/MathiasHilgert/frappe-api/issues/87)) ([720406b](https://github.com/MathiasHilgert/frappe-api/commit/720406bf98ac7cd4e2a6a41c1df2842612596d69))


### Documentation

* **conventions:** rewrite architecture and structure rules with this repo's modules ([#82](https://github.com/MathiasHilgert/frappe-api/issues/82)) ([f1bc5b6](https://github.com/MathiasHilgert/frappe-api/commit/f1bc5b61f4b42ac6338980d4f79fd586e8ec52a4))
* **conventions:** rewrite domain, http, events, design and criteria rules with real examples ([#83](https://github.com/MathiasHilgert/frappe-api/issues/83)) ([a1d8082](https://github.com/MathiasHilgert/frappe-api/commit/a1d8082859b6a12b2030f5a61726d0798d9614d4))
* **conventions:** rewrite the remaining rule cards with real examples ([#84](https://github.com/MathiasHilgert/frappe-api/issues/84)) ([3e0ff4c](https://github.com/MathiasHilgert/frappe-api/commit/3e0ff4cd034bf7ac5185a2f00c73a2ff9cc2f2fb))
* **modules:** add a readme map per module ([#81](https://github.com/MathiasHilgert/frappe-api/issues/81)) ([636c6d9](https://github.com/MathiasHilgert/frappe-api/commit/636c6d91c61da95f82c6612f13ac369e41aaf1c4))


### Build and dependencies

* **analyzers:** enforce masked ToString, no EF retries and justified suppressions ([#70](https://github.com/MathiasHilgert/frappe-api/issues/70)) ([aed80cc](https://github.com/MathiasHilgert/frappe-api/commit/aed80cc1847c1edf85524ca6e73ce41b1a275325))
* **scaffold:** add a crud recipe with entity and repository templates ([#69](https://github.com/MathiasHilgert/frappe-api/issues/69)) ([e15b2e0](https://github.com/MathiasHilgert/frappe-api/commit/e15b2e06765caf890795a5e91df129e8565b9381))


### Continuous integration

* **solution:** build the image only when a job that uses it runs ([#71](https://github.com/MathiasHilgert/frappe-api/issues/71)) ([b31eb51](https://github.com/MathiasHilgert/frappe-api/commit/b31eb516f7dc343ecff17d3a7269298fe040a339))
* **solution:** run only the test suites a pull request can break ([#73](https://github.com/MathiasHilgert/frappe-api/issues/73)) ([e63d11d](https://github.com/MathiasHilgert/frappe-api/commit/e63d11d86f5828177ac67fae0f383cd7ed43de80))
* **solution:** skip the end-to-end suite on pull requests, covered by bruno ([#72](https://github.com/MathiasHilgert/frappe-api/issues/72)) ([fd34420](https://github.com/MathiasHilgert/frappe-api/commit/fd3442015401eedb4d48a2ec7abbbaa0c210ca56))

## [0.4.1](https://github.com/MathiasHilgert/frappe-api/compare/v0.4.0...v0.4.1) (2026-11-24)


### Bug fixes

* **identity:** refuse line separators in names everywhere they reach a mail ([#60](https://github.com/MathiasHilgert/frappe-api/issues/60)) ([8687435](https://github.com/MathiasHilgert/frappe-api/commit/8687435d05e70057c5859da16402445e8611016b))

## [0.4.0](https://github.com/MathiasHilgert/frappe-api/compare/v0.3.0...v0.4.0) (2026-11-24)


### Features

* **http:** document answers without a location and allow own ids under me ([#37](https://github.com/MathiasHilgert/frappe-api/issues/37)) ([0401453](https://github.com/MathiasHilgert/frappe-api/commit/0401453e08a065eefa7394ea43fe2560b879fe4a))
* **identity:** add the identity module, its schema, roles and event contracts ([#38](https://github.com/MathiasHilgert/frappe-api/issues/38)) ([20aae2e](https://github.com/MathiasHilgert/frappe-api/commit/20aae2eb36a21076558b0069af52635d6f698a82))
* **identity:** authenticate sessions by cookie or bearer token ([#41](https://github.com/MathiasHilgert/frappe-api/issues/41)) ([d70d9fd](https://github.com/MathiasHilgert/frappe-api/commit/d70d9fd94342ab3e29689bd900afd37c21afa6e9))
* **identity:** change an owner's email with step-up and notice ([#46](https://github.com/MathiasHilgert/frappe-api/issues/46)) ([b7f70f7](https://github.com/MathiasHilgert/frappe-api/commit/b7f70f7b05e451d831fe383b935f96c4942754f4))
* **identity:** hash and check owner passwords ([#39](https://github.com/MathiasHilgert/frappe-api/issues/39)) ([2db1a23](https://github.com/MathiasHilgert/frappe-api/commit/2db1a23ba675b57c615cb00892bd12511d341ef2))
* **identity:** issue and redeem one-time codes under attempt limits ([#40](https://github.com/MathiasHilgert/frappe-api/issues/40)) ([a854f93](https://github.com/MathiasHilgert/frappe-api/commit/a854f93686e968173d40b7a3f2000c8a357cbb80))
* **identity:** list and revoke own sessions ([#48](https://github.com/MathiasHilgert/frappe-api/issues/48)) ([8c793b3](https://github.com/MathiasHilgert/frappe-api/commit/8c793b39dfbfc4a30073c0475d4c2c1f30897b2a))
* **identity:** log owners in and serve their profile ([#43](https://github.com/MathiasHilgert/frappe-api/issues/43)) ([923c92a](https://github.com/MathiasHilgert/frappe-api/commit/923c92a0815687bcae28ef55e627f1d653c084b2))
* **identity:** register owners and verify their email ([#42](https://github.com/MathiasHilgert/frappe-api/issues/42)) ([28671fe](https://github.com/MathiasHilgert/frappe-api/commit/28671fe63e9dce83f953e031465278665a6a773a))
* **identity:** reset and change owner passwords ([#44](https://github.com/MathiasHilgert/frappe-api/issues/44)) ([ffcd8f8](https://github.com/MathiasHilgert/frappe-api/commit/ffcd8f876cdc9dcc0ad490a026a08aa02a0b832c))
* **identity:** step up the current session ([#45](https://github.com/MathiasHilgert/frappe-api/issues/45)) ([2b575a8](https://github.com/MathiasHilgert/frappe-api/commit/2b575a8477ecdf00c3da454069aaa6ecd0d65e1e))
* **identity:** sweep ended sessions and purge stale identity data ([#49](https://github.com/MathiasHilgert/frappe-api/issues/49)) ([3ca1615](https://github.com/MathiasHilgert/frappe-api/commit/3ca1615dd82ae5256b930e51fba194d94a3e52c4))
* **identity:** undo an email change from the notice ([#47](https://github.com/MathiasHilgert/frappe-api/issues/47)) ([57bedc0](https://github.com/MathiasHilgert/frappe-api/commit/57bedc0a1592bb7d05740e10cb8353f875571272))
* **notifications:** mail the old address an undo link after an email change ([#54](https://github.com/MathiasHilgert/frappe-api/issues/54)) ([d742b55](https://github.com/MathiasHilgert/frappe-api/commit/d742b5576a83e04a509e685977bd2a0d6a7ecdc3))
* **notifications:** send identity mails through an identity producer ([#53](https://github.com/MathiasHilgert/frappe-api/issues/53)) ([fd2e3b1](https://github.com/MathiasHilgert/frappe-api/commit/fd2e3b122ba79cdd174f54629fc3187da20669ca))


### Bug fixes

* **identity:** answer races clearly, purge undo links and bound notice mails ([#55](https://github.com/MathiasHilgert/frappe-api/issues/55)) ([cf90dc8](https://github.com/MathiasHilgert/frappe-api/commit/cf90dc887a65627de25129467815b13447544f6e))
* **identity:** index codes by principal, list live sessions one way and align names ([#56](https://github.com/MathiasHilgert/frappe-api/issues/56)) ([1bdd51a](https://github.com/MathiasHilgert/frappe-api/commit/1bdd51a590cc729da0340fcd77a9bb121a0c8b68))

## [0.3.0](https://github.com/MathiasHilgert/frappe-api/compare/v0.2.1...v0.3.0) (2026-11-24)


### Features

* **notifications:** add the notifications module with email delivery through resend ([#27](https://github.com/MathiasHilgert/frappe-api/issues/27)) ([314e174](https://github.com/MathiasHilgert/frappe-api/commit/314e1740796670e9a07cfb05eace01780ef3c69b))


### Bug fixes

* **notifications:** enforce delivery invariants in the schema and survive hash-key rotation ([#35](https://github.com/MathiasHilgert/frappe-api/issues/35)) ([fdcacfb](https://github.com/MathiasHilgert/frappe-api/commit/fdcacfb37cc17a1118b212074b60bcbdf58077a6))
* **notifications:** validate every payload and recipient before anything is sent ([#32](https://github.com/MathiasHilgert/frappe-api/issues/32)) ([4fe2bcf](https://github.com/MathiasHilgert/frappe-api/commit/4fe2bcfa6f9c645f8167b2460e0c0006d6c6fb29))
* **notifications:** verify unsubscribe tokens before rate limiting and guard email texts at startup ([#33](https://github.com/MathiasHilgert/frappe-api/issues/33)) ([cc90c7c](https://github.com/MathiasHilgert/frappe-api/commit/cc90c7cba5ceba7cc0c75e4c4a01cce74dd4ad0c))


### Refactoring

* **messaging:** move outbox storage and broker declarations out of the broker assembly ([#20](https://github.com/MathiasHilgert/frappe-api/issues/20)) ([fff175c](https://github.com/MathiasHilgert/frappe-api/commit/fff175cb9eecb243f9c2aaf4a6acdcfe16f202dd))


### Documentation

* **conventions:** adopt the rule updates the notifications module proved ([#30](https://github.com/MathiasHilgert/frappe-api/issues/30)) ([364d2e9](https://github.com/MathiasHilgert/frappe-api/commit/364d2e95aa3480f0c83e069c4e05a053ec8ad56c))


### Continuous integration

* **image:** scan the image only when its dockerfile or dependencies change ([#36](https://github.com/MathiasHilgert/frappe-api/issues/36)) ([1f7d246](https://github.com/MathiasHilgert/frappe-api/commit/1f7d2467d141f403c881e5ba827e1549150c9531))

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
