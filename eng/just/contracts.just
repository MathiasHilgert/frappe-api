set working-directory := "../.."

# Regenerate contracts/ (the HTTP and event contracts) from the code; commit the result
update:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ ! -d tests/Hosts/Frappe.Api.IntegrationTests ]; then
        echo "skip: no host publishes a contract yet"
        exit 0
    fi
    FRAPPE_UPDATE_CONTRACTS=true timeout 120 dotnet test --project tests/Frappe.Architecture.Tests -- --treenode-filter "/*/*/IntegrationEventContractTests/*"
    FRAPPE_UPDATE_CONTRACTS=true timeout 400 dotnet test --project tests/Hosts/Frappe.Api.IntegrationTests -- --treenode-filter "/*/*/OpenApiContractTests/*"
    git status --short contracts
