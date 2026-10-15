#!/usr/bin/env bash
# abdera-host davranış testleri (docs/multi-tenant/ARCHITECTURE.md §6.1, §16).
# Gerçek Docker gerekmez: PATH'e konan sahte `docker` çağrıları kaydeder ve durumları taklit eder.
# Linux'ta (flock gerekli) çalışır; yerelde: deploy/host/test/run.sh
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
AGENT="$HERE/../abdera-host"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
PASS=0; FAIL=0

export ABDERA_ROOT="$WORK/srv"
export ABDERA_SHARE="$WORK/share"
export ABDERA_HEALTH_ATTEMPTS=2
export ABDERA_HEALTH_SLEEP=0
export STUB_LOG="$WORK/docker.log"
export STUB_STATE="$WORK/state"
mkdir -p "$ABDERA_ROOT/caddy/sites" "$ABDERA_ROOT/caddy/suspended" "$ABDERA_SHARE" "$WORK/bin" "$STUB_STATE"
cp "$HERE/../caddy/Caddyfile" "$ABDERA_ROOT/caddy/Caddyfile"
cp "$HERE/../caddy/suspended/index.html" "$ABDERA_ROOT/caddy/suspended/"
cp "$HERE/../../instance/compose.yml" "$ABDERA_SHARE/compose.yml"

# --- sahte docker ---
cat > "$WORK/bin/docker" <<'STUB'
#!/usr/bin/env bash
echo "docker $*" >> "$STUB_LOG"
args="$*"
case "$args" in
  *"caddy validate"*)
    # İçerikte "BOZUK" geçen bir site dosyası varsa doğrulama başarısız.
    cfg="$(echo "$args" | sed -E 's/.*--config ([^ ]+).*/\1/')"
    dir="$ABDERA_ROOT/caddy/$(dirname "${cfg#/etc/caddy/}")"
    grep -rq "BOZUK" "$dir/sites" 2>/dev/null && exit 1
    exit 0 ;;
  *"caddy reload"*) exit 0 ;;
  "network inspect"*) [ -f "$STUB_STATE/net-${args##* }" ] && exit 0 || exit 1 ;;
  "network create"*) touch "$STUB_STATE/net-${args##* }"; exit 0 ;;
  "network "*) exit 0 ;;
  *"exec -T api wget -q --spider"*)
    # unhealthy dosyası kalan başarısız kontrol sayısını tutar.
    if [ -f "$STUB_STATE/unhealthy" ]; then
      n="$(cat "$STUB_STATE/unhealthy")"
      if [ "$n" -gt 0 ]; then echo $((n-1)) > "$STUB_STATE/unhealthy"; exit 1; fi
    fi
    exit 0 ;;
  *"exec -T db pg_dump"*) echo "-- SQL DUMP"; exit 0 ;;
  *"exec -T api sh -c"*) cat > "$STUB_STATE/provision-body"; [ -f "$STUB_STATE/provision-fails" ] && exit 1; echo '{"alreadyProvisioned":false}'; exit 0 ;;
  *"exec -T db pg_isready"*) exit 0 ;;
  *"exec -T db psql"*) cat > /dev/null; exit 0 ;;
  run*) exit 0 ;;
  *) exit 0 ;;
esac
STUB
chmod +x "$WORK/bin/docker"
export PATH="$WORK/bin:$PATH"

check() { # $1 açıklama, $2 beklenen çıkış kodu, $3... komut
  local name="$1" want="$2"; shift 2
  local got
  "$@" >"$WORK/out" 2>"$WORK/err"; got=$?
  if [ "$got" = "$want" ]; then PASS=$((PASS+1)); echo "ok   - $name"
  else FAIL=$((FAIL+1)); echo "FAIL - $name (beklenen $want, gelen $got)"; sed 's/^/       /' "$WORK/err"; fi
}
assert() { # $1 açıklama, $2... test ifadesi
  local name="$1"; shift
  if "$@"; then PASS=$((PASS+1)); echo "ok   - $name"; else FAIL=$((FAIL+1)); echo "FAIL - $name"; fi
}

