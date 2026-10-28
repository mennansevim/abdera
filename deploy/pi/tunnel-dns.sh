#!/usr/bin/env bash
# Bir kez: Pi'de ayrı bir Cloudflare tüneli açar ve ayarını yazar. DNS kaydı ELLE açılır (betik sonunda
# yazdırılır): Pi'deki ~/.cloudflared/cert.pem tek bir bölgeye (agoravoice.com.tr) bağlı; `cloudflared tunnel
# route dns` başka bölgedeki adı o bölgenin altına ekler, ayrıca ~/.cloudflared/config.yml'deki tüneli seçer.
# Tünel istekleri Caddy'ye (127.0.0.1:8443) iletir; Caddy okulu Host başlığından seçer.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=env.sh
. "$HERE/env.sh"

ssh "$PI_HOST" bash -s -- "$PI_BASE" "$TUNNEL_NAME" "$SCHOOLS_DOMAIN" <<'REMOTE'
set -euo pipefail
BASE="$1" NAME="$2" DOMAIN="$3"
id="$(cloudflared tunnel list -o json 2>/dev/null | python3 -c "import json,sys; print(next((t['id'] for t in json.load(sys.stdin) if t['name']=='$NAME'), ''))")"
if [ -z "$id" ]; then
  cloudflared tunnel create "$NAME" >/dev/null
  id="$(cloudflared tunnel list -o json | python3 -c "import json,sys; print(next(t['id'] for t in json.load(sys.stdin) if t['name']=='$NAME'))")"
fi
install -m 600 "$HOME/.cloudflared/$id.json" "$BASE/tunnel/credentials.json"
cat > "$BASE/tunnel/config.yml" <<YML
tunnel: $id
credentials-file: /etc/cloudflared/credentials.json
ingress:
  - hostname: "*.$DOMAIN"
    service: https://127.0.0.1:8443
    originRequest:
      noTLSVerify: true
      matchSNItoHost: true
  - service: http_status:404
YML
echo "Tünel: $NAME ($id)"
echo "Cloudflare → $DOMAIN → DNS: CNAME, ad '*', hedef $id.cfargotunnel.com, Proxied (turuncu bulut)."
REMOTE
echo "Şimdi deploy/pi/up.sh tünel konteynerini başlatır."
