# Railway'e Taşıma Planı

Durum: **Plan — henüz uygulanmadı.** Bugünkü canlı yayın Vercel Services (`vercel.json`:
`web` = Next.js, `api` = `backend/Dockerfile.vercel` container) + Supabase PostgreSQL üzerinde,
`Runtime__Serverless=true` ile çalışıyor. Bu belge Railway'e geçişin hedef topolojisini, kod/
konfigürasyon değişikliklerini, veri taşımayı ve geçiş gününün adımlarını tarif eder.

## 1. Neden bu taşıma kod açısından kolay

Railway kalıcı (long-running) container çalıştırır. Uygulama zaten bu model için yazıldı —
serverless uyarlamaları (`Runtime:Serverless`, Vercel Cron uçları, bağlantı havuzunun kapatılması)
Vercel için sonradan eklenmiş istisnalardı. Railway'de:

- `Runtime__Serverless=false` → `BackgroundService`'ler (bildirim dağıtıcı, aylık aidat üretici,
  gecikme taraması, yedekleme, sistem sağlığı, ders notu hatırlatıcı) yeniden devreye girer.
  Vercel Cron'a gerek kalmaz (`DailyBillingCron`/`LessonNoteReminderCron` uçları `CRON_SECRET`
  tanımlanmazsa 404 döner — zararsız, kod değişmez; `docs/10-decisions.md` N3).
- Npgsql havuzu yeniden açılır, migration'lar açılışta **senkron** uygulanır (Kestrel ancak sonra
  bind eder — healthcheck bunu bekler).
- Mevcut `backend/Dockerfile` ve `frontend/Dockerfile` olduğu gibi kullanılır
  (`Dockerfile.vercel` gerekmez).

## 2. Hedef topoloji

```
                 İnternet (HTTPS, Railway edge TLS'i sonlandırır)
                               │
                   ┌───────────▼───────────┐
                   │ gateway  (Caddy)      │  ← tek public domain: okul.example.com
                   │ :$PORT                │
                   └──┬─────────────────┬──┘
     /api/*, /health  │                 │  geri kalan her şey
        ┌─────────────▼──┐        ┌─────▼──────────┐
        │ api (.NET 10)  │        │ web (Next.js)  │   ← public domain YOK
        │ :8080          │        │ :3000          │
        └───────┬────────┘        └────────────────┘
                │  private network (*.railway.internal)
        ┌───────▼────────┐
        │ Postgres 16    │  ← Railway Postgres, volume'lü
        └────────────────┘
```

Railway projesi: `abdera`, ortam `production` (+ istenirse `staging`). Bölge: **EU West
(Amsterdam)** — İstanbul'a en yakın Railway bölgesi.

### Neden ayrı bir gateway servisi (Caddy)?

Mevcut prod mimarisinin temel varsayımı **tek origin**'dir (`Caddyfile`, `docs/17-technical-architecture.md` §7.1):

1. **Oturum çerezi.** Cookie `SameSite=Lax` + `Secure`. Railway'in verdiği `*.up.railway.app`
   alt alan adları Public Suffix List'te; `web-xxx.up.railway.app` ile `api-xxx.up.railway.app`
   **farklı site** sayılır ve tarayıcı `fetch` isteklerinde Lax çerezi göndermez → giriş sessizce
   bozulur. Tek origin bu sınıf hatayı tamamen ortadan kaldırır.
2. **Frontend kodu zaten tek origin bekliyor.** `frontend/src/lib/api.ts`: production'da
   `NEXT_PUBLIC_API_BASE_URL` boşsa aynı origin'deki `/api` kullanılır — kod değişikliği yok.
