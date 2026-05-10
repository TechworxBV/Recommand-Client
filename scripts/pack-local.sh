#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT=$(cd "$(dirname "$0")/.." && pwd)
LOCAL_FEED="${RECOMMAND_LOCAL_FEED:-$REPO_ROOT/local-nuget}"

VERSION_BASE="${1:-0.4.0}"
VERSION_SUFFIX="local.$(date -u +%Y%m%d%H%M%S)"
VERSION="${VERSION_BASE}-${VERSION_SUFFIX}"

mkdir -p "$LOCAL_FEED"

cd "$REPO_ROOT"

# Pack every publishable project at the same version so AspNetCore's
# ProjectReference -> PackageReference rewrite picks up the matching main
# client by version.
PROJECTS=(
    Recommand.Client/Recommand.Client.csproj
    Recommand.Client.AspNetCore/Recommand.Client.AspNetCore.csproj
)
for proj in "${PROJECTS[@]}"; do
    dotnet pack "$proj" \
        --configuration Release \
        --output "$LOCAL_FEED" \
        -p:Version="$VERSION"
done

cat <<EOF

Packed at version $VERSION into $LOCAL_FEED:
    Recommand.Client.${VERSION}.nupkg
    Recommand.Client.AspNetCore.${VERSION}.nupkg

Consume in a project:

    dotnet add package Recommand.Client            --version $VERSION --source $LOCAL_FEED
    dotnet add package Recommand.Client.AspNetCore --version $VERSION --source $LOCAL_FEED

…or, after configuring $LOCAL_FEED as a feed in nuget.config:

    dotnet add package Recommand.Client            --version $VERSION --prerelease
    dotnet add package Recommand.Client.AspNetCore --version $VERSION --prerelease

EOF
