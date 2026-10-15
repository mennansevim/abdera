#!/usr/bin/env bash
# abdera-host'un GERÇEK Docker'la uçtan uca testi (docs/multi-tenant/ARCHITECTURE.md §6, §16.1-16.3, 16.7, 16.9).
#
# Okul API/web imajları yerine küçük bir vekil (busybox, gerçek registry digest'iyle) kullanılır; veritabanı,
# ağlar, Caddy, döküm/geri yükleme gerçektir. Ölçtükleri:
#   - iki okul kurulur; okullar birbirinin db/api servislerine ağ düzeyinde ulaşamaz (§16.1)
#   - paylaşılan Caddy okulun API'sine kendi edge ağı üzerinden ulaşır, site bloğu yayında
#   - askı: uygulamadan bağımsız şifreli döküm + askı sayfası; devam ettirme
#   - prova geri yükleme: veri prova kopyasına gelir, dış bağlantılar kapalı (§16.7)
#   - iki aşamalı silme: kurulmuş okul bekleme dolmadan silinmez (§16.9)
#
# Linux test sunucusunda: sudo bash deploy/host/test/e2e-real-docker.sh
# macOS + Podman (yerel): deploy/host/test/e2e-podman.sh
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
AGENT="$HERE/../abdera-host"
WORK="${E2E_WORK:-/var/tmp/abdera-e2e}"
export ABDERA_ROOT="$WORK/srv"
export ABDERA_SHARE="$WORK/share"
export ABDERA_CADDY_CONTAINER="abdera-caddy-e2e"
export ABDERA_HEALTH_ATTEMPTS=30
export ABDERA_HEALTH_SLEEP=2
STANDIN="${E2E_STANDIN_IMAGE:-docker.io/library/busybox@sha256:73aaf090f3d85aa34ee199857f03fa3a95c8ede2ffd4cc2cdb5b94e566b11662}"
PASS=0; FAIL=0

ok() { PASS=$((PASS+1)); echo "ok   - $1"; }
bad() { FAIL=$((FAIL+1)); echo "FAIL - $1"; }
check() { local name="$1"; shift; if "$@" >"$WORK/out" 2>&1; then ok "$name"; else bad "$name"; sed 's/^/       /' "$WORK/out" | tail -15; fi; }
refuse() { local name="$1"; shift; if "$@" >"$WORK/out" 2>&1; then bad "$name (beklenmedik başarı)"; else ok "$name"; fi; }

cleanup() {
  # Önce Caddy: okulların edge ağlarına bağlıyken ağlar silinemez.
  docker rm -f "$ABDERA_CADDY_CONTAINER" >/dev/null 2>&1 || true
  for slug in e2eokul e2eiki e2eokul-drill; do
    docker compose -p "abdera-$slug" down --volumes --remove-orphans >/dev/null 2>&1 || true
    docker network rm "abdera-edge-$slug" >/dev/null 2>&1 || true
  done
  # WORK bağlama noktası olabilir (e2e-podman.sh); kendisini değil içini sil.
  [ -d "$WORK" ] && find "$WORK" -mindepth 1 -delete 2>/dev/null
  return 0
}
trap cleanup EXIT
cleanup
mkdir -p "$ABDERA_ROOT/caddy/sites" "$ABDERA_ROOT/caddy/suspended" "$ABDERA_SHARE"
cp "$HERE/../caddy/Caddyfile" "$ABDERA_ROOT/caddy/"
cp "$HERE/../caddy/suspended/index.html" "$ABDERA_ROOT/caddy/suspended/"

# Vekil: api ve web her isteğe 200 döner (sağlık ve kurulum isteği). Yalnızca bu test için şablona
# komut eklenir; gerçek imajların kendi giriş noktası vardır. Sağlık denetimi busybox wget'iyle çalışır.
cat > "$WORK/api.cmd" <<'CMD'
    command: ["sh", "-c", "while true; do printf 'HTTP/1.1 200 OK\\r\\nContent-Type: application/json\\r\\nContent-Length: 2\\r\\nConnection: close\\r\\n\\r\\n{}' | nc -l -p 8080 >/dev/null 2>&1; done"]