3. **`X-Forwarded-For` güveni.** `Program.cs` `KnownProxies`/`KnownIPNetworks`'ü temizleyip
   header'a koşulsuz güvenir; bu ancak API **dışarıdan doğrudan erişilemezken** güvenlidir. API'ye
   public domain verilmez, yalnızca gateway üzerinden private network ile erişilir — Compose'daki
   `127.0.0.1` bağlamasının Railway karşılığı budur. (Aksi halde herkes XFF'i sahteleyerek login
   rate limit'ini atlatabilir.)
4. **Güvenlik başlıkları** (HSTS, CSP, X-Frame-Options...) bugün Caddyfile'da; aynen korunur.

Alternatif (reddedildi): Next.js `rewrites()` ile `/api`'yi API'ye proxy'lemek. Bir servis
eksiltir, ama güvenlik başlıklarını `next.config.ts`'e taşımayı, rewrite hedefinin derleme anında
sabitlenmesini ve Next'in XFF davranışının ayrıca doğrulanmasını gerektirir. Kazanç (~ayda birkaç $)
bu riske değmez.

## 3. Repo değişiklikleri (tek PR)

| Dosya | Değişiklik |
|---|---|
| `deploy/railway/gateway/Dockerfile` | `FROM caddy:2-alpine` + `Caddyfile.railway` kopyası. |
| `deploy/railway/gateway/Caddyfile` | Mevcut `Caddyfile`'ın türevi: site adresi `:{$PORT}`, `auto_https off` (TLS'i Railway edge yapar), upstream'ler `{$API_UPSTREAM}` / `{$WEB_UPSTREAM}` (ör. `api.railway.internal:8080`), `servers { trusted_proxies static private_ranges }` (Railway edge'in gönderdiği gerçek istemci IP'si API'ye ulaşsın), aynı `header` bloğu. |
| `backend/railway.json` | `build.dockerfilePath=Dockerfile`, `deploy.healthcheckPath=/health`, `healthcheckTimeout=300`, `numReplicas=1`, `restartPolicyType=ON_FAILURE`, `watchPatterns=["/backend/src/**"]`. |
| `frontend/railway.json` | `dockerfilePath=Dockerfile`, `watchPatterns=["/frontend/**"]`. |
| `deploy/railway/gateway/railway.json` | `watchPatterns=["/deploy/railway/gateway/**"]`. |
| `.claude/skills/abdera-deploy/SKILL.md` | Vercel → Railway (deploy takibi GitHub deployment kayıtlarıyla aynı şekilde; canlı adres değişir). |
| `docs/17-technical-architecture.md` §7 | Railway topolojisi eklenir. |
| `docs/10-decisions.md` | Yeni karar satırı: "Barındırma Railway; gateway ile tek origin; tek replica" (aşağıdaki gerekçelerle). |

Uygulama kodunda **zorunlu değişiklik yok**. Opsiyonel küçük düzeltme (aşağıdaki §5.3 notu):
`BackupService.RunPgDumpAsync` bağlantı dizesini `NpgsqlConnectionStringBuilder` ile ayrıştırıyor
ve `postgresql://` URI biçimini desteklemiyor (`Program.cs` destekliyor). Railway'de keyword
biçimini kullanacağımız için şart değil, ama tutarlılık için `ResolveConnectionString`'in ortak bir
yardımcıya çıkarılması iyi olur.

Vercel dosyaları (`vercel.json`, `.vercelignore`, `backend/Dockerfile.vercel`,
`Runtime:Serverless` kod yolları) geçiş doğrulanıp Vercel kapatılana kadar **silinmez** — geri dönüş
yolu onlar. Temizlik ayrı, sonraki bir PR.

## 4. Railway servis kurulumu

Tüm servisler aynı GitHub reposuna (`mennansevim/abdera`, dal `main`) bağlanır; her birinin
**Root Directory**'si ayrı ayarlanır. `main`'e push → Railway otomatik deploy (watch pattern'e
göre yalnızca etkilenen servis yeniden derlenir).

### 4.1 `Postgres`

- Railway Postgres şablonu, **major sürüm 16'ya sabitlenmeli** (imaj etiketi `:16`).
  Neden: API imajı `postgresql16-client` taşıyor; `pg_dump` 16, **daha yeni** bir sunucuyu (17/18)
  dökemez → yedekleme sessizce `FAILED` olur. Sunucu sürümü yükseltilecekse Dockerfile'daki
  istemci paketi aynı PR'da yükseltilir.
