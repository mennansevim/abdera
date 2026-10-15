#!/usr/bin/env bash
# Yeni bir okul sunucusunu hazırlar (docs/multi-tenant/ARCHITECTURE.md §6.1, §6.2, B15/B16).
# Bir kez, root olarak, temiz bir Ubuntu/Debian sunucuda çalıştırılır:
#   sudo ./setup-host.sh "<platform ssh public key>" <acme e-posta>
# Yaptıkları:
#   - Docker (yoksa) ve ajanın ihtiyaçları (flock, openssl, curl, gzip)
#   - /srv/abdera düzeni, paylaşılan Caddy (80/443) ve askı sayfası
#   - ajanı /usr/local/bin/abdera-host, compose şablonunu /usr/local/share/abdera'ya kopyalar
#   - platform anahtarını YALNIZCA ajanı çalıştırabilecek şekilde authorized_keys'e ekler
set -euo pipefail

PLATFORM_KEY="${1:?platform SSH public key gerekli}"
ACME_EMAIL="${2:?ACME e-posta gerekli}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
ROOT=/srv/abdera

[ "$(id -u)" -eq 0 ] || { echo "root olarak çalıştır" >&2; exit 1; }
[[ "$PLATFORM_KEY" =~ ^ssh-(ed25519|rsa)\ [A-Za-z0-9+/=]+( [^\"]*)?$ ]] || { echo "geçersiz anahtar" >&2; exit 1; }

if ! command -v docker >/dev/null; then
  apt-get update -qq && apt-get install -y -qq ca-certificates curl
  curl -fsSL https://get.docker.com | sh
fi
apt-get install -y -qq util-linux openssl curl gzip >/dev/null

install -d -m 755 /usr/local/share/abdera
install -m 644 "$REPO/deploy/instance/compose.yml" /usr/local/share/abdera/compose.yml
install -m 755 "$HERE/abdera-host" /usr/local/bin/abdera-host

install -d -m 755 "$ROOT" "$ROOT/caddy" "$ROOT/caddy/sites" "$ROOT/caddy/suspended"
install -m 644 "$HERE/caddy/Caddyfile" "$ROOT/caddy/Caddyfile"
install -m 644 "$HERE/caddy/suspended/index.html" "$ROOT/caddy/suspended/index.html"

# Paylaşılan Caddy: tek konteyner, her okulun edge ağına ajan bağlar. Varsayılan bridge'de
# başlar; okul ağları ayrı olduğu için okullar birbirini göremez.
if ! docker inspect abdera-caddy >/dev/null 2>&1; then
  docker run -d --name abdera-caddy --restart unless-stopped \
    -p 80:80 -p 443:443 -p 443:443/udp \
    -e ACME_EMAIL="$ACME_EMAIL" \
    -v "$ROOT/caddy:/etc/caddy" \
    -v abdera_caddy_data:/data -v abdera_caddy_config:/config \
    caddy:2-alpine >/dev/null
fi

# Platform yalnızca ajanı çalıştırabilir; port yönlendirme, kabuk, X11 yok.
AUTH=/root/.ssh/authorized_keys
install -d -m 700 /root/.ssh
touch "$AUTH" && chmod 600 "$AUTH"
LINE="command=\"/usr/local/bin/abdera-host\",restrict $PLATFORM_KEY"
grep -qxF "$LINE" "$AUTH" || echo "$LINE" >> "$AUTH"

echo "Hazır: $(/usr/local/bin/abdera-host host-info)"
