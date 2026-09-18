# Documentation

- Javadoc on every type and every member visible at package level or wider; `package-info.java` for every package, describing its role. The gate enforces it: `./gradlew javadoc` runs with `-Xdoclint:all -Werror` at package member level as part of `check`.
- Say what the element is for and its contract (`@param`, `@return`, `@throws` for expected failures), not how the code reads.
- Inline comments explain WHY for non-obvious logic: concurrency and ordering, retries, deduplication, framework constraints, deliberate trade-offs. No comments restating the code, no commented-out code, no TODOs without a ticket.
- English only.
