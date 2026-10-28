#!/usr/bin/env bash
# Bir kez: Pi'de ayrı bir Cloudflare tüneli açar ve ayarını yazar. DNS kaydı ELLE açılır (betik sonunda
# yazdırılır): Pi'deki ~/.cloudflared/cert.pem tek bir bölgeye (agoravoice.com.tr) bağlı; `cloudflared tunnel
# route dns` başka bölgedeki adı o bölgenin altına ekler, ayrıca ~/.cloudflared/config.yml'deki tüneli seçer.
# Tünel istekleri Caddy'ye (127.0.0.1:8443) iletir; Caddy okulu Host başlığından seçer.
# PLATFORM_PUBLIC=1: platform.<alan adı> panele (Mac, panel.sh) ve /api/* platform API'sine gider. Bu kural
# yalnızca Cloudflare Access o adresi korurken yazılır: betik önce adresin Access girişine yönlendirdiğini
# doğrular, yönlendirmiyorsa durur (panel korumasız internete açılmasın).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=env.sh
. "$HERE/env.sh"

PLATFORM_RULES=""
if [ "${PLATFORM_PUBLIC:-0}" = "1" ]; then
  location="$(curl -s -o /dev/null -w '%{redirect_url}' --max-time 15 "https://platform.$SCHOOLS_DOMAIN/")"
  case "$location" in
    https://*.cloudflareaccess.com/*) echo "Access koruması doğrulandı: platform.$SCHOOLS_DOMAIN" ;;
    *) echo "HATA: platform.$SCHOOLS_DOMAIN Cloudflare Access girişine yönlendirmiyor ($location); kural yazılmadı." >&2; exit 1 ;;
  esac
  PLATFORM_RULES="  - hostname: platform.$SCHOOLS_DOMAIN
    path: ^/api/
    service: http://$MAC_IP:8090
  - hostname: platform.$SCHOOLS_DOMAIN
    service: http://$MAC_IP:${PANEL_PORT:-3200}
"
fi

# ssh argümanları uzak kabukta yeniden bölünür: çok satırlı kural base64 ile taşınır.
ssh "$PI_HOST" bash -s -- "$PI_BASE" "$TUNNEL_NAME" "$SCHOOLS_DOMAIN" "$(printf '%s' "$PLATFORM_RULES" | base64 | tr -d '\n')" <<'REMOTE'
set -euo pipefail
BASE="$1" NAME="$2" DOMAIN="$3" PLATFORM_RULES="$(printf '%s' "${4:-}" | base64 -d)"
[ -z "$PLATFORM_RULES" ] || PLATFORM_RULES="$PLATFORM_RULES
"
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
${PLATFORM_RULES}  - hostname: "*.$DOMAIN"
    service: https://127.0.0.1:8443
    originRequest:
      noTLSVerify: true
      matchSNItoHost: true
  - service: http_status:404
YML
docker restart abdera-pi-tunnel >/dev/null 2>&1 || true
echo "Tünel: $NAME ($id)"
echo "Cloudflare → $DOMAIN → DNS: CNAME, ad '*', hedef $id.cfargotunnel.com, Proxied (turuncu bulut)."
REMOTE
echo "Şimdi deploy/pi/up.sh tünel konteynerini başlatır."
