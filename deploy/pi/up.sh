#!/usr/bin/env bash
# Raspberry Pi'yi okul sunucusu olarak hazırlar (Mac'ten çalıştırılır, Pi'ye SSH ile bağlanır).
# deploy/local/ ile aynı düzen; farkları: Docker'ın kendisi (Podman değil), arm64, okullar internete
# Cloudflare Tunnel ile açılır. Pi'de başka projeler çalışıyor: sudo kullanılmaz, 80/443'e, nginx'e ve
# var olan tünele dokunulmaz; her kaynak abdera-pi-* / abdera-caddy adını taşır ve $PI_BASE altındadır.
#
#   deploy/pi/up.sh               altyapı (depo, S3, Caddy, sunucu konteyneri, tünel)
#   deploy/pi/build-release.sh 2026.10.28.1
#   deploy/pi/platform.sh         platform API'si (Mac, 8090)
#   deploy/pi/down.sh             her şeyi siler (okullar dahil)
#
# Tünelin DNS kaydı (*.<alan adı> → tünel) bir kez elle açılır: deploy/pi/tunnel-dns.sh
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DEPLOY="$(cd "$HERE/.." && pwd)"
OUT="$HERE/.out"
# shellcheck source=env.sh
. "$HERE/env.sh"
mkdir -p "$OUT"

[ -f "$OUT/s3.env" ] || printf 'S3_ACCESS_KEY=abdera-pi\nS3_SECRET_KEY=%s\n' "$(openssl rand -hex 16)" > "$OUT/s3.env"
[ -f "$OUT/platform_key" ] || ssh-keygen -q -t ed25519 -N "" -C abdera-platform-pi -f "$OUT/platform_key"
# shellcheck disable=SC1091
. "$OUT/s3.env"

# Ajan, okul şablonu ve sunucu konteyneri Pi'ye kopyalanır (sunucu konteyneri bunları her açılışta kurar).
ssh "$PI_HOST" "mkdir -p $PI_BASE/deploy/local $PI_ROOT/caddy/sites $PI_ROOT/caddy/suspended $PI_BASE/s3/abdera-backups $PI_BASE/keys $PI_BASE/tunnel"
rsync -a --delete "$DEPLOY/host" "$DEPLOY/instance" "$PI_HOST:$PI_BASE/deploy/"
rsync -a --delete "$DEPLOY/local/host" "$PI_HOST:$PI_BASE/deploy/local/"
scp -q "$OUT/platform_key.pub" "$PI_HOST:$PI_BASE/keys/platform_key.pub"

# Caddy: okul sunucusundaki Caddyfile'ın aynısı + local_certs. Dışarıya karşı sertifikayı Cloudflare
# verir; tünel Caddy'ye yerel sertifikayla bağlanır (doğrulama kapalı, SNI = okulun adresi).
sed 's/^{$/{\n\tlocal_certs/' "$DEPLOY/host/caddy/Caddyfile" | ssh "$PI_HOST" "cat > $PI_ROOT/caddy/Caddyfile"
ssh "$PI_HOST" "cp $PI_BASE/deploy/host/caddy/suspended/index.html $PI_ROOT/caddy/suspended/index.html"

ssh "$PI_HOST" bash -s -- "$PI_BASE" "$PI_ROOT" "$S3_ACCESS_KEY" "$S3_SECRET_KEY" <<'REMOTE'
set -euo pipefail
BASE="$1" ROOT="$2" S3_ACCESS_KEY="$3" S3_SECRET_KEY="$4"
running() { [ "$(docker inspect -f '{{.State.Running}}' "$1" 2>/dev/null)" = "true" ]; }

running abdera-pi-registry || { docker rm -f abdera-pi-registry >/dev/null 2>&1 || true
  docker run -d --name abdera-pi-registry --restart unless-stopped -p 5050:5000 \
    -v abdera_pi_registry:/var/lib/registry registry:2.8.3 >/dev/null; }

running abdera-pi-s3 || { docker rm -f abdera-pi-s3 >/dev/null 2>&1 || true
  docker run -d --name abdera-pi-s3 --restart unless-stopped -p 9000:9000 -v "$BASE/s3:/data" \
    rclone/rclone:1.75.1 serve s3 /data --addr :9000 --auth-key "$S3_ACCESS_KEY,$S3_SECRET_KEY" >/dev/null; }

# Yalnızca Pi'nin kendisine açık: dışarıdan erişim tünel üzerinden.
running abdera-caddy || { docker rm -f abdera-caddy >/dev/null 2>&1 || true
  docker run -d --name abdera-caddy --restart unless-stopped -p 127.0.0.1:8443:443 \
    -e ACME_EMAIL=ops@abdera.local -v "$ROOT/caddy:/etc/caddy" \
    -v abdera_caddy_data:/data -v abdera_caddy_config:/config caddy:2-alpine >/dev/null; }
docker exec abdera-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1 || true

docker build -q -t abdera-pi-host "$BASE/deploy/local/host" >/dev/null
running abdera-pi-host || { docker rm -f abdera-pi-host >/dev/null 2>&1 || true
  docker run -d --name abdera-pi-host --restart unless-stopped -p 2222:22 -e ABDERA_ROOT="$ROOT" \
    -v /var/run/docker.sock:/var/run/docker.sock -v "$ROOT:$ROOT" -v "$BASE/deploy:/deploy:ro" \
    -v "$BASE/keys/platform_key.pub:/keys/platform_key.pub:ro" abdera-pi-host >/dev/null; }

# Tünel: kendi kimliği ve ayarıyla ayrı bir bağlayıcı (var olan dify-tunnel servisine dokunulmaz).
if [ -f "$BASE/tunnel/config.yml" ]; then
  running abdera-pi-tunnel || { docker rm -f abdera-pi-tunnel >/dev/null 2>&1 || true
    docker run -d --name abdera-pi-tunnel --restart unless-stopped --network host --user "$(id -u)" \
      -v "$BASE/tunnel:/etc/cloudflared:ro" cloudflare/cloudflared:2025.10.0 \
      tunnel --no-autoupdate --config /etc/cloudflared/config.yml run >/dev/null; }
else
  echo "Tünel henüz yok: deploy/pi/tunnel-dns.sh" >&2
fi
REMOTE

for _ in $(seq 1 20); do ssh-keyscan -p 2222 -t ed25519 "$PI_IP" >/dev/null 2>&1 && break; sleep 1; done
ssh-keyscan -p 2222 -t ed25519 "$PI_IP" 2>/dev/null | ssh-keygen -lf - | awk '{print $2}' > "$OUT/host_fingerprint"
echo "Ajan: $(ssh -q -o StrictHostKeyChecking=no -o UserKnownHostsFile=/dev/null -i "$OUT/platform_key" -p 2222 "root@$PI_IP" host-info)"
echo "Sunucu parmak izi: $(cat "$OUT/host_fingerprint")"
