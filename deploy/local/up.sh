#!/usr/bin/env bash
# Abdera çok kiracılı yapının YEREL test ortamı (macOS + Podman). Gerçek okul imajları, gerçek ajan,
# gerçek SSH, gerçek Caddy; Cloudflare yerine sahte DNS (*.localhost), R2 yerine yerel S3 (rclone serve s3).
#
#   deploy/local/up.sh            altyapıyı kurar (depo, yerel S3, Caddy, sunucu konteyneri)
#   deploy/local/build-release.sh 2026.10.16.1   okul imajlarını derleyip depoya iter
#   deploy/local/platform.sh      platform API'sini bu ortama bağlı başlatır (Mac'te, 8090)
#   deploy/local/down.sh          her şeyi siler (okullar dahil)
#
# Okullar https://<kısaltma>.okul.localhost:8443 adresinde açılır (Caddy'nin yerel sertifikası).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DEPLOY="$(cd "$HERE/.." && pwd)"
OUT="$HERE/.out"
ROOT=/var/tmp/abdera-local/srv          # VM içinde; sunucu konteyneri ve Caddy aynı yolu görür
NOLABEL="--security-opt label=disable"  # Podman VM'inde SELinux: bind mount etiketsiz okunmaz
mkdir -p "$OUT"

running() { [ "$(docker inspect -f '{{.State.Running}}' "$1" 2>/dev/null)" = "true" ]; }

# 1. İmaj deposu (Mac'te 5000 AirPlay'de, bu yüzden 5050)
running abdera-local-registry || { docker rm -f abdera-local-registry >/dev/null 2>&1 || true
  docker run -d --name abdera-local-registry -p 5050:5000 --restart unless-stopped docker.io/library/registry:2.8.3 >/dev/null; }

# 2. Yerel S3 (R2 yerine): rclone serve s3, okul şablonunun zaten kullandığı imaj. Bucket = dizin.
[ -f "$OUT/s3.env" ] || printf 'S3_ACCESS_KEY=abdera-local\nS3_SECRET_KEY=%s\n' "$(openssl rand -hex 16)" > "$OUT/s3.env"
# shellcheck disable=SC1091
. "$OUT/s3.env"
running abdera-local-s3 || { docker rm -f abdera-local-s3 >/dev/null 2>&1 || true
  podman machine ssh "mkdir -p /var/tmp/abdera-local/s3/abdera-backups" >/dev/null
  # shellcheck disable=SC2086
  docker run -d --name abdera-local-s3 --restart unless-stopped $NOLABEL -p 9000:9000 \
    -v /var/tmp/abdera-local/s3:/data docker.io/rclone/rclone:1.75.1 \
    serve s3 /data --addr :9000 --auth-key "$S3_ACCESS_KEY,$S3_SECRET_KEY" >/dev/null; }

# 3. Sunucu dizini ve paylaşılan Caddy (setup-host.sh'in yerel karşılığı; portlar 8088/8443)
podman machine ssh "mkdir -p $ROOT/caddy/sites $ROOT/caddy/suspended" >/dev/null
podman machine ssh "cat > $ROOT/caddy/Caddyfile" < "$DEPLOY/host/caddy/Caddyfile"
podman machine ssh "cat > $ROOT/caddy/suspended/index.html" < "$DEPLOY/host/caddy/suspended/index.html"
running abdera-caddy || { docker rm -f abdera-caddy >/dev/null 2>&1 || true
  # shellcheck disable=SC2086
  docker run -d --name abdera-caddy --restart unless-stopped $NOLABEL -p 8088:80 -p 8443:443 \
    -e ACME_EMAIL=ops@abdera.local -v "$ROOT/caddy:/etc/caddy" \
    -v abdera_caddy_data:/data -v abdera_caddy_config:/config docker.io/library/caddy:2-alpine >/dev/null; }

# Caddy'nin yerel sertifika otoritesi ilk .localhost sitesiyle oluşur; platform kök sertifikayı açılışta
# okur (platform.sh). Bu blok yalnızca yerel ortamda var ve ajanın yönettiği okul dosyalarına dokunmaz.
podman machine ssh "cat > $ROOT/caddy/sites/_local-ca.caddy" <<'CADDY'
ca.localhost {
	respond "abdera yerel CA"
}
CADDY
docker exec abdera-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1 || true
for _ in $(seq 1 15); do docker exec abdera-caddy test -f /data/caddy/pki/authorities/local/root.crt && break; sleep 1; done
docker cp abdera-caddy:/data/caddy/pki/authorities/local/root.crt "$OUT/caddy-root.crt"

# 4. Platformun SSH anahtarı ve sunucu konteyneri
[ -f "$OUT/platform_key" ] || ssh-keygen -q -t ed25519 -N "" -C abdera-platform-local -f "$OUT/platform_key"
podman build -q -t abdera-local-host "$HERE/host" >/dev/null
running abdera-local-host || { docker rm -f abdera-local-host >/dev/null 2>&1 || true
  # shellcheck disable=SC2086
  docker run -d --name abdera-local-host --restart unless-stopped $NOLABEL -p 2222:22 \
    -e ABDERA_ROOT="$ROOT" \
    -v /run/user/"$(podman machine ssh 'id -u' | tr -d '\r')"/podman/podman.sock:/var/run/docker.sock \
    -v "$ROOT:$ROOT" -v "$DEPLOY:/deploy:ro" -v "$OUT/platform_key.pub:/keys/platform_key.pub:ro" \
    abdera-local-host >/dev/null; }
for _ in $(seq 1 20); do ssh-keyscan -p 2222 -t ed25519 localhost >/dev/null 2>&1 && break; sleep 1; done
FINGERPRINT="$(ssh-keyscan -p 2222 -t ed25519 localhost 2>/dev/null | ssh-keygen -lf - | awk '{print $2}')"
echo "$FINGERPRINT" > "$OUT/host_fingerprint"

echo "Ajan: $(ssh -q -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -i "$OUT/platform_key" -p 2222 root@localhost host-info)"
echo "Sunucu parmak izi: $FINGERPRINT"
echo "Hazır. Sırada: deploy/local/build-release.sh <sürüm> ve deploy/local/platform.sh"
