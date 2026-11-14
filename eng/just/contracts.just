set working-directory := "../.."

# Regenerate docs/api/ (the HTTP and event contracts) from the code; commit the result (the HTTP one needs Docker)
update:
    FRAPPE_UPDATE_CONTRACTS=true timeout 120 dotnet test --project tests/Frappe.Architecture.Tests -- --treenode-filter "/*/*/IntegrationEventContractTests/*"
    FRAPPE_UPDATE_CONTRACTS=true timeout 400 dotnet test --project tests/Modules/Geo/Frappe.Geo.IntegrationTests -- --treenode-filter "/*/*/OpenApiContractTests/*"
    git status --short docs/api
