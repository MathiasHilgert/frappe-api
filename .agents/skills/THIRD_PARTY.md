# Third-party skills

Vendored unmodified. Each directory carries the upstream repository `LICENSE`, copied in because the skill directory has none of its own. Update by re-copying the directory from a newer upstream commit and editing this table.

| Skill | Source | Commit | License |
| --- | --- | --- | --- |
| `126-java-exception-handling` | https://github.com/jabrena/plinth (`skills/`) | `9a3dc2923f01af913fd0bb01b96879ba8f5bc4f6` | Apache-2.0 |
| `181-java-observability-logging` | https://github.com/jabrena/plinth (`skills/`) | `9a3dc2923f01af913fd0bb01b96879ba8f5bc4f6` | Apache-2.0 |
| `182-java-observability-metrics-micrometer` | https://github.com/jabrena/plinth (`skills/`) | `9a3dc2923f01af913fd0bb01b96879ba8f5bc4f6` | Apache-2.0 |
| `183-java-observability-tracing-opentelemetry` | https://github.com/jabrena/plinth (`skills/`) | `9a3dc2923f01af913fd0bb01b96879ba8f5bc4f6` | Apache-2.0 |
| `305-frameworks-spring-boot-modulith` | https://github.com/jabrena/plinth (`skills/`) | `9a3dc2923f01af913fd0bb01b96879ba8f5bc4f6` | Apache-2.0 |
| `321-frameworks-spring-boot-testing-unit-tests` | https://github.com/jabrena/plinth (`skills/`) | `9a3dc2923f01af913fd0bb01b96879ba8f5bc4f6` | Apache-2.0 |
| `322-frameworks-spring-boot-testing-integration-tests` | https://github.com/jabrena/plinth (`skills/`) | `9a3dc2923f01af913fd0bb01b96879ba8f5bc4f6` | Apache-2.0 |
| `opentelemetry` | https://github.com/grafana/skills (`skills/grafana-core/`) | `05196628fa1a6a557fad1a5623156170693607eb` | Apache-2.0 |
| `prometheus-label-strategy` | https://github.com/grafana/skills (`skills/grafana-cloud/`) | `05196628fa1a6a557fad1a5623156170693607eb` | Apache-2.0 |
| `promql` | https://github.com/grafana/skills (`skills/grafana-core/`) | `05196628fa1a6a557fad1a5623156170693607eb` | Apache-2.0 |
| `slo-implementation` | https://github.com/wshobson/agents (`plugins/observability-monitoring/skills/`) | `4236bb91f8395b0435f1d8b8baf9e8e4c69a8620` | MIT |
| `supabase-postgres-best-practices` | https://github.com/supabase/agent-skills (`skills/`) | `8331f910845103c08d51f6ca1d86ebb7d1f745e3` | MIT |

Where our standards conflict with a vendored skill, ours win; the observability conflicts are listed in `observing-the-api`.

Copied from a clone instead of `npx skills add` so the files land directly in `.agents/skills` without symlinks or a lock file.
