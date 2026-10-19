#!/usr/bin/env bash
# Okullar arası oturum izolasyonu (ARCHITECTURE.md §14): A okulunun oturum çerezi B okulunda geçersiz olmalı.
# Önce: python3 deploy/local/seed.py reveal <a> <b>
set -euo pipefail
A="${1:?okul A}"; B="${2:?okul B}"
OUT="$(cd "$(dirname "$0")" && pwd)/.out"
CA="$OUT/caddy-root.crt"
# shellcheck disable=SC1090
. "$OUT/$A-admin.env"
JAR="$(mktemp)"; trap 'rm -f "$JAR"' EXIT
code() { curl -s -o /dev/null -w '%{http_code}' --cacert "$CA" "$@"; }
login=$(code -c "$JAR" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" "https://$A.okul.localhost:8443/api/auth/login")
echo "$A girişi: $login"
# Çerez alan adına bağlı; tarayıcı onu B'ye zaten göndermez. Burada elle B'ye taşınır: B kendi anahtarıyla çözemeli.
cookie="$(awk '$6 ~ /abdera_session/ {print $6"="$7}' "$JAR")"
own=$(code -H "Cookie: $cookie" "https://$A.okul.localhost:8443/api/auth/me")
other=$(code -H "Cookie: $cookie" "https://$B.okul.localhost:8443/api/auth/me")
echo "$A çereziyle $A /api/auth/me: $own (200 beklenir)"
echo "$A çereziyle $B /api/auth/me: $other (401 beklenir)"
[ "$own" = 200 ] && [ "$other" = 401 ]
