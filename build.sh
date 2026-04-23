#!/usr/bin/env bash
# JellyFusion local build & package script (Linux/macOS/WSL)
# Usage: ./build.sh [version]
# Produces releases/JellyFusion-v<version>.zip and updates manifest.json.

set -euo pipefail

VERSION="${1:-1.0.0}"
GUID="a1b2c3d4-e5f6-7890-abcd-ef1234567890"
OWNER="KOOL4"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$ROOT/src/JellyFusion/JellyFusion.csproj"
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
    -p:AssemblyVersion="${VERSION}.0" \
    -p:FileVersion="${VERSION}.0"

echo "==> Sanity check: publish folder should contain only JellyFusion.dll"
ls -la "$PUB"
EXTRA=$(find "$PUB" -maxdepth 1 -name "*.dll" ! -name "JellyFusion.dll" | wc -l)
if [ "$EXTRA" -gt 0 ]; then
    echo "⚠️  Warning: extra DLLs detected in publish output — check <PrivateAssets>all</PrivateAssets>:"
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
    "version": "${VERSION}.0"
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
import json, sys
from pathlib import Path
p = Path("$ROOT/manifest.json")
m = json.loads(p.read_text())
found = False
for v in m[0]["versions"]:
    if v["version"] == "${VERSION}.0":
        v["checksum"]  = "${MD5}"
        v["timestamp"] = "${TIMESTAMP}".replace("T", " ").replace("Z", "")
        found = True
        break
if not found:
    sys.stderr.write("Warning: no manifest entry for ${VERSION}.0\n")
p.write_text(json.dumps(m, indent=2) + "\n")
EOF

echo ""
echo "==> DONE"
echo "    ZIP:      $ZIP_OUT"
echo "    Size:     $(stat -c%s "$ZIP_OUT" 2>/dev/null || stat -f%z "$ZIP_OUT") bytes"
echo "    MD5:      $MD5"
echo "    Manifest: $ROOT/manifest.json"
