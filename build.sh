#!/usr/bin/env bash
# JellyFusion local build & package script (Linux/macOS/WSL)
# Usage: ./build.sh [version]
# Produces releases/JellyFusion-v<version>.zip and updates manifest.json.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$ROOT/src/JellyFusion/JellyFusion.csproj"
set -euo pipefail

DEFAULT_VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJ" | head -n 1)"
VERSION="${1:-${DEFAULT_VERSION:-3.0.6.1}}"
GUID="b7c8d9e0-f1a2-3b4c-5d6e-7f8090a1b2c3"
OWNER="KOOL4"
REPO="JellyFusionV3"

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$ ]]; then
    echo "Version must look like 3.0.6 or 3.0.6.1"
    exit 1
fi

if [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    MANIFEST_VERSION="${VERSION}.0"
else
    MANIFEST_VERSION="$VERSION"
fi

PUB="$ROOT/publish"
REL="$ROOT/releases"
ZIP_OUT="$REL/JellyFusion-v${VERSION}.zip"
META="$PUB/meta.json"

echo "==> Cleaning previous build"
rm -rf "$PUB"
mkdir -p "$PUB" "$REL"

echo "==> Restoring + publishing ($VERSION)"
dotnet publish "$PROJ" \
    --configuration Release \
    --output "$PUB" \
    -p:Version="$VERSION" \
    -p:AssemblyVersion="${MANIFEST_VERSION}" \
    -p:FileVersion="${MANIFEST_VERSION}"

echo "==> Sanity check: publish folder should contain only JellyFusion.dll"
ls -la "$PUB"
EXTRA=$(find "$PUB" -maxdepth 1 -name "*.dll" ! -name "JellyFusion.dll" | wc -l)
if [ "$EXTRA" -gt 0 ]; then
    echo "Warning: extra DLLs detected in publish output - check <PrivateAssets>all</PrivateAssets>:"
    find "$PUB" -maxdepth 1 -name "*.dll" ! -name "JellyFusion.dll" -exec basename {} \;
fi

echo "==> Writing meta.json"
TIMESTAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
cat > "$META" <<EOF
{
    "category": "General",
    "changelog": "Release ${VERSION}",
    "description": "All-in-one Jellyfin plugin: Netflix-style slider, quality badges (LAT/SUB/NUEVO/KID), studios, themes and notifications.",
    "guid": "${GUID}",
    "imagePath": "",
    "name": "JellyFusion",
    "overview": "Combines Editor's Choice and JellyTag into a single unified plugin with multi-language support.",
    "owner": "${OWNER}",
    "targetAbi": "10.10.0.0",
    "timestamp": "${TIMESTAMP}",
    "version": "${MANIFEST_VERSION}"
}
EOF

echo "==> Creating ZIP"
rm -f "$ZIP_OUT"
(cd "$PUB" && zip "$ZIP_OUT" JellyFusion.dll meta.json)

echo "==> Computing MD5"
MD5=$(md5sum "$ZIP_OUT" | awk '{print $1}')
echo "    MD5: $MD5"

echo "==> Updating manifest.json"
python3 - <<EOF
import json
from pathlib import Path

p = Path("$ROOT/manifest.json")
m = json.loads(p.read_text())
target_version = "${MANIFEST_VERSION}"
source_url = "https://github.com/${OWNER}/${REPO}/releases/download/v${VERSION}/JellyFusion-v${VERSION}.zip"

for v in m[0]["versions"]:
    if v["version"] == target_version:
        v["checksum"] = "${MD5}"
        v["timestamp"] = "${TIMESTAMP}"
        v["changelog"] = f"Release ${VERSION}"
        v["sourceUrl"] = source_url
        break
else:
    m[0]["versions"].insert(0, {
        "version": target_version,
        "changelog": f"Release ${VERSION}",
        "targetAbi": "10.10.0.0",
        "sourceUrl": source_url,
        "checksum": "${MD5}",
        "timestamp": "${TIMESTAMP}",
    })

p.write_text(json.dumps(m, indent=2) + "\n")
EOF

echo ""
echo "==> DONE"
echo "    ZIP:      $ZIP_OUT"
echo "    Size:     $(stat -c%s "$ZIP_OUT" 2>/dev/null || stat -f%z "$ZIP_OUT") bytes"
echo "    MD5:      $MD5"
echo "    Manifest: $ROOT/manifest.json"
