#!/usr/bin/env bash
# Abdera test koşucusu - .github/workflows/ci.yml'deki katmanların yerel karşılığı.
#
#   ./tools/test.sh              hepsi (unit + integration + frontend)
#   ./tools/test.sh unit         yalnız birim testleri   - Docker GEREKMEZ, ~5 sn
#   ./tools/test.sh integration  entegrasyon testleri    - Docker GEREKİR
#   ./tools/test.sh backend      unit + integration
#   ./tools/test.sh frontend     lint + build            - Docker GEREKMEZ
#   ./tools/test.sh e2e          Playwright smoke        - Docker GEREKİR, en yavaş
#
# Katmanlar bilerek ayrı: entegrasyon testleri Testcontainers ile gerçek bir Postgres
# konteyneri açar (docs/09-testing.md), birim testleri açmaz. Hızlı geri bildirim için
# geliştirirken `unit`, push öncesi `backend`, sürüm öncesi hepsini koş.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

BOLD=$'\e[1m'; RED=$'\e[31m'; GREEN=$'\e[32m'; YELLOW=$'\e[33m'; OFF=$'\e[0m'
step() { printf '\n%s==> %s%s\n' "$BOLD" "$1" "$OFF"; }
ok()   { printf '%s  ✓ %s%s\n' "$GREEN" "$1" "$OFF"; }
warn() { printf '%s  ! %s%s\n' "$YELLOW" "$1" "$OFF"; }
die()  { printf '\n%s  ✗ %s%s\n' "$RED" "$1" "$OFF" >&2; exit 1; }

# Testcontainers'ın çekeceği imaj - AbderaWebApplicationFactory.cs ile birebir aynı olmalı.
PG_IMAGE="postgres:16-alpine"

require_dotnet() {
  command -v dotnet >/dev/null 2>&1 || die "dotnet bulunamadı. .NET 10 SDK kur: https://dotnet.microsoft.com/download"

  # global.json belirli bir SDK sürümünü sabitliyor. Yüklü SDK daha düşük bir "feature band"
  # ise (örn. global.json 10.0.400 ister, yüklü 10.0.112) dotnet hiçbir şey çalıştırmadan
  # "SDK not found" der - hata mesajı bunun sebebini söylemediği için burada açıkça yakalıyoruz.
  if ! dotnet --version >/dev/null 2>&1; then
    printf '%s' "$RED" >&2
    echo "  ✗ Yüklü .NET SDK global.json ile uyuşmuyor." >&2
    echo "    global.json ister : $(grep -o '"version"[^,}]*' global.json | head -1)" >&2
    echo "    Yüklü SDK'lar     : $(dotnet --list-sdks 2>/dev/null | tr '\n' ' ')" >&2
    echo "    Çözüm: istenen sürümü kur, ya da global.json'daki version'ı yüklü sürüme çek." >&2
    printf '%s' "$OFF" >&2
    exit 1
  fi
}

require_docker() {
  command -v docker >/dev/null 2>&1 || die "docker bulunamadı. Entegrasyon testleri gerçek bir Postgres konteyneri açar."
  docker info >/dev/null 2>&1 || die "Docker daemon çalışmıyor. Docker Desktop'ı (ya da servisi) başlat ve tekrar dene."
}

# Testcontainers imajı test çalışırken çeker; çekme başarısız olursa hata testin ortasında
# ve okunması zor bir biçimde çıkar. Önden çekip sorunu burada, anlaşılır bir mesajla
# yakalıyoruz. İmaj yerelde varsa ağa hiç çıkmaz.
pull_postgres_image() {
  if docker image inspect "$PG_IMAGE" >/dev/null 2>&1; then
    ok "$PG_IMAGE yerelde hazır"
    return
  fi
  step "Test imajı indiriliyor ($PG_IMAGE)"
  if ! docker pull "$PG_IMAGE"; then
    printf '%s' "$RED" >&2
    echo "  ✗ $PG_IMAGE indirilemedi. Yukarıdaki satır sebebi söyler:" >&2
    echo "    'too many requests' / 429 -> Docker Hub anonim indirme limiti. Çözüm: docker login" >&2
    echo "    'Forbidden' / 403 / TLS   -> ağ ya da kurumsal proxy engeli. Çözüm: proxy ayarlarını" >&2
    echo "                                 kontrol et, ya da imajı erişimi olan bir makinede çekip" >&2
    echo "                                 'docker save/load' ile taşı." >&2
    printf '%s' "$OFF" >&2
    exit 1
  fi
}

run_unit() {
  require_dotnet
  step "Birim testleri (Docker gerekmez)"
  dotnet test backend/Abdera.slnx --filter "FullyQualifiedName~Abdera.Tests.Unit"
  ok "Birim testleri geçti"
}