API="ghcr.io/abdera/abdera-api@sha256:$(printf 'a%.0s' $(seq 1 64))"
WEB="ghcr.io/abdera/abdera-web@sha256:$(printf 'b%.0s' $(seq 1 64))"
API2="ghcr.io/abdera/abdera-api@sha256:$(printf 'c%.0s' $(seq 1 64))"
WEB2="ghcr.io/abdera/abdera-web@sha256:$(printf 'd%.0s' $(seq 1 64))"
env_for() {
  cat <<EOF
INSTANCE_SLUG=$1
ABDERA_API_IMAGE=$API
ABDERA_WEB_IMAGE=$WEB
POSTGRES_PASSWORD=pg-secret
Telemetry__Token=telemetry-0123456789abcdef0123456789abcdef
Provisioning__Token=provisioning-0123456789abcdef0123456789ab
Backup__EncryptionKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=
WhatsApp__Provider=Cloud
Email__Provider=Smtp
R2_ACCESS_KEY_ID=r2-key
R2_SECRET_ACCESS_KEY=r2-secret
R2_ACCOUNT_ID=acc
R2_BUCKET=abdera-backups
EOF
}

# --- Girdi doğrulama ---
check "geçersiz kısaltma reddedilir" 2 "$AGENT" status "Deniz"
check "noktalı virgül enjeksiyonu reddedilir" 2 env SSH_ORIGINAL_COMMAND="status deniz;touch /tmp/pwn" "$AGENT"
check "komut değiştirme reddedilir" 2 env SSH_ORIGINAL_COMMAND='status $(id)' "$AGENT"
check "bilinmeyen komut" 2 "$AGENT" rm deniz
check "olmayan okul" 4 "$AGENT" status deniz
check "sürüm" 0 "$AGENT" version

# --- create ---
check "digest'siz imaj reddedilir" 2 bash -c "env_for() { :; }; printf 'INSTANCE_SLUG=deniz\nABDERA_API_IMAGE=ghcr.io/a/b:latest\nABDERA_WEB_IMAGE=$WEB\n' | '$AGENT' create deniz"
check "slug uyuşmazlığı reddedilir" 2 bash -c "$(declare -f env_for); API='$API'; WEB='$WEB'; env_for baska | '$AGENT' create deniz"
check "okul oluşturulur" 0 bash -c "$(declare -f env_for); API='$API'; WEB='$WEB'; env_for deniz | '$AGENT' create deniz"
assert ".env 0600" [ "$(stat -c %a "$ABDERA_ROOT/deniz/.env")" = "600" ]
assert "api.env R2 anahtarı taşımaz" bash -c "! grep -q '^R2_' '$ABDERA_ROOT/deniz/api.env' && grep -q '^POSTGRES_PASSWORD=' '$ABDERA_ROOT/deniz/api.env'"
assert "edge ağı oluşturuldu" grep -q "network create abdera-edge-deniz" "$STUB_LOG"
check "aynı .env ile tekrar create değişiklik yapmaz" 0 bash -c "$(declare -f env_for); API='$API'; WEB='$WEB'; env_for deniz | '$AGENT' create deniz"
check "farklı .env ile create reddedilir" 6 bash -c "$(declare -f env_for); API='$API'; WEB='$WEB'; { env_for deniz; echo X=1; } | '$AGENT' create deniz"

# --- reconfigure ---
check "imaj reconfigure ile değişmez" 2 bash -c "$(declare -f env_for); API='$API2'; WEB='$WEB'; env_for deniz | '$AGENT' reconfigure deniz"
check "modül değişikliği uygulanır" 0 bash -c "$(declare -f env_for); API='$API'; WEB='$WEB'; { env_for deniz; echo Modules__Enabled=none; } | '$AGENT' reconfigure deniz"
assert "yeni modül değeri api.env'de" grep -qx "Modules__Enabled=none" "$ABDERA_ROOT/deniz/api.env"
echo 2 > "$STUB_STATE/unhealthy"
check "sağlıksız yapılandırma geri alınır" 7 bash -c "$(declare -f env_for); API='$API'; WEB='$WEB'; { env_for deniz; echo Modules__Enabled=progress; } | '$AGENT' reconfigure deniz"
assert "önceki yapılandırmaya dönüldü" grep -qx "Modules__Enabled=none" "$ABDERA_ROOT/deniz/.env"
rm -f "$STUB_STATE/unhealthy"

