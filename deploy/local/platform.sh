#!/usr/bin/env bash
# Platform API'sini yerel test ortamına bağlı başlatır (Mac'te, http://localhost:8090). Gerçek SSH ajanı
# (sunucu konteyneri, 2222), gerçek okul istemcisi (https://<okul>.okul.localhost:8443, Caddy'nin yerel kök
# sertifikası), sahte DNS (*.localhost zaten 127.0.0.1'e çözülür), yedek için yerel S3.
# Veritabanı: PLATFORM_DB ya da .out/platform-db.env (ör. yerel Postgres'te abdera_platform_local).
# Panel: (cd platform/web && npm run dev) -> http://localhost:3100
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
OUT="$HERE/.out"
API="$HERE/../../platform/api/Abdera.Platform.Api"
[ -f "$OUT/platform_key" ] || { echo "önce deploy/local/up.sh" >&2; exit 1; }
# shellcheck disable=SC1091
. "$OUT/s3.env"

# İlk operatör: rastgele parola, yalnızca bu dosyada (repoya girmez).
[ -f "$OUT/operator.env" ] || printf 'OPERATOR_EMAIL=ops@abdera.local\nOPERATOR_PASSWORD=%s\n' "$(openssl rand -base64 18 | tr -d '/+=')" > "$OUT/operator.env"
# shellcheck disable=SC1091
. "$OUT/operator.env"

[ -f "$OUT/caddy-root.crt" ] || { echo "Caddy kök sertifikası yok; deploy/local/up.sh" >&2; exit 1; }

# Veritabanı bağlantısı: PLATFORM_DB ortamdan ya da .out/platform-db.env'den.
# shellcheck disable=SC1091
[ -n "${PLATFORM_DB:-}" ] || { [ -f "$OUT/platform-db.env" ] && . "$OUT/platform-db.env"; }

export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:8090
export ConnectionStrings__Platform="${PLATFORM_DB:?PLATFORM_DB tanımlı değil: Host=localhost;Port=5432;Database=abdera_platform_local;Username=...;Password=...}"
export Platform__PublicUrl=http://host.containers.internal:8090 Platform__KeysDirectory="$OUT/platform-keys"
export Platform__BootstrapEmail="$OPERATOR_EMAIL" Platform__BootstrapPassword="$OPERATOR_PASSWORD"
export HostAgent__Provider=Ssh HostAgent__PrivateKeyPath="$OUT/platform_key"
# Okul alt alanları <kısaltma>.okul.localhost; başka bir *.localhost adresi okulun "kendi alan adı" denemesi için boş kalır.
export Dns__Provider=Fake Dns__BaseDomain=okul.localhost
export SchoolClient__Provider=Http SchoolClient__HttpsPort=8443 SchoolClient__TrustedRootCaPath="$OUT/caddy-root.crt"
export Certificates__Provider=Fake
export Backup__R2__AccountId=local Backup__R2__Bucket=abdera-backups Backup__R2__Provider=Other
export Backup__R2__Endpoint=http://host.containers.internal:9000
export Backup__R2__AccessKeyId="$S3_ACCESS_KEY" Backup__R2__SecretAccessKey="$S3_SECRET_KEY"
# Yalnızca yerel ortam: platform girişinde iki adımlı doğrulama istenmez (production reddeder).
export Platform__RequireTotp=false
export Provisioning__SmokeAttempts=20 Provisioning__SmokeDelaySeconds=3 Rollouts__CanarySoakMinutes=1
cd "$API" && exec dotnet run --no-launch-profile
