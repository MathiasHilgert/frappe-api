set working-directory := "../.."

# Run the tests of .github/scripts: the title, commit and branch conventions, and which checks a change affects
conventions:
    #!/usr/bin/env bash
    set -euo pipefail
    if ! ls .github/scripts/*.test.mjs > /dev/null 2>&1; then
        echo "skip: no convention scripts yet (.github/scripts)"
        exit 0
    fi
    node --test .github/scripts/*.test.mjs

# Settings (squash = PR title + description; Actions may open the release PR; dependency graph and alerts) and main's ruleset; needs gh as admin
protect:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ ! -f .github/rulesets/main.json ]; then
        echo "skip: no ruleset yet (.github/rulesets/main.json)"
        exit 0
    fi
    gh api --method PATCH "repos/{owner}/{repo}" -f squash_merge_commit_title=PR_TITLE -f squash_merge_commit_message=PR_BODY -F allow_squash_merge=true -F allow_merge_commit=false -F allow_rebase_merge=false -F delete_branch_on_merge=true --silent
    gh api --method PUT "repos/{owner}/{repo}/actions/permissions/workflow" -f default_workflow_permissions=read -F can_approve_pull_request_reviews=true --silent
    gh api --method PUT "repos/{owner}/{repo}/vulnerability-alerts" --silent
    id=$(gh api "repos/{owner}/{repo}/rulesets" --jq '.[] | select(.name == "main") | .id')
    if [ -n "$id" ]; then gh api --method PUT "repos/{owner}/{repo}/rulesets/$id" --input .github/rulesets/main.json --silent; \
    else gh api --method POST "repos/{owner}/{repo}/rulesets" --input .github/rulesets/main.json --silent; fi
    echo "Settings and the ruleset of main are applied."

# Make the repository's labels exactly .github/labels.json: create or update each one, delete any other; needs gh
labels:
    #!/usr/bin/env bash
    set -euo pipefail
    if [ ! -f .github/labels.json ]; then
        echo "skip: no labels yet (.github/labels.json)"
        exit 0
    fi
    jq -c '.[]' .github/labels.json | while read -r label; do
      gh label create "$(jq -r .name <<< "$label")" --color "$(jq -r .color <<< "$label")" --description "$(jq -r .description <<< "$label")" --force
    done
    gh label list --limit 200 --json name --jq '.[].name' | while read -r name; do
      jq -e --arg name "$name" 'any(.[]; .name == $name)' .github/labels.json > /dev/null || gh label delete "$name" --yes
    done
    echo "Labels match .github/labels.json."