# --- route (Caddy) ---
check "geçersiz alan adı" 2 "$AGENT" route add deniz "deniz.abdera.app;rm"
check "yönlendirme eklenir" 0 "$AGENT" route add deniz deniz.abdera.app
assert "site bloğu yazıldı" grep -q "reverse_proxy deniz-api:8080" "$ABDERA_ROOT/caddy/sites/deniz.caddy"
assert "caddy reload çağrıldı" grep -q "caddy reload" "$STUB_LOG"
# Doğrulanamayan yapılandırma yayına girmez: başka okulun bozuk bloğu varmış gibi davran.
cp "$ABDERA_ROOT/caddy/sites/deniz.caddy" "$WORK/deniz.before"
echo "BOZUK" > "$ABDERA_ROOT/caddy/sites/zzz.caddy"
check "doğrulanamayan Caddy yapılandırması reddedilir" 5 "$AGENT" route add deniz yeni.abdera.app
assert "yayındaki site dosyası değişmedi" cmp -s "$WORK/deniz.before" "$ABDERA_ROOT/caddy/sites/deniz.caddy"
rm -f "$ABDERA_ROOT/caddy/sites/zzz.caddy"
assert "staging dizini temizlendi" bash -c "! ls -d '$ABDERA_ROOT'/caddy/.staging.* 2>/dev/null"

# --- upgrade / rollback ---
# Yeni sürüm iki sağlık kontrolünde de düşer (ABDERA_HEALTH_ATTEMPTS=2), geri dönülen sürüm sağlıklı.
echo 2 > "$STUB_STATE/unhealthy"
check "sağlıksız güncelleme geri alınır" 9 "$AGENT" upgrade deniz "$API2" "$WEB2"
assert "önceki imaja dönüldü" grep -qxF "ABDERA_API_IMAGE=$API" "$ABDERA_ROOT/deniz/.env"
assert "güncelleme öncesi döküm alındı" bash -c "ls '$ABDERA_ROOT'/deniz/predeploy/*.sql.gz >/dev/null"
rm -f "$STUB_STATE/unhealthy"
check "sağlıklı güncelleme" 0 "$AGENT" upgrade deniz "$API2" "$WEB2"
assert "yeni imaj yazıldı" grep -qxF "ABDERA_API_IMAGE=$API2" "$ABDERA_ROOT/deniz/.env"
check "geçersiz imajla güncelleme" 2 "$AGENT" upgrade deniz "ghcr.io/x/y:latest" "$WEB2"

# --- işlem kimliği (yeniden deneme) ---
check "işlemli komut" 0 "$AGENT" route add deniz deniz.abdera.app --op op-12345678
before="$(wc -l < "$STUB_LOG")"
check "aynı işlem yeniden çalışmaz" 0 "$AGENT" route add deniz deniz.abdera.app --op op-12345678
assert "tekrarda docker çağrılmadı" [ "$before" = "$(wc -l < "$STUB_LOG")" ]
check "geçersiz işlem kimliği" 2 "$AGENT" status deniz --op 'x;y'

# --- askı ---
check "askıya alma" 0 "$AGENT" suspend deniz
assert "askı sayfası yayında" grep -q "status 503" "$ABDERA_ROOT/caddy/sites/deniz.caddy"
assert "şifreli askı dökümü alındı" bash -c "ls '$ABDERA_ROOT'/deniz/dumps/*.sql.gz.enc >/dev/null"
assert "döküm açık metin değil" bash -c "! grep -q 'SQL DUMP' '$ABDERA_ROOT'/deniz/dumps/*.enc"
assert "döküm R2 suspended/ önekine kopyalandı" grep -q "r2:abdera-backups/deniz/suspended/" "$STUB_LOG"
assert "R2 anahtarı komut satırında değil" bash -c "! grep -q 'r2-secret' '$STUB_LOG'"
check "devam ettirme" 0 "$AGENT" resume deniz
assert "canlı site geri geldi" grep -q "reverse_proxy deniz-web:3000" "$ABDERA_ROOT/caddy/sites/deniz.caddy"

