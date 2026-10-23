# Single source of truth for local and CI quality gates. CI jobs call these recipes.
set shell := ["bash", "-euo", "pipefail", "-c"]

configuration := env("CONFIGURATION", "Release")
coverage_min  := env("COVERAGE_MIN", "70")
coverage_dir  := "artifacts/coverage"
mutation_dir  := "artifacts/mutation"
image_tag     := env("IMAGE", "frappe-api:local")

# Pinned tool images.
actionlint_image := "rhysd/actionlint:1.7.7"
hadolint_image   := "hadolint/hadolint:v2.12.0"
gitleaks_image   := "zricethezav/gitleaks:v8.24.3"
trivy_image      := "aquasec/trivy:0.62.1"

# Test projects are discovered: *IntegrationTests need Docker, every other test project is fast.
fast_tests        := `find tests -name '*.csproj' ! -name '*IntegrationTests.csproj' -printf '%h\n' 2>/dev/null | sort | tr '\n' ' '`
integration_tests := `find tests -name '*IntegrationTests.csproj' -printf '%h\n' 2>/dev/null | sort | tr '\n' ' '`
# Stryker pairs: <test project dir>:<source csproj name>.
mutation_targets  := "tests/BuildingBlocks/Frappe.SharedKernel.Tests:Frappe.SharedKernel.csproj tests/Modules/Frappe.Identity.Tests:Frappe.Identity.Domain.csproj"

root       := justfile_directory()
dockerfile := "deploy/Dockerfile"
compose    := "docker compose -f deploy/compose.yaml"
docker_run := "docker run --rm -v " + quote(root) + ":/repo -w /repo"

# List recipes
default:
    @just --list --unsorted

# Restore packages and local dotnet tools
[group('dev')]
setup:
    dotnet tool restore
    dotnet restore

# Run the API with the http launch profile
[group('dev')]
run:
    dotnet run --project src/Host/Frappe.Api --launch-profile http

# Start the local stack (docker compose)
[group('dev')]
up:
    {{compose}} up -d

# Stop the local stack
[group('dev')]
down:
    {{compose}} down




# Remove bin/obj, TestResults and coverage/mutation output
[group('dev')]
clean:
    rm -rf artifacts
    find src tests -type d \( -name bin -o -name obj -o -name TestResults -o -name StrykerOutput \) -prune -exec rm -rf {} +

# Apply every autofix (dotnet format)
[group('quality')]
fix: setup
    dotnet format --no-restore

# Every static check: formatting, actionlint, hadolint, gitleaks
[group('quality')]
lint: _format-check _actionlint _hadolint _gitleaks

_format-check: setup
    dotnet format --verify-no-changes --no-restore

_actionlint:
    {{docker_run}} {{actionlint_image}} -color

_hadolint:
    if [[ -f {{dockerfile}} ]]; then {{docker_run}} {{hadolint_image}} hadolint {{dockerfile}}; else echo "No {{dockerfile}} yet; skipping hadolint."; fi

_gitleaks:
    {{docker_run}} {{gitleaks_image}} dir /repo --config /repo/.config/gitleaks.toml --no-banner --redact --exit-code 1

# Release build of the whole solution
[group('test')]
build: setup
    dotnet build --no-restore -c {{configuration}}

# Run tests; suite is fast | integration | all
[group('test')]
test suite="all":
    case "{{suite}}" in \
      fast) projects="{{fast_tests}}" ;; \
      integration) projects="{{integration_tests}}" ;; \
      all) projects="{{fast_tests}} {{integration_tests}}" ;; \
      *) echo "Unknown suite '{{suite}}'. Use one of: fast | integration | all" >&2; exit 2 ;; \
    esac; \
    if [[ -z "${projects// }" ]]; then echo "No {{suite}} test projects yet; nothing to run."; exit 0; fi; \
    for p in $projects; do dotnet test --project "$p" -c {{configuration}}; done

# Tests with coverage, merged report; fails under COVERAGE_MIN% line coverage
[group('test')]
cover: setup
    rm -rf {{coverage_dir}}
    for p in {{fast_tests}} {{integration_tests}}; do \
      dotnet test --project "$p" -c {{configuration}} --coverage --coverage-output-format cobertura \
        --coverage-output coverage.cobertura.xml --results-directory "{{coverage_dir}}/raw/$(basename "$p")"; \
    done
    dotnet reportgenerator '-reports:{{coverage_dir}}/raw/*/coverage.cobertura.xml' '-targetdir:{{coverage_dir}}' \
      '-reporttypes:Html;MarkdownSummaryGithub;Cobertura;TextSummary' '-assemblyfilters:-*Tests' '-filefilters:-*.g.cs;-*/obj/*'
    cat {{coverage_dir}}/Summary.txt
    rate="$(grep -m1 -o 'line-rate="[0-9.]*"' {{coverage_dir}}/Cobertura.xml | grep -o '[0-9.]*')"; \
    pct="$(awk -v r="$rate" 'BEGIN { printf "%.1f", r * 100 }')"; \
    if awk -v p="$pct" -v m={{coverage_min}} 'BEGIN { exit !(p < m) }'; then \
      echo "Line coverage $pct% is below the {{coverage_min}}% threshold" >&2; exit 1; \
    fi; echo "Line coverage $pct% meets the {{coverage_min}}% threshold"

# Mutation testing with Stryker.NET (slow)
[group('test')]
mutate: setup
    for t in {{mutation_targets}}; do \
      dir="${t%%:*}"; proj="${t##*:}"; name="$(basename "$dir")"; \
      dotnet stryker --config-file {{quote(root / ".config" / "stryker-config.json")}} \
        --test-project "{{root}}/$dir" --project "$proj" --output "{{root}}/{{mutation_dir}}/$name"; \
    done

# Vulnerable NuGet packages, including transitive ones (secrets are scanned by lint)
[group('security')]
audit: _vuln

_vuln: setup
    out="$(dotnet list package --vulnerable --include-transitive)"; echo "$out"; \
    if grep -q 'has the following vulnerable packages' <<<"$out"; then echo 'Vulnerable packages found' >&2; exit 1; fi



# Check commits in a range: Conventional Commits and no AI attribution
[group('git')]
commits base="origin/main" head="HEAD":
    scripts/git/lint-commits.sh {{base}} {{head}}
    scripts/git/check-ai-attribution.sh commits {{base}} {{head}}

# Check the branch name follows <type>/<kebab-slug> (defaults to the current branch)
[group('git')]
branch name="":
    scripts/git/check-branch.sh {{name}}

# Install the local commit-msg hook (header format and AI attribution, no Docker)
[group('git')]
hooks:
    git config core.hooksPath scripts/git/hooks
    @echo "commit-msg hook installed from scripts/git/hooks"

# Build the container image
[group('container')]
image:
    docker build -f {{dockerfile}} -t {{image_tag}} \
      --build-arg REVISION="$(git rev-parse HEAD)" \
      --build-arg CREATED="$(date -u +%Y-%m-%dT%H:%M:%SZ)" .

# Scan the image with trivy; fails on HIGH/CRITICAL
[group('container')]
image-scan:
    docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v trivy-cache:/root/.cache {{trivy_image}} \
      image --exit-code 1 --severity HIGH,CRITICAL --ignore-unfixed --no-progress {{image_tag}}

# Test the git hygiene scripts
[group('git')]
test-git:
    bash scripts/git/test.sh

# Exactly what the pipeline runs, in pipeline order
[group('ci')]
ci:
    just lint
    just test-git
    just build
    just test all
    just cover
    just audit
    just image
    just image-scan