run_integration() {
  require_dotnet
  require_docker
  pull_postgres_image

  # OpsFlowTests gerçek pg_dump çalıştırır (BackupService) - Backup:Provider=Fake yalnızca
  # depolamayı/e-postayı sahteler, pg_dump gerçekten koşar. CI bu paketi açıkça kurar.
  command -v pg_dump >/dev/null 2>&1 \
    || warn "pg_dump yok - OpsFlowTests yedekleme testi kırılır. Kur: apt-get install postgresql-client (macOS: brew install libpq)"

  step "Entegrasyon testleri (Testcontainers ile gerçek Postgres)"
  dotnet test backend/Abdera.slnx --filter "FullyQualifiedName~Abdera.Tests.Integration"
  ok "Entegrasyon testleri geçti"
}

run_frontend() {
  command -v npm >/dev/null 2>&1 || die "npm bulunamadı. Node 22 kur."
  step "Frontend bağımlılıkları"
  [ -d frontend/node_modules ] || npm ci --prefix frontend
  step "Frontend lint"
  npm run lint --prefix frontend
  step "Frontend build (tip kontrolü bu adımda çalışır)"
  npm run build --prefix frontend
  ok "Frontend temiz"
}

# CI'daki e2e-smoke işinin yerel karşılığı: gerçek Compose uygulamasına karşı üç rol smoke.
run_e2e() {
  require_docker
  command -v npm >/dev/null 2>&1 || die "npm bulunamadı. Node 22 kur."

  if [ ! -f .env ]; then
    step ".env hazırlanıyor (.env.example'dan)"
    cp .env.example .env
    # Yalnızca YEREL smoke için sabit parolalar. .env .gitignore'da - commit'lenmez.
    sed -i.bak 's/POSTGRES_PASSWORD=<GUCLU-PAROLA-URET>/POSTGRES_PASSWORD=LocalSmokeOnly123!/' .env
    sed -i.bak 's/Bootstrap__AdminPassword=<ILK-GIRISTE-DEGISTIR>/Bootstrap__AdminPassword=LocalAdminOnly123!/' .env
    # Giriş ve veli OTP uçları IP başına 15 dakikada 5 istekle sınırlı; smoke suite'i tek
    # koşuda bunu tüketir ve testler ÜRÜN HATASI OLMADAN kırmızı döner. Sınır kaldırılmıyor,
    # yalnızca yerel smoke ortamında yeniden denemeye yer bırakacak kadar yükseltiliyor.
    sed -i.bak 's/^RateLimiting__LoginPermitLimit=.*/RateLimiting__LoginPermitLimit=50/' .env
    sed -i.bak 's/^RateLimiting__GuardianOtpPermitLimit=.*/RateLimiting__GuardianOtpPermitLimit=50/' .env
    rm -f .env.bak
    warn ".env oluşturuldu - içindeki parolalar YALNIZCA yerel smoke içindir."
  fi

  [ -d frontend/node_modules ] || npm ci --prefix frontend
  step "Playwright tarayıcısı"
  npx --prefix frontend playwright install --with-deps chromium

  step "Compose uygulaması başlatılıyor"
  docker compose up --build -d
  # Hangi sebeple çıkarsak çıkalım konteynerler ayakta kalmasın.
  trap 'printf "\n%s==> Compose kapatılıyor%s\n" "$BOLD" "$OFF"; docker compose down -v' EXIT

  step "Sağlık kontrolü bekleniyor"
  for attempt in $(seq 1 30); do
    if curl --fail --silent http://localhost:8080/health >/dev/null \
       && curl --fail --silent http://localhost:3000/login >/dev/null; then
      ok "Uygulama ayakta"
      break
    fi
    [ "$attempt" -eq 30 ] && { docker compose ps; docker compose logs --no-color --tail=100; die "Uygulama 60 sn içinde ayağa kalkmadı."; }
    sleep 2
  done

  step "Üç rol Playwright smoke"
  E2E_ADMIN_EMAIL=admin@example.com \
  E2E_ADMIN_PASSWORD=LocalAdminOnly123! \
    npm run test:e2e --prefix frontend
  ok "Smoke geçti"
}

case "${1:-all}" in
  unit)        run_unit ;;
  integration) run_integration ;;
  backend)     run_unit; run_integration ;;
  frontend)    run_frontend ;;
  e2e)         run_e2e ;;
  all)         run_unit; run_integration; run_frontend ;;
  *)
    echo "Bilinmeyen hedef: $1"
    echo "Kullanım: ./tools/test.sh [unit|integration|backend|frontend|e2e|all]"
    exit 2 ;;
esac

printf '\n%s  Bitti.%s\n' "$GREEN" "$OFF"