CMD
cat > "$WORK/web.cmd" <<'CMD'
    command: ["sh", "-c", "while true; do printf 'HTTP/1.1 200 OK\\r\\nContent-Length: 2\\r\\nConnection: close\\r\\n\\r\\nok' | nc -l -p 3000 >/dev/null 2>&1; done"]
CMD
awk -v api="$WORK/api.cmd" -v web="$WORK/web.cmd" '
  { print }
  /image: \$\{ABDERA_API_IMAGE/ { while ((getline line < api) > 0) print line }
  /image: \$\{ABDERA_WEB_IMAGE/ { while ((getline line < web) > 0) print line }
' "$HERE/../../instance/compose.yml" > "$ABDERA_SHARE/compose.yml"
grep -q 'nc -l -p 8080' "$ABDERA_SHARE/compose.yml" || { echo "vekil şablon üretilemedi"; exit 1; }

# E2E_DOCKER_RUN_OPTS: SELinux'lu yerel VM'de (Podman machine) bind mount etiketi için; Linux test
# sunucusunda boş kalır.
# shellcheck disable=SC2086
docker run -d ${E2E_DOCKER_RUN_OPTS:-} --name "$ABDERA_CADDY_CONTAINER" -e ACME_EMAIL=e2e@abdera.test \
  -v "$ABDERA_ROOT/caddy:/etc/caddy" caddy:2-alpine >/dev/null || { echo "Caddy başlatılamadı"; exit 1; }

env_for() {
  cat <<EOF
INSTANCE_SLUG=$1
ABDERA_API_IMAGE=$STANDIN
ABDERA_WEB_IMAGE=$STANDIN
POSTGRES_PASSWORD=pg-$1-$(head -c 12 /dev/urandom | od -An -tx1 | tr -d ' \n')
BACKUP_GW_PASSWORD=gw-$1
Backup__EncryptionKey=$(head -c 32 /dev/urandom | base64)
Provisioning__Token=provisioning-$1-0123456789abcdef0123456789
Telemetry__Token=telemetry-$1-0123456789abcdef0123456789abc
WhatsApp__Provider=Cloud
Email__Provider=Smtp
EOF
}

# --- iki okul ---
for slug in e2eokul e2eiki; do
  check "$slug oluşturulur" bash -c "$(declare -f env_for); STANDIN='$STANDIN'; env_for $slug | '$AGENT' create $slug"
  check "$slug başlar ve sağlıklı" "$AGENT" up "$slug"
done
check "durum sağlıklı" bash -c "'$AGENT' status e2eokul | grep -q '\"healthy\":true'"
check "kurulum isteği okulun iç ağından gider" bash -c "printf '{\"admin\":{}}' | '$AGENT' provision e2eokul"

# --- izolasyon (§16.1) ---
api_of() { docker compose -p "abdera-$1" ps -q api; }
check "okul kendi db'sine ulaşır" docker exec "$(api_of e2eokul)" sh -c 'nc -z -w 3 db 5432'
refuse "okul diğer okulun api'sine ulaşamaz" docker exec "$(api_of e2eokul)" sh -c 'wget -q -T 3 -O- http://e2eiki-api:8080/health'
# DNS adı başka ağda zaten çözülmez; asıl sınama doğrudan IP ile yapılır.
OTHER_DB_IP="$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}' "$(docker compose -p abdera-e2eiki ps -q db)" | awk '{print $1}')"
refuse "okul diğer okulun db'sine IP ile de ulaşamaz ($OTHER_DB_IP)" docker exec "$(api_of e2eokul)" sh -c "nc -z -w 3 $OTHER_DB_IP 5432"
OTHER_API_IP="$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}} {{end}}' "$(api_of e2eiki)" | awk '{print $1}')"
refuse "okul diğer okulun api'sine IP ile de ulaşamaz ($OTHER_API_IP)" docker exec "$(api_of e2eokul)" sh -c "wget -q -T 3 -O- http://$OTHER_API_IP:8080/health"

# --- Caddy ---
check "yönlendirme eklenir (validate + reload)" "$AGENT" route add e2eokul e2eokul.abdera.test
check "Caddy okulun api'sine edge ağından ulaşır" docker exec "$ABDERA_CADDY_CONTAINER" wget -q -T 3 -O- http://e2eokul-api:8080/health
refuse "Caddy bağlanmadığı ağdaki takma ada ulaşamaz" docker exec "$ABDERA_CADDY_CONTAINER" wget -q -T 3 -O- http://e2eyok-api:8080/health
check "site bloğu yayındaki yapılandırmada" bash -c "docker exec '$ABDERA_CADDY_CONTAINER' wget -q -O- http://127.0.0.1:2019/config/ | grep -q 'e2eokul-api:8080'"
echo 'bozuk.abdera.test { reverse_proxy {' > "$ABDERA_ROOT/caddy/sites/zz-bozuk.caddy"
refuse "doğrulanamayan yapılandırma reddedilir" "$AGENT" route add e2eiki e2eiki.abdera.test
rm -f "$ABDERA_ROOT/caddy/sites/zz-bozuk.caddy"
check "yayındaki yapılandırma bozulmadı" bash -c "docker exec '$ABDERA_CADDY_CONTAINER' wget -q -O- http://127.0.0.1:2019/config/ | grep -q 'e2eokul-api:8080'"

# --- veri, askı, devam ---
docker compose -p abdera-e2eokul exec -T db psql -q -U abdera -d abdera -c "CREATE TABLE e2e (id int); INSERT INTO e2e VALUES (1),(2),(3);" >/dev/null
check "askıya alma" "$AGENT" suspend e2eokul
check "askı dökümü şifreli" bash -c "f=\$(ls '$ABDERA_ROOT'/e2eokul/dumps/*.enc | head -1) && [ -s \"\$f\" ] && ! gunzip -t < \"\$f\" 2>/dev/null"
check "askı sayfası yayında" bash -c "docker exec '$ABDERA_CADDY_CONTAINER' wget -q -O- http://127.0.0.1:2019/config/ | grep -q 'suspended'"
refuse "askıda api durdu" bash -c "docker compose -p abdera-e2eokul ps --status running --services | grep -qx api"
check "devam ettirme" "$AGENT" resume e2eokul

# --- prova geri yükleme (§16.7) ---
DUMP="$(basename "$(ls "$ABDERA_ROOT"/e2eokul/dumps/*.enc | head -1)")"
check "prova geri yükleme" "$AGENT" restore e2eokul "$DUMP" --drill
check "veri prova kopyasına geldi" bash -c "[ \"\$(docker compose -p abdera-e2eokul-drill exec -T db psql -tA -U abdera -d abdera -c 'SELECT count(*) FROM e2e')\" = 3 ]"
check "provada WhatsApp/e-posta kapalı" bash -c "docker exec \$(docker compose -p abdera-e2eokul-drill ps -q api) env | grep -qx 'WhatsApp__Provider=Disabled' && docker exec \$(docker compose -p abdera-e2eokul-drill ps -q api) env | grep -qx 'Email__Provider=Disabled'"
check "prova Caddy'de yok" bash -c "! docker exec '$ABDERA_CADDY_CONTAINER' wget -q -O- http://127.0.0.1:2019/config/ | grep -q 'e2eokul-drill'"

# --- iki aşamalı silme (§16.9) ---
check "silme 1. aşama" "$AGENT" destroy e2eokul
check "veri duruyor" bash -c "docker volume ls --format '{{.Name}}' | grep -q '^abdera-e2eokul_pgdata$'"
refuse "kurulmuş okul bekleme dolmadan silinmez" "$AGENT" purge e2eokul
check "süre dolunca kalıcı silme" env ABDERA_DESTROY_GRACE_DAYS=0 "$AGENT" purge e2eokul
check "veri silindi" bash -c "! docker volume ls --format '{{.Name}}' | grep -q '^abdera-e2eokul_pgdata$'"

echo
echo "geçen: $PASS, kalan: $FAIL"
[ "$FAIL" -eq 0 ]
