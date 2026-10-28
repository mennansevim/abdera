#!/usr/bin/env bash
# Pi test ortamını siler: okullar (verileriyle), sunucu konteyneri, Caddy, S3, depo, tünel konteyneri.
# Yalnızca bu ortamın kaynaklarına dokunur (abdera-pi-*, abdera-caddy, abdera-<okul> projeleri, $PI_BASE).
# Cloudflare'deki tünel ve DNS kaydı kalır; silmek için Pi'de:
#   Cloudflare panelinden *.<alan adı> kaydını sil, sonra Pi'de: cloudflared tunnel delete abdera-pi
# Mac'teki platform veritabanı (abdera-platform-db) ve .out/ kalır.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=env.sh
. "$HERE/env.sh"
ssh "$PI_HOST" bash -s -- "$PI_BASE" "$PI_ROOT" <<'REMOTE'
set -euo pipefail
BASE="$1" ROOT="$2"
for slug in $(ls "$ROOT" 2>/dev/null | grep -v '^caddy$' || true); do
  docker compose -p "abdera-$slug" down --volumes --remove-orphans >/dev/null 2>&1 || true
  docker network rm "abdera-edge-$slug" >/dev/null 2>&1 || true
  echo "okul silindi: $slug"
done
docker rm -f abdera-pi-tunnel abdera-pi-host abdera-caddy abdera-pi-s3 abdera-pi-registry >/dev/null 2>&1 || true
docker volume rm abdera_caddy_data abdera_caddy_config abdera_pi_registry >/dev/null 2>&1 || true
# Okul dizinleri root'a ait (sunucu konteyneri yazdı): bir konteynerle silinir, sudo gerekmez.
docker run --rm -v "$BASE:/x" alpine:3.20 rm -rf /x/srv /x/s3 >/dev/null
rm -rf "$BASE"
REMOTE
echo "Pi ortamı silindi."
