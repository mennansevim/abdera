#!/usr/bin/env bash
# Yerel test okuluna 6 aylık örnek veri yükler (öğretmen, öğrenci, veli, ders, aidat).
#   deploy/local/seed-demo.sh <kısaltma>
# Okulun kendi imajı, okulun iç ağında ve kendi veritabanına bağlı GEÇİCİ bir konteynerde geliştirme
# modunda açılır; okul uygulamasının /api/dev/mock-data/seed ucu çağrılır, konteyner silinir. Okulun
# çalışan konteynerlerine dokunulmaz. Yalnızca yerel test ortamı içindir.
set -euo pipefail
SLUG="${1:?okul kısaltması gerekli}"
ROOT=/var/tmp/abdera-local/srv
PORT=18080
NAME="abdera-$SLUG-seeder"

value() { docker exec abdera-local-host sh -c "grep -E '^$1=' '$ROOT/$SLUG/.env' | tail -n1 | cut -d= -f2-"; }
IMAGE="$(value ABDERA_API_IMAGE)"
[ -n "$IMAGE" ] || { echo "okul bulunamadı: $SLUG" >&2; exit 1; }

docker rm -f "$NAME" >/dev/null 2>&1 || true
trap 'docker rm -f "$NAME" >/dev/null 2>&1 || true' EXIT
docker run -d --name "$NAME" --network "abdera-${SLUG}_internal" -p "$PORT:8080" \
  -e ASPNETCORE_ENVIRONMENT=Development -e ASPNETCORE_URLS=http://+:8080 -e Auth__DevLogin__Enabled=true \
  -e "ConnectionStrings__Default=Host=db;Port=5432;Database=abdera;Username=abdera;Password=$(value POSTGRES_PASSWORD)" \
  -e "Modules__Enabled=$(value Modules__Enabled)" -e WhatsApp__Provider=Disabled -e Email__Provider=Disabled \
  -e Ai__Provider=Disabled -e Backup__Provider=Disabled -e Alerts__WebhookUrl= "$IMAGE" >/dev/null

for _ in $(seq 1 60); do curl -sf "http://localhost:$PORT/health" >/dev/null && break; sleep 2; done
JAR="$(mktemp)"
curl -sf -c "$JAR" -H 'Content-Type: application/json' -d '{"role":"Admin"}' "http://localhost:$PORT/api/dev/auth/login" >/dev/null
curl -sS -b "$JAR" -X POST "http://localhost:$PORT/api/dev/mock-data/seed"
echo
# Örnek veri aidat satırı üretmez; kurulumda verilen tarife (Ekim'den geçerli) ile bu ayın aidatları açılır.
curl -sS -b "$JAR" -H 'Content-Type: application/json' -d "{\"period\":\"$(date +%Y-%m)\"}" "http://localhost:$PORT/api/receivables/monthly-run"
echo
rm -f "$JAR"