- Public TCP proxy **kapalı** kalır (Compose'daki "veritabanı asla internete açılmaz" kuralı).
  Taşıma sırasında geçici açılabilir (§6), iş bitince kapatılır.

### 4.2 `api` — Root Directory `backend/`

Ağ: **public domain yok**. Private hostname `api.railway.internal`.

Ortam değişkenleri (Railway referans sözdizimi `${{Servis.DEĞİŞKEN}}`):

```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://[::]:8080          # private network için IPv6 dual-stack bind
SCHOOL_TIMEZONE=Europe/Istanbul

# Keyword biçimi ZORUNLU: BackupService pg_dump için bu diziyi NpgsqlConnectionStringBuilder
# ile ayrıştırıyor, postgresql:// URI'yi okuyamaz. Havuz, Railway Postgres'in varsayılan
# max_connections=100'ünün altında tutulur (LOADTEST.md'deki 150, max_connections=250 içindi).
ConnectionStrings__Default=Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}};Maximum Pool Size=40;Connection Idle Lifetime=60
Database__AutoMigrate=true

Runtime__Serverless=false
Frontend__Origin=https://<gateway-domain>     # önce *.up.railway.app, sonra özel domain

# Data Protection anahtarları DB'de: Railway volume'leri root sahipli bağlanır, imaj ise
# "app" (1654) kullanıcısıyla koşuyor -> /app/keys volume'ü yazılamaz. DB'de tutmak volume
# ihtiyacını kaldırır ve Supabase'den taşınan anahtarlarla mevcut oturumlar da korunur.
Auth__PersistKeysToDatabase=true
Auth__CookieName=abdera_session
Auth__SessionHours=12
Auth__DevLogin__Enabled=false
Demo__Enabled=false

Bootstrap__AdminEmail=...                     # yalnızca boş DB'de etkili
Bootstrap__AdminPassword=...

WhatsApp__Provider=Cloud                      # veya Disabled
WhatsApp__PhoneNumberId / AccessToken / ApiVersion / WebhookVerifyToken / AppSecret / PayloadSigningKey
Banking__Provider=Manual
Backup__Provider=Sftp                         # veya Disabled (bkz. §5.2)
Backup__EncryptionKey=...                     # MEVCUT anahtarla aynı olmalı, yoksa eski yedekler açılmaz
Backup__Sftp__*=...
Email__Provider=Fake | Smtp                   # bkz. §5.1
Ops__AlertRecipients=...
Notifications__*, Policy__*, Scheduling__*, RateLimiting__*, Ai__*  # .env.example varsayılanları
```

`CRON_SECRET` **tanımlanmaz** (cron uçları kapalı kalır; işi arka plan servisleri yapar).

Değerlerin kaynağı: bugünkü Vercel projesinin ortam değişkenleri. Değişenler yalnızca
`ConnectionStrings__Default`, `Runtime__Serverless`, `Frontend__Origin`, `ASPNETCORE_URLS`.

Deploy ayarları: **replica = 1** (bkz. §5.3), healthcheck `/health`, timeout 300 sn (ilk açılışta
migration çalışır).

### 4.3 `web` — Root Directory `frontend/`

Ağ: public domain yok. Private hostname `web.railway.internal`.

```
HOSTNAME=::                     # Dockerfile'daki 0.0.0.0 yalnızca IPv4; eski (IPv6-only) Railway
                                # ortamlarında gateway ulaşamaz. :: iki yığını da dinler.
PORT=3000
NEXT_PUBLIC_DEMO_ENABLED=false
```

`NEXT_PUBLIC_API_BASE_URL` **tanımlanmaz** → build'de boş kalır → istemci aynı origin'deki `/api`'yi
kullanır (`api.ts`). Değer tanımlanırsa build anında gömülür; değiştirmek yeniden derleme ister.

### 4.4 `gateway` — Root Directory `deploy/railway/gateway/`

Ağ: **tek public domain burada** (önce Railway'in verdiği `*.up.railway.app`, sonra özel domain).

```
API_UPSTREAM=api.railway.internal:8080
WEB_UPSTREAM=web.railway.internal:3000
```

`PORT` Railway tarafından enjekte edilir; Caddy `:{$PORT}` dinler.

## 5. Kararlar ve riskler

### 5.1 E-posta (SMTP) — Hobby planında engelli

Railway, Free/Trial/**Hobby** planlarında giden SMTP'yi (25/465/587) engelliyor; yalnızca **Pro**
ve üstünde açık. Bugünkü `Email__Provider=Smtp` (Gmail uygulama şifresi) Hobby'de "Network is
unreachable" ile düşer — `SystemHealthMonitor` alarmları hiç gitmez.

Seçenekler:
- **(a) Pro plan** — kod değişmez. Önerilen, eğer bütçe uygunsa.
- **(b) Hobby + HTTP API'li e-posta sağlayıcısı** (Resend/Postmark, 443 üzerinden) — yeni bir
  `IEmailSender` implementasyonu gerekir (yeni bağımlılık değil, düz `HttpClient`). CLAUDE.md
  gereği önce `docs/10-decisions.md`'de onaylanmalı.
- **(c) Hobby + `Email__Provider=Fake`** — alarm e-postası yok; kabul edilebilir değil, çünkü
  yedek başarısızlığını haber veren tek kanal bu.

**Karar gerekiyor.**

### 5.2 Yedekleme

- Uygulama içi yedek (`BackupService` → şifreli `pg_dump` → SFTP) aynen çalışır; giden 22 portu
  engelli değil. SFTP private key dosya yolu bekliyor (`Backup__Sftp__PrivateKeyPath`) — Railway'de
  dosya yok; ya parola kullanılır ya da anahtar içeriği bir ortam değişkeninden dosyaya
  yazılır (küçük kod değişikliği, ayrı PR).
- Ek güvence: Railway Postgres volume'ü için Railway'in kendi **volume backup** zamanlaması da
  açılır (ücretli planlarda). Bu, `docs/16-backup-restore.md`'deki şifreli off-site yedeğin
  **yerine geçmez**, üstüne ek katmandır.

### 5.3 Tek replica şartı

Arka plan servisleri süreç içi çalışıyor; `NotificationDispatcher` `FOR UPDATE SKIP LOCKED` ile
çoklu örneğe dayanıklı, ama `BackupService` (süreç içi `_runLock`) ve aylık üretici gibi işler
dağıtık kilit kullanmıyor. **`numReplicas` 1'de kalmalı.** Ölçek ihtiyacı doğarsa (bu okul
ölçeğinde beklenmiyor) önce işçi/web ayrımı tasarlanır (`docs/17` §9).

### 5.4 Deploy sırasında kısa kesinti

Railway yeni sürümü healthcheck geçince devreye alır, eskiyi sonra durdurur; kısa bir süre **iki
API örneği aynı anda** çalışabilir. Migration açılışta tek örnekte koşar (yeni örnek), eski örnek
bu sırada eski şemayla çalışmaya devam eder — geriye uyumsuz migration'larda (`abdera-migration`
skill'inin kontrol listesi) bu pencere dikkate alınmalı. Çakışan arka plan işi riski: bir-iki
dakikalık pencerede iki `BackupService`; yedek idempotent ve günlük olduğu için kabul edilebilir.

### 5.5 Maliyet (yaklaşık, doğrulanmalı)

4 servis, düşük trafik: api (~300–500 MB RAM), web (~150 MB), Postgres (~200 MB + disk),
gateway (~30 MB). Kullanım bazlı faturalama ile Hobby'de aylık ~$5–15, Pro'da plan ücreti + kullanım.
Kesin rakam için ilk haftanın Railway usage ekranına bakılır.

## 6. Veri taşıma (Supabase → Railway Postgres)

Supabase'de uygulamanın tüm tabloları `public` şemasında (`__EFMigrationsHistory` ve Data
Protection anahtar tablosu dahil). Supabase'in kendi şemaları (`auth`, `storage`, `realtime`…)
**taşınmaz**.

1. **Sürüm kontrolü:** Supabase sunucu sürümünü not et (`select version();`). Dump'ı, kaynak
   sunucuyla aynı veya daha yeni major sürümlü `pg_dump` ile al.
2. **Prova (geçiş gününden önce):**
   ```bash
   pg_dump "$SUPABASE_DIRECT_URL" --schema=public --no-owner --no-privileges \
     --format=custom --file=abdera-prova.dump
   # Railway Postgres'in public TCP proxy'si geçici açık:
   pg_restore --no-owner --no-privileges --dbname "$RAILWAY_PUBLIC_URL" abdera-prova.dump
   ```
   Supabase'in **doğrudan** (5432, pooler olmayan) bağlantı dizesi kullanılmalı; pooler üzerinden
   `pg_dump` sorun çıkarır.
3. **Doğrulama:** `docs/16-backup-restore.md` §4'teki satır sayısı + tutarlılık sorguları iki
   tarafta koşulur, sonuçlar birebir eşleşmeli. `__EFMigrationsHistory` son satırı repo'daki son
   migration'la aynı olmalı (aksi halde API açılışta eksik migration'ı uygular — beklenen davranış,
   ama provada görülmeli).
4. **Staging'de uçtan uca:** Railway'de API/web/gateway prova DB'sine karşı açılır, *.up.railway.app
   üzerinden admin, öğretmen ve veli girişi + bir aidat ekranı elle denenir; CI'daki Playwright
   smoke'u (`frontend/e2e`) bu adrese karşı koşturulabilir.

## 7. Geçiş günü (cut-over)

Hedef kesinti: ~15–30 dk. Okulun en sakin saatinde (ör. pazar sabahı; sessiz saat penceresi
içinde, WhatsApp gönderimi zaten ertelenmiş olur).

1. **T-1 gün:** Özel domain'in DNS TTL'ini 300 sn'ye indir. Railway servisleri, değişkenleri
   ve prova DB'si hazır; gateway `*.up.railway.app` üzerinden yeşil.
2. **Dondur:** Vercel'de API'yi bakım moduna al (en basit yol: Vercel'deki API servisine
   geçersiz bir `ConnectionStrings__Default` verip redeploy — yazma durur; ya da yöneticilere
   "şu saatler arası işlem yapmayın" duyurusu). Vercel Cron'larını devre dışı bırak.
3. **Son dump/restore:** §6 adım 2'yi **temiz** (drop/recreate edilmiş) Railway DB'sine tekrarla.
   §6 adım 3 doğrulaması.
4. **API'yi başlat:** Railway `api` redeploy → migration'lar (varsa) uygulanır, `/health` Healthy.
   Public TCP proxy'yi kapat.
5. **Domain:** Özel domain'i Railway `gateway`'e ekle, DNS'te CNAME'i Railway'in verdiği hedefe
   çevir; Railway sertifikayı otomatik alır. `api`'de `Frontend__Origin=https://<özel-domain>`,
   redeploy.
6. **Entegrasyon uç adresleri:** Meta WhatsApp webhook URL'i özel domain kullanıyorsa değişmez;
   Vercel adresini (`abdera-web-nine.vercel.app`) kullanıyorsa Meta panelinde
   `https://<özel-domain>/api/...` olarak güncellenir ve doğrulama (verify token) yeniden yapılır.
7. **Duman testi:** `/health` 200, `/login` 200; admin + öğretmen + veli girişi; bir ödeme kaydı
   ekranı açılıyor (yazmadan); bekleyen bir `NotificationJob`'un dağıtıcı tarafından işlendiği
   logda görülüyor; ertesi sabah `BackupService`'in yedeği SFTP'ye yazdığı teyit ediliyor.
8. **Oturumlar:** Data Protection anahtarları DB ile taşındığı için kullanıcılar oturumdan
   düşmemeli. Düşerlerse yalnızca yeniden giriş gerekir — veri etkisi yok.

## 8. Geri dönüş

- Vercel + Supabase **en az 2 hafta** dokunulmadan (yalnızca durdurulmuş halde) tutulur.
- Geçişten sonraki ilk saatlerde sorun çıkarsa: DNS'i geri çevir, Vercel API'sinin bağlantı
  dizesini düzelt. **Dikkat:** Railway'de bu arada yazılan veri Supabase'de yoktur — geri dönüşten
  önce Railway'den `pg_dump` alınıp Supabase'e ters yönde yüklenmeli. Bu yüzden geri dönüş kararı
  ne kadar erken verilirse o kadar ucuz.
- 2 hafta sorunsuz geçince: Vercel projesi kapatılır, Supabase'den son bir arşiv dump'ı alınıp
  proje silinir, ardından Vercel'e özgü kod/konfigürasyon temizliği PR'ı açılır
  (`vercel.json`, `.vercelignore`, `Dockerfile.vercel`; `Runtime:Serverless` yolları ve cron uçları
  kalıcı container'da da zararsız olduğundan kalmaları tartışılabilir).

## 9. İş listesi (sıralı)

1. [ ] §5.1 e-posta kararı (Pro plan mı, HTTP e-posta sağlayıcısı mı).
2. [ ] Repo PR'ı: gateway Dockerfile + Caddyfile, `railway.json`'lar, doküman/skill güncellemeleri.
3. [ ] Railway projesi + 4 servis + değişkenler (§4); Postgres 16 sabitlemesi.
4. [ ] SFTP kimlik bilgisi yöntemi (parola ya da env'den key dosyası).
5. [ ] Supabase → Railway prova taşıma + doğrulama (§6).
6. [ ] Staging duman testi / Playwright smoke.
7. [ ] Geçiş günü (§7).
8. [ ] 2 hafta izleme → Vercel/Supabase kapatma + temizlik PR'ı (§8).
