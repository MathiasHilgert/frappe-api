#!/usr/bin/env bash
# Installs a pinned oasdiff release into <dir>, verified against the release's checksums.
# Usage: install-oasdiff.sh <version> <dir>; needs GH_TOKEN for gh.
set -euo pipefail

version=$1
dir=$2
archive="oasdiff_${version}_linux_amd64.tar.gz"
work=$(mktemp -d)

gh release download "v$version" --repo oasdiff/oasdiff --pattern "$archive" --pattern checksums.txt --dir "$work"
(cd "$work" && sha256sum --check --ignore-missing checksums.txt)
mkdir -p "$dir"
tar -xzf "$work/$archive" -C "$dir" oasdiff
"$dir/oasdiff" --version
