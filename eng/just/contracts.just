set working-directory := "../.."

# Regenerate docs/api/ (the HTTP contract) from the code; commit the result (it needs Docker)
update:
    FRAPPE_UPDATE_CONTRACTS=true timeout 400 dotnet test --project tests/Modules/Geo/Frappe.Geo.IntegrationTests -- --treenode-filter "/*/*/OpenApiContractTests/*"
    git status --short docs/api
