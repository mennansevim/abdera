#!/usr/bin/env bash
# Platform panelini üretim modunda (standalone) Mac'te 0.0.0.0:3200'de çalıştırır; tünel
# platform.<alan adı> isteklerini buraya, /api/* isteklerini platform API'sine (8090) iletir
# (canlıda bu ayrımı Caddy yapar). Önünde Cloudflare Access olmadan tünele EKLENMEZ: tunnel-dns.sh.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
WEB="$HERE/../../platform/web"
cd "$WEB"
npm run build >/dev/null
# Standalone çıktı statik dosyaları kendisi taşımaz.
rm -rf .next/standalone/.next/static .next/standalone/public
cp -r .next/static .next/standalone/.next/static
[ -d public ] && cp -r public .next/standalone/public
PORT="${PANEL_PORT:-3200}" HOSTNAME=0.0.0.0 exec node .next/standalone/server.js
