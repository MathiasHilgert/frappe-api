set working-directory := "../.."

# Rewrite every packages.lock.json after changing Directory.Packages.props or the SDK; commit the result
lock:
    dotnet restore Frappe.slnx --force-evaluate

# List packages with known vulnerabilities, direct and transitive (the CI check "No vulnerable dependencies" fails on any)
audit:
    dotnet package list --project Frappe.slnx --vulnerable --include-transitive
