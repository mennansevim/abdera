#!/usr/bin/env bash
# Deploy sonrası canlı duman testi (gece turu, .claude/skills/abdera-nightly-bugfix).
# Kullanım: smoke-check.sh <deploy edilen 40 karakterlik sha> [log penceresi, dakika = 15]
# Çıkış 0 = sağlıklı; değilse her sorun stdout'a "FAIL: ..." satırı olarak yazılır.
set -uo pipefail

SHA="${1:?sha gerekli}"
SINCE="${2:-15}"
BASE="https://panel.abderasanat.com"
SSH_TARGET="deploy@panel.abderasanat.com"
fails=0
fail() { echo "FAIL: $*"; fails=$((fails + 1)); }

expect() { # expect <yol> <beklenen http kodu>
  local code
  code=$(curl -s -o /dev/null -m 20 -w '%{http_code}' "$BASE$1")
  [ "$code" = "$2" ] && echo "ok   $1 $code" || fail "$1 -> $code (beklenen $2)"
}

expect /health 200
expect / 200
expect /login 200
expect /dashboard 200
expect /parent 200
expect /gizlilik 200
expect /manifest.webmanifest 200
expect /api/auth/me 401   # API ayakta ve oturumsuz istek reddediliyor (500 değil)

curl -s -m 20 "$BASE/health" | grep -q '"status":"Healthy"' || fail "/health gövdesi Healthy değil"

# Sayfanın yüklediği JS paketleri gerçekten servis ediliyor mu (yarım kalmış frontend build'i)
chunk=$(curl -s -m 20 "$BASE/login" | grep -oE '/_next/static/[^"]+\.js' | head -1)
if [ -z "$chunk" ]; then fail "/login içinde _next/static script bulunamadı"
else expect "$chunk" 200; fi

remote=$(ssh -o BatchMode=yes -o ConnectTimeout=15 "$SSH_TARGET" bash -s -- "$SINCE" <<'REMOTE'
cd /opt/abdera
echo "HEAD $(git rev-parse HEAD)"
# </dev/null: docker compose aksi halde bash -s'in okuduğu betiğin kalanını yutar
docker compose --profile prod ps --format '{{.Service}} {{.Status}}' </dev/null | sed 's/^/PS /'
echo "ERRORS $(docker compose logs --since "${1}m" api </dev/null 2>&1 | grep -cE '\[(ERR|FTL)\]|fail:|Unhandled')"
REMOTE
)
[ -n "$remote" ] || fail "sunucuya SSH ile bağlanılamadı"
echo "$remote" | grep -q "^HEAD $SHA$" || fail "sunucudaki commit $SHA değil: $(echo "$remote" | grep '^HEAD')"
for svc in api web db caddy; do
  line=$(echo "$remote" | grep "^PS $svc ")
  case "$line" in
    *Up*unhealthy*|*Restarting*|"") fail "container $svc: ${line:-yok}";;
    *Up*) echo "ok   $line";;
    *) fail "container $svc: $line";;
  esac
done
errs=$(echo "$remote" | sed -n 's/^ERRORS //p')
[ "${errs:-0}" -le 5 ] && echo "ok   api log hata sayısı ($SINCE dk): ${errs:-0}" \
  || fail "api loglarında son $SINCE dakikada $errs hata satırı"

[ "$fails" -eq 0 ] && echo "SMOKE OK" || { echo "SMOKE FAILED ($fails)"; exit 1; }
