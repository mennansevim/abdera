#!/usr/bin/env bash
# Yerel test ortamını tamamen siler: okullar (verileriyle), sunucu konteyneri, Caddy, yerel S3, depo.
# Yalnızca bu ortamın adlandırdığı kaynaklara dokunur (abdera-local-*, abdera-caddy, okul projeleri).
# Platform veritabanı ve .out/ (anahtarlar) kalır; .out'u da silmek için: rm -rf deploy/local/.out
set -euo pipefail
ROOT=/var/tmp/abdera-local/srv
for slug in $(podman machine ssh "ls $ROOT 2>/dev/null" | tr -d '\r' | grep -v '^caddy$' || true); do
  docker compose -p "abdera-$slug" down --volumes --remove-orphans >/dev/null 2>&1 || true
  docker network rm "abdera-edge-$slug" >/dev/null 2>&1 || true
  echo "okul silindi: $slug"
done
docker rm -f abdera-local-host abdera-caddy abdera-local-s3 abdera-local-registry >/dev/null 2>&1 || true
docker volume rm abdera_caddy_data abdera_caddy_config >/dev/null 2>&1 || true
podman machine ssh "rm -rf /var/tmp/abdera-local" >/dev/null
echo "Yerel ortam silindi."
