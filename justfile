# Every recipe is plain dotnet or docker commands, so each one also works by hand without Just.

set shell := ["bash", "-euo", "pipefail", "-c"]

results := "artifacts/test-results"

[private]
default:
    @{{ just_executable() }} --justfile {{ justfile() }} --list --unsorted

# --- build ---

[group('build')]
[doc('Restore tools and packages exactly as locked')]
restore:
    dotnet tool restore
    dotnet restore --locked-mode

[group('build')]
[doc('Build the solution')]
build: restore
    dotnet build --no-restore

[group('build')]
[doc('Remove build output and test results')]
clean:
    dotnet clean
    rm -rf {{ results }}

# --- quality ---

[group('quality')]
[doc('Format the code with CSharpier')]
format:
    dotnet tool restore
    dotnet csharpier format .

[group('quality')]
[doc('Verify formatting without changing files')]
format-check:
    dotnet tool restore
    dotnet csharpier check .

[group('quality')]
[doc('Rebuild everything so every analyzer runs; warnings are errors')]
lint: restore
    dotnet build --no-restore --no-incremental

[group('quality')]
[doc('List vulnerable packages; fail on a high or critical advisory')]
audit: restore
    #!/usr/bin/env bash
    set -euo pipefail
    dotnet package list --vulnerable --include-transitive --no-restore
    # Capture first: piping into grep -q could end the pipe early and let the gate pass.
    report="$(dotnet package list --vulnerable --include-transitive --no-restore --format json)"
    if grep -qE '"severity": *"(High|Critical)"' <<< "$report"; then
      echo "error: a package has a high or critical advisory; see the list above." >&2
      exit 1
    fi

# --- test ---

[group('test')]
[doc('Run tests: all | unit | integration | architecture')]
[arg('kind', pattern='all|unit|integration|architecture')]
test kind="all": build
    #!/usr/bin/env bash
    set -euo pipefail
    # The kind comes from the project name, so a new test project needs no change here.
    unit=(-name '*.UnitTests.csproj')
    integration=(-name '*.IntegrationTests.csproj' -o -name '*.Host.Tests.csproj')
    architecture=(-name '*.ArchitectureTests.csproj')
    every=("${unit[@]}" -o "${integration[@]}" -o "${architecture[@]}")
    unnamed=$(find tests -name '*.csproj' -not \( "${every[@]}" \))
    if [[ -n "$unnamed" ]]; then
      echo "error: these test projects match no kind; end the name in .UnitTests," >&2
      echo ".IntegrationTests or .ArchitectureTests:" >&2
      echo "$unnamed" >&2
      exit 1
    fi
    case "{{ kind }}" in
      unit) selected=("${unit[@]}") ;;
      integration) selected=("${integration[@]}") ;;
      architecture) selected=("${architecture[@]}") ;;
      all) selected=("${every[@]}") ;;
    esac
    projects=$(find tests -name '*.csproj' \( "${selected[@]}" \) | sort)
    if [[ -z "$projects" ]]; then
      echo "No {{ kind }} test projects yet; nothing to run."
      exit 0
    fi
    while read -r project; do
      name=$(basename "$project" .csproj)
      dotnet test --project "$project" --no-build \
        --results-directory "{{ results }}/{{ kind }}" \
        --report-xunit-trx --report-xunit-trx-filename "$name.trx" \
        --report-xunit-markdown --report-xunit-markdown-filename "$name.md"
    done <<< "$projects"

# --- run ---

[group('run')]
[doc('Run the host in one role: all | api | worker | scheduler')]
[arg('role', pattern='all|api|worker|scheduler')]
run role="all": restore
    dotnet run --project src/Frappe.Host --no-restore -- --Platform:Role {{ role }}

# --- env ---

[group('env')]
[doc('Build the container image')]
image:
    docker build --build-arg REVISION="$(git rev-parse HEAD)" --tag frappe-api:local .

[group('env')]
[doc('Start PostgreSQL and the API in containers; wait until they are up')]
up:
    REVISION="$(git rev-parse HEAD)" docker compose up --detach --build --wait

[group('env')]
[doc('Stop and remove the containers; data stays in its volume')]
down:
    docker compose down

[group('env')]
[doc('Follow the logs of every service, or of one: postgres | api')]
logs service="":
    docker compose logs --follow {{ service }}

[group('env')]
[doc('List the services and their health')]
ps:
    docker compose ps

# --- ci ---

[group('ci')]
[doc('Run every check CI runs, in the same order')]
check: format-check lint (test "all") audit
