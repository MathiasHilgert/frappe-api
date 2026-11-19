#!/usr/bin/env bash
# Writes the SQL of the migrations a pull request adds, one file per project, into <out dir>. A project gets a file only
# when it has migrations that the base branch does not. Usage: migration-sql.sh <base branch> <out dir>
set -euo pipefail

base_ref=$1
out=$2
mkdir -p "$out"

# Migration ids (yyyyMMddHHmmss_Name) in a Migrations directory, at the base branch or in the working tree.
# A module's first migrations have no directory at the base branch: no ids, not an error.
ids_at_base() { { git ls-tree --name-only "origin/$base_ref" -- "$1/" 2> /dev/null || true; } | xargs -r -n1 basename | { grep -E '^[0-9]{14}_[^.]+\.cs$' || true; } | sed 's/\.cs$//' | sort; }
ids_here() { find "$1" -maxdepth 1 -name '[0-9]*_*.cs' ! -name '*.Designer.cs' -printf '%f\n' | sed 's/\.cs$//' | sort; }

find src -type d -name Migrations -not -path '*/bin/*' -not -path '*/obj/*' | sort | while read -r migrations; do
  project=$(dirname "$migrations")
  while [ ! -f "$project/$(basename "$project").csproj" ] && [ "$project" != src ]; do project=$(dirname "$project"); done

  new=$(comm -13 <(ids_at_base "$migrations") <(ids_here "$migrations"))
  if [ -z "$new" ]; then
    continue
  fi

  from=$(ids_at_base "$migrations" | tail -n1)
  to=$(ids_here "$migrations" | tail -n1)
  name=$(basename "$project")
  echo "$name: $(echo "$new" | wc -l) new migration(s), ${from:-empty database} → $to"
  dotnet ef migrations script "${from:-0}" "$to" --project "$project" --no-build --output "$out/$name.sql"
done
