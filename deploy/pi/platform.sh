#!/usr/bin/env bash
# Platform API'sini Pi ortamına bağlı başlatır (Mac'te, http://0.0.0.0:8090). Gerçek SSH ajanı (Pi, 2222),
# okullara gerçek https (Cloudflare Tunnel, sistem güven deposu), sahte DNS (wildcard kayıt tünele bir
# kez açıldı), yedek için Pi'deki S3. Veritabanı Mac'te ayrı bir konteyner (abdera-platform-db).
# Panel: (cd platform/web && npm run dev) -> http://localhost:3100
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/.out"
API="$HERE/../../platform/api/Abdera.Platform.Api"
# shellcheck source=env.sh
. "$HERE/env.sh"
[ -f "$OUT/platform_key" ] || { echo "önce deploy/pi/up.sh" >&2; exit 1; }
# shellcheck disable=SC1091
. "$OUT/s3.env"

[ -f "$OUT/operator.env" ] || printf 'OPERATOR_EMAIL=ops@abdera.local\nOPERATOR_PASSWORD=%s\n' "$(openssl rand -base64 18 | tr -d '/+=')" > "$OUT/operator.env"
[ -f "$OUT/db.env" ] || printf 'PLATFORM_DB_PASSWORD=%s\n' "$(openssl rand -hex 16)" > "$OUT/db.env"
# shellcheck disable=SC1091
. "$OUT/operator.env"; . "$OUT/db.env"

if [ "$(docker inspect -f '{{.State.Running}}' abdera-platform-db 2>/dev/null)" != "true" ]; then
  docker rm -f abdera-platform-db >/dev/null 2>&1 || true
  docker run -d --name abdera-platform-db --restart unless-stopped -p "127.0.0.1:$PLATFORM_DB_PORT:5432" \
    -e POSTGRES_DB=abdera_platform -e POSTGRES_USER=abdera -e POSTGRES_PASSWORD="$PLATFORM_DB_PASSWORD" \
    -v abdera_platform_db:/var/lib/postgresql/data docker.io/library/postgres:16-alpine >/dev/null
  for _ in $(seq 1 30); do docker exec abdera-platform-db pg_isready -U abdera >/dev/null 2>&1 && break; sleep 1; done
fi

export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:8090
export ConnectionStrings__Platform="Host=localhost;Port=$PLATFORM_DB_PORT;Database=abdera_platform;Username=abdera;Password=$PLATFORM_DB_PASSWORD"
export Platform__PublicUrl="http://$MAC_IP:8090" Platform__KeysDirectory="$OUT/platform-keys"
export Platform__BootstrapEmail="$OPERATOR_EMAIL" Platform__BootstrapPassword="$OPERATOR_PASSWORD"
export HostAgent__Provider=Ssh HostAgent__PrivateKeyPath="$OUT/platform_key"
export Dns__Provider=Fake Dns__BaseDomain="$SCHOOLS_DOMAIN"
export SchoolClient__Provider=Http
export Certificates__Provider=Fake
export Backup__R2__AccountId=local Backup__R2__Bucket=abdera-backups Backup__R2__Provider=Other
export Backup__R2__Endpoint="http://$PI_IP:9000"
export Backup__R2__AccessKeyId="$S3_ACCESS_KEY" Backup__R2__SecretAccessKey="$S3_SECRET_KEY"
# Yalnızca test ortamı: platform girişinde iki adımlı doğrulama istenmez (production reddeder).
export Platform__RequireTotp=false
export Provisioning__SmokeAttempts=30 Provisioning__SmokeDelaySeconds=5 Rollouts__CanarySoakMinutes=1
cd "$API" && exec dotnet run --no-launch-profile
