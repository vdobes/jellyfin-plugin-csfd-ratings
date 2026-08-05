#!/usr/bin/env bash
# Build a deployable plugin folder.
#
#   ./build.sh              -> ./dist/CsfdRatings_<meta.json version>/
#   ./build.sh --zip        -> also creates a zip with an MD5
#
set -euo pipefail

cd "$(dirname "$0")"

VERSION="$(python3 -c "import json; print(json.load(open('meta.json', encoding='utf-8'))['version'])")"
PROJECT="src/Jellyfin.Plugin.CsfdRatings/Jellyfin.Plugin.CsfdRatings.csproj"
OUT="dist/CsfdRatings_${VERSION}"

command -v dotnet >/dev/null 2>&1 || {
  echo "dotnet SDK not found. Install .NET 9 SDK: https://dotnet.microsoft.com/download" >&2
  exit 1
}

echo "==> Restoring and building"
dotnet build "$PROJECT" -c Release -p:Version="$VERSION"

rm -rf "$OUT"
mkdir -p "$OUT"

# Only our own assembly ships. Jellyfin.* are supplied by the host at runtime,
# which is why the csproj marks them ExcludeAssets=runtime.
cp "src/Jellyfin.Plugin.CsfdRatings/bin/Release/net9.0/Jellyfin.Plugin.CsfdRatings.dll" "$OUT/"
cp meta.json "$OUT/"

echo "==> Built $OUT"
ls -la "$OUT"

if [[ "${1:-}" == "--zip" ]]; then
  ZIP="dist/csfd-ratings_${VERSION}.zip"
  rm -f "$ZIP"
  (cd "$OUT" && zip -qr "../$(basename "$ZIP")" .)
  if command -v md5sum >/dev/null 2>&1; then
    md5sum "$ZIP" | awk '{print $1}' > "$ZIP.md5"
  else
    md5 -q "$ZIP" > "$ZIP.md5"
  fi
  echo "==> Packaged $ZIP (md5: $(cat "$ZIP.md5"))"
fi

cat <<'EOF'

Instalace:
  1. Zastav Jellyfin.
  2. Zkopíruj obsah dist/CsfdRatings_<verze>/ do plugins složky serveru, např.
       /var/lib/jellyfin/plugins/CsfdRatings_<verze>/
     nebo v Dockeru do /config/plugins/CsfdRatings_<verze>/
  3. chown -R jellyfin:jellyfin <cílová složka>   (u bare-metal instalace)
  4. Spusť Jellyfin a otevři Ovládací panel -> Pluginy.

EOF