# --- prova geri yükleme ---
DUMP="$(basename "$(ls "$ABDERA_ROOT"/deniz/dumps/*.enc | head -1)")"
check "geçersiz döküm adı" 2 "$AGENT" restore deniz "../../etc/passwd"
check "prova geri yükleme" 0 "$AGENT" restore deniz "$DUMP" --drill
assert "provada WhatsApp kapalı" grep -qx "WhatsApp__Provider=Disabled" "$ABDERA_ROOT/deniz-drill/.env"
assert "provada e-posta kapalı" grep -qx "Email__Provider=Disabled" "$ABDERA_ROOT/deniz-drill/.env"
assert "provada alarm kapalı" grep -qx "Alerts__WebhookUrl=" "$ABDERA_ROOT/deniz-drill/.env"
assert "provada R2 anahtarı yok" bash -c "! grep -q '^R2_ACCESS_KEY_ID=' '$ABDERA_ROOT/deniz-drill/.env'"
assert "prova Caddy'ye eklenmedi" [ ! -f "$ABDERA_ROOT/caddy/sites/deniz-drill.caddy" ]

# --- kurulum isteği ---
check "boş kurulum isteği reddedilir" 2 bash -c "printf '' | '$AGENT' provision deniz"
check "kurulum isteği iletilir" 0 bash -c "printf '{\"admin\":{}}' | '$AGENT' provision deniz"
assert "istek gövdesi iç ağa gitti" grep -q '"admin"' "$STUB_STATE/provision-body"
assert "jeton komut satırında değil" bash -c "! grep -q 'provisioning-0123' '$STUB_LOG'"
assert "kurulum işareti yazıldı" [ -f "$ABDERA_ROOT/deniz/.provisioned" ]

# --- iki aşamalı silme ---
check "silme 1. aşama" 0 "$AGENT" destroy deniz
assert "okul dizini duruyor" [ -f "$ABDERA_ROOT/deniz/.env" ]
assert "yönlendirme kaldırıldı" [ ! -f "$ABDERA_ROOT/caddy/sites/deniz.caddy" ]
check "süre dolmadan kalıcı silme reddedilir" 10 "$AGENT" purge deniz
check "silme geri alınır" 0 "$AGENT" undestroy deniz
assert "yönlendirme geri geldi" [ -f "$ABDERA_ROOT/caddy/sites/deniz.caddy" ]
check "tekrar silme 1. aşama" 0 "$AGENT" destroy deniz
check "süre dolunca kalıcı silme" 0 env ABDERA_DESTROY_GRACE_DAYS=0 "$AGENT" purge deniz
assert "okul dizini silindi" [ ! -d "$ABDERA_ROOT/deniz" ]
check "destroy olmadan purge reddedilir" 10 "$AGENT" purge deniz-drill

# Kurulumu hiç tamamlanmamış okul (veri yok) beklemeden temizlenebilir; yarıda kalan kurulumun geri alınması.
check "yarım okul oluşturulur" 0 bash -c "$(declare -f env_for); API='$API'; WEB='$WEB'; env_for yarim | '$AGENT' create yarim"
touch "$STUB_STATE/provision-fails"
check "okul kurulumu reddederse hata" 11 bash -c "printf '{}' | '$AGENT' provision yarim"
rm -f "$STUB_STATE/provision-fails"
check "yarım okul silme 1. aşama" 0 "$AGENT" destroy yarim
check "yarım okul beklemeden silinir" 0 "$AGENT" purge yarim
assert "yarım okul dizini silindi" [ ! -d "$ABDERA_ROOT/yarim" ]

echo
echo "geçen: $PASS, kalan: $FAIL"
[ "$FAIL" -eq 0 ]
