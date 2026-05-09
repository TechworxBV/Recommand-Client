#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT=$(cd "$(dirname "$0")/.." && pwd)
LOCAL_FEED="${RECOMMAND_LOCAL_FEED:-$HOME/local-nuget}"

VERSION_BASE="${1:-0.2.0}"
VERSION_SUFFIX="local.$(date -u +%Y%m%d%H%M%S)"
VERSION="${VERSION_BASE}-${VERSION_SUFFIX}"

mkdir -p "$LOCAL_FEED"

cd "$REPO_ROOT"
dotnet pack Recommand.Client/Recommand.Client.csproj \
    --configuration Release \
    --output "$LOCAL_FEED" \
    -p:Version="$VERSION"

cat <<EOF

Packed:    $LOCAL_FEED/Recommand.Client.${VERSION}.nupkg
Symbols:   $LOCAL_FEED/Recommand.Client.${VERSION}.snupkg

Install in a consumer project:

    dotnet add package Recommand.Client --version $VERSION --source $LOCAL_FEED

…or, after configuring $LOCAL_FEED as a feed (see scripts/pack-local.md):

    dotnet add package Recommand.Client --version $VERSION --prerelease

EOF
