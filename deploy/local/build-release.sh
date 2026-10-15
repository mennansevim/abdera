#!/usr/bin/env bash
# Yerel test ortamı: okul imajlarını derleyip yerel depoya (localhost:5050) iter ve platforma
# kaydedilecek sürüm kaydını (release.json) üretir. images.yml'in yerel karşılığı.
# Kullanım: deploy/local/build-release.sh 2026.10.16.1
set -euo pipefail
VERSION="${1:?sürüm gerekli (YYYY.MM.DD.N)}"
REGISTRY="${LOCAL_REGISTRY:-localhost:5050}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="${LOCAL_OUT:-$ROOT/deploy/local/.out}"
mkdir -p "$OUT"

digest_of() { # depodaki manifest digest'i (ajan imajı bununla çeker)
  curl -fsS -I -H 'Accept: application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json' \
    "http://$REGISTRY/v2/$1/manifests/$VERSION" | awk -F': ' 'tolower($1)=="docker-content-digest"{print $2}' | tr -d '\r'
}

echo "API imajı derleniyor..."
podman build -q --build-arg APP_VERSION="$VERSION" -t "$REGISTRY/abdera-api:$VERSION" "$ROOT/backend" >/dev/null
podman push -q --tls-verify=false "$REGISTRY/abdera-api:$VERSION"
echo "Web imajı derleniyor..."
podman build -q -t "$REGISTRY/abdera-web:$VERSION" "$ROOT/frontend" >/dev/null
podman push -q --tls-verify=false "$REGISTRY/abdera-web:$VERSION"

echo "Modül kataloğu..."
dotnet build "$ROOT/backend/src/Abdera.Api" -c Release -o "$OUT/api-build" -v quiet >/dev/null
dotnet "$OUT/api-build/Abdera.Api.dll" --print-module-catalog > "$OUT/module-catalog.json"

python3 - "$VERSION" "$REGISTRY/abdera-api@$(digest_of abdera-api)" "$REGISTRY/abdera-web@$(digest_of abdera-web)" \
  "$(git -C "$ROOT" rev-parse --short HEAD)" "$OUT/module-catalog.json" > "$OUT/release-$VERSION.json" <<'PY'
import json, sys
version, api, web, revision, catalog = sys.argv[1:]
print(json.dumps({"version": version, "apiImage": api, "webImage": web, "revision": revision,
                  "modules": json.load(open(catalog)), "notes": "yerel test sürümü"}, ensure_ascii=False))
PY
echo "Sürüm kaydı: $OUT/release-$VERSION.json"
