# Hetzner Cloud Kurulum Runbook'u

Durum: **Runbook — henüz uygulanmadı.** Abdera'yı tek bir Hetzner Cloud sunucusunda, repodaki
hazır `docker compose --profile prod` kurulumuyla (db + api + web + Caddy) yayına alır ve bugünkü
Vercel + Supabase yayınından veriyi taşır. Railway alternatifi: `docs/18-railway-migration.md`.

Uygulama kodunda değişiklik gerekmez. Altyapı dosyaları (deploy betiği, deploy workflow'u,
`backup-gw` servisi) tek bir PR ile eklenir (§13.2, §7.2). Tüm komutlar sırayla,
kopyala-yapıştır çalıştırılabilir; `<...>` ile işaretli yerler doldurulur.

## Genel sıra

| # | Adım | Kim | Nerede | Süre | Bölüm |
|---|---|---|---|---|---|
| 1 | Alan adını al, DNS'i Cloudflare'e bağla | Okul | Tarayıcı | 30 dk | §0 |
| 2 | Hetzner hesabı aç (kimlik onayı saatler sürebilir, ilk bu başlasın) | Okul | Tarayıcı | 15 dk + bekleme | §0 |
| 3 | R2 bucket + Bucket Lock + lifecycle + API token | Okul | Cloudflare | 15 dk | §7.1 |
| 4 | Sırları topla: Vercel ortam değişkenleri, Supabase doğrudan bağlantı dizesi, e-posta SMTP bilgisi → parola yöneticisi | Okul | Tarayıcı | 20 dk | §0 |
| 5 | Altyapı PR'ı: deploy betiği, `deploy.yml`, `backup-gw`, karar kaydı → merge | Claude | Repo | — | §13.2, §7.2 |
| 6 | Sunucu: oluştur, sertleştir, Docker, kod, `.env` | Okul (+ Claude destek) | Hetzner + SSH | 1–2 saat | §1–§5 |
| 7 | DNS kaydı + boş DB ile ilk açılış + giriş testi | Okul | SSH | 20 dk | §6 |
| 8 | Yedek testi: şimdi yedekle, kilit, hata alarmı, geri yükleme provası | Okul | Uygulama + SSH | 45 dk | §7.3 |
| 9 | GitHub `production` environment + secret'lar + deneme deploy | Okul | GitHub | 20 dk | §13.3 |
| 10 | Veri taşıma provası (Supabase → Hetzner) + sayım karşılaştırması | Okul | SSH | 1 saat | §8 |
| 11 | **Geçiş günü**: dondur, son taşıma, DNS, WhatsApp webhook, duman testi | Okul | Hepsi | 30–60 dk | §9 |
| 12 | 2 hafta izleme → Vercel/Supabase kapatma, skill ve dokümanları güncelleme | Okul + Claude | — | — | §12 |

1–4 birbirinden bağımsız ve paralel yapılabilir. 6'dan sonrası sırayla ilerler. Geçiş günü
(11), 8 ve 10 başarıyla bitmeden planlanmaz.

## 0. Önkoşullar

- Hetzner Cloud hesabı ve bir proje (`abdera`).
- Alan adı; DNS'i Cloudflare'de (yedek için R2 zaten Cloudflare'de). Örneklerde `panel.okulum.com`.
- Cloudflare hesabı, R2 etkin (§7.1).
- E-posta alarmı için SMTP bilgisi: alan adının posta hesabı ya da Gmail uygulama şifresi (port 587).
- Yerel makinede bir SSH anahtarı (`ssh-keygen -t ed25519`).
- Bugünkü Vercel projesinin ortam değişkenleri (WhatsApp, e-posta, yedek anahtarı vb.).
- Supabase'in **doğrudan** bağlantı dizesi (pooler değil, port 5432).

## 1. Sunucuyu oluştur

Hetzner Console → **Add Server**:

| Alan | Değer |
|---|---|
| Location | **Nuremberg** veya **Falkenstein** (Türkiye'ye Helsinki'den yakın) |
| Image | **Ubuntu 24.04** |
| Type | **CX33** (4 vCPU / 8 GB / 80 GB). İmajlar sunucuda derleneceği için CX23 yerine CX33. |
| Networking | IPv4 + IPv6 |
| SSH keys | Yerel public key'ini ekle (parola ile girişi hiç açma) |
| Backups | **Aç** (+%20; 7 günlük otomatik sunucu imajı) |
| Firewall | Aşağıdaki kuralla yeni bir firewall: `abdera-fw` |
| Name | `abdera-prod` |

**Firewall `abdera-fw` — Inbound:**

| Protokol | Port | Kaynak |
|---|---|---|
| TCP | 22 | Yalnızca kendi IP'n (mümkünse); değilse Any |
| TCP | 80 | Any (Let's Encrypt doğrulaması + HTTPS'e yönlendirme) |
| TCP | 443 | Any |
| UDP | 443 | Any (HTTP/3, opsiyonel) |

Outbound kısıtlanmaz. Postgres (5432), API (8080) ve web (3000) zaten compose'ta `127.0.0.1`'e
bağlı; firewall ikinci savunma katmanı. Docker, `ufw` kurallarını atlattığı için sunucu içi `ufw`
yerine **Hetzner Cloud Firewall** kullanıyoruz.

Sunucunun IPv4 adresini not et: `<SUNUCU_IP>`.

## 2. İlk giriş ve sertleştirme

```bash
ssh root@<SUNUCU_IP>

# Güncellemeler + otomatik güvenlik yamaları
apt update && apt -y full-upgrade
apt -y install unattended-upgrades fail2ban git
dpkg-reconfigure -plow unattended-upgrades   # "Yes"

# Saat dilimi (loglar okunur olsun; uygulama zaten SCHOOL_TIMEZONE kullanıyor, DB UTC)
timedatectl set-timezone Europe/Istanbul

# Yönetici kullanıcı
adduser --disabled-password --gecos "" deploy
usermod -aG sudo deploy
echo "deploy ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/deploy
mkdir -p /home/deploy/.ssh && cp /root/.ssh/authorized_keys /home/deploy/.ssh/
chown -R deploy:deploy /home/deploy/.ssh && chmod 700 /home/deploy/.ssh

# SSH: yalnızca anahtar, root girişi kapalı
cat > /etc/ssh/sshd_config.d/99-abdera.conf <<'EOF'
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
EOF
systemctl restart ssh
```

**Yeni bir terminalde** `ssh deploy@<SUNUCU_IP>` ile girebildiğini doğrulamadan mevcut oturumu
kapatma. Bundan sonraki adımlar `deploy` kullanıcısıyla.

## 3. Docker

```bash
# Resmi Docker deposu (Ubuntu'nun docker.io paketi değil - compose eklentisi eski kalıyor)
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list
sudo apt update
sudo apt -y install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker deploy   # oturumu kapatıp yeniden aç

# Container logları varsayılan olarak SINIRSIZ büyür ve diski doldurur - sınırla.
sudo tee /etc/docker/daemon.json <<'EOF'
{ "log-driver": "json-file", "log-opts": { "max-size": "20m", "max-file": "5" } }
EOF
sudo systemctl restart docker
docker compose version
```

## 4. Kodu çek

```bash
sudo mkdir -p /opt/abdera && sudo chown deploy:deploy /opt/abdera
git clone https://github.com/mennansevim/abdera.git /opt/abdera
cd /opt/abdera && git checkout main
```

## 5. `.env` dosyası

```bash
cd /opt/abdera
cp .env.example .env
chmod 600 .env
```

Aşağıdaki değerleri `.env` içinde düzenle. Listede olmayan satırlar `.env.example`
varsayılanında kalabilir.

```ini
# --- Ortam ---
ASPNETCORE_ENVIRONMENT=Production
SCHOOL_TIMEZONE=Europe/Istanbul

# --- Veritabanı ---
# Parola bağlantı dizesine gömülüyor: ; ' " boşluk içermemeli. Hex güvenli:
#   openssl rand -hex 24
POSTGRES_PASSWORD=<openssl rand -hex 24 çıktısı>
POSTGRES_HOST=db

# --- Oturum ---
Auth__KeysDirectory=/app/keys          # compose kalıcı abdera_dpkeys volume'üne bağlar
Auth__PersistKeysToDatabase=false
Auth__DevLogin__Enabled=false
Runtime__Serverless=false
Demo__Enabled=false
Database__AutoMigrate=true

# İlk yönetici YALNIZCA boş veritabanında oluşturulur. Supabase'den veri taşınacaksa
# (§8) mevcut kullanıcılar gelir, bu değerler kullanılmaz - ama Production guard
# placeholder'ı yine reddeder: "password", "example", "<" içermeyen gerçek bir değer ver.
Bootstrap__AdminEmail=<yonetici@okulum.com>
Bootstrap__AdminPassword=<güçlü, benzersiz>

# --- Alan adı / CORS / TLS ---
FRONTEND_ORIGIN=https://panel.okulum.com
PUBLIC_DOMAIN=panel.okulum.com
ACME_EMAIL=<sertifika-uyarilari@okulum.com>
# NEXT_PUBLIC_API_BASE_URL'e dokunma: localhost değeri, gerçek alan adında açılan sayfada
# frontend tarafından otomatik olarak aynı-origin /api'ye çevrilir (frontend/src/lib/api.ts).

# --- WhatsApp ---
WhatsApp__Provider=Cloud                # hazır değilse Disabled
WhatsApp__PhoneNumberId=...             # Vercel'deki değerlerin aynısı
WhatsApp__AccessToken=...
WhatsApp__WebhookVerifyToken=...
WhatsApp__AppSecret=...
WhatsApp__PayloadSigningKey=...         # DEĞİŞTİRME: eski RSVP butonları geçersiz kalır

# --- Banka ---
Banking__Provider=Manual

# --- Yedek (Cloudflare R2, ayrıntı §7.3) ---
Backup__Provider=Sftp
Backup__EncryptionKey=...               # Vercel'deki MEVCUT anahtar - yoksa: openssl rand -base64 32
# R2_*, BACKUP_GW_PASSWORD ve Backup__Sftp__* satırları §7.3'te

# --- E-posta alarmı (Hetzner 25 ve 465'i kapatır, 587 açıktır) ---
Email__Provider=Smtp
Email__Smtp__Host=smtp.gmail.com
Email__Smtp__Port=587
Email__Smtp__Username=...
Email__Smtp__Password=...               # Gmail uygulama şifresi
Email__Smtp__FromAddress=...
Email__Smtp__UseSsl=true
Ops__AlertRecipients=<admin@okulum.com>
```

`ProductionSecretsGuard` eksik veya placeholder bir değer görürse API açılmaz ve logda hangi
değişkenin eksik olduğunu yazar (§6'daki `docker compose logs api`).

## 6. DNS ve ilk açılış (boş veritabanıyla prova)

1. DNS'e `A panel.okulum.com → <SUNUCU_IP>` (ve varsa `AAAA` → IPv6) ekle. Cloudflare
   kullanıyorsan kayıt **DNS only (gri bulut)** olsun; proxy açılacaksa §11'e bak.
   `dig +short panel.okulum.com` sunucu IP'sini döndürene kadar bekle.
2. Derle ve başlat (ilk derleme ~5–10 dk):
   ```bash
   cd /opt/abdera
   docker compose --profile prod up -d --build
   docker compose ps                  # db, api, web healthy; caddy running
   docker compose logs -f api         # "Now listening on" + migration logları
   docker compose logs caddy | grep -i certificate   # sertifika alındı mı
   ```
3. Kontrol:
   ```bash
   curl -s https://panel.okulum.com/health        # {"status":"Healthy",...}
   curl -sI https://panel.okulum.com/login | head -1
   ```
4. Tarayıcıdan `Bootstrap__AdminEmail` ile giriş yap. Çalışıyorsa altyapı hazır.

Veri taşınacaksa (§8), bu prova veritabanı orada silinip yeniden oluşturulacak.

## 7. Yedekleme hedefi: Cloudflare R2

Karar: uygulamanın şifreli günlük yedeği **Cloudflare R2**'ye gider. 10 GB'a kadar ücretsiz.
Bucket Lock sayesinde sunucu ya da erişim anahtarı ele geçirilse bile yedekler 60 gün boyunca
silinemez. Uygulama kodu değişmez. Uygulama yalnızca SFTP'ye yazabildiği için araya sunucuda,
yalnızca Docker iç ağında çalışan bir `rclone serve sftp` servisi (`backup-gw`) girer:

```
api ──SFTP (iç ağ, :2022)──► backup-gw (rclone) ──HTTPS──► R2 bucket "abdera-backups"
                                                             ├─ Bucket Lock: 60 gün silinemez
                                                             └─ Lifecycle: 65. günde silinir
```

(Hetzner Storage Box alternatifi bu belgenin git geçmişinde duruyor: `git log -p docs/19-hetzner-runbook.md`.)

### 7.1 Cloudflare tarafı (tarayıcı)

1. Cloudflare Dashboard → **R2 Object Storage** → planı etkinleştir. Kart ister; 10 GB altında
   ücret çıkmaz.
2. **Create bucket** → `abdera-backups`, konum ipucu **Eastern Europe (EEUR)**.
3. Bucket → **Settings**:
   - **Bucket lock rules → Add rule**: prefix `abdera/`, **60 gün**. Kilitli dosya API
     anahtarıyla (sunucudaki anahtar dahil) silinemez. Kural bucket ayarı olduğu için
     yalnızca Cloudflare hesabından kaldırılabilir. Bu yüzden Cloudflare hesabında
     **iki adımlı doğrulama açık olmalı**; korumanın son halkası hesabın kendisi.
   - **Object lifecycle rules → Add rule**: prefix `abdera/`, **65 gün sonra sil**. Kilit
     lifecycle'dan önce gelir, silme 60 günden önce olmaz.
   - Saklama süresi KVKK açısından da bir karardır: kalıcı silinen bir öğrencinin verisi
     (`PersonEraser`) şifreli yedeklerde en fazla 65 gün daha durur. Aydınlatma metninde
     yedek saklama süresi belirtilmeli.
4. R2 → **Manage API tokens → Create API token**:
   - İzin: **Object Read & Write**, yalnızca `abdera-backups` bucket'ı
   - Çıkan **Access Key ID**, **Secret Access Key** ve hesap sayfasındaki **Account ID**'yi
     parola yöneticisine kaydet. Secret bir daha gösterilmez.

### 7.2 `backup-gw` servisi

`docker-compose.yml`'de, yalnızca `prod` profilinde: `rclone/rclone:1.75.1` sabit sürüm,
`serve sftp r2:<bucket>`, port publish yok (yalnızca compose iç ağından `backup-gw:2022`).
`--vfs-cache-mode=off` bilinçli: dosya kapanırken R2'ye yükleme bitmiş olur ve R2 hatası
`BackupService`'e döner. Cache açık olsaydı yükleme arka planda sürer, hata uygulamaya hiç
ulaşmaz ve yedek "Succeeded" görünürdü. R2 anahtarları `.env`'deki `R2_*` değişkenlerinden
gelir. Karar kaydı: `docs/10-decisions.md` P2.

Yerel doğrulama (rclone 1.75.1, 2026-09-28): bucket tabanlı bir arka uçta
`stat`→`mkdir`→`put`→`listdir` akışı uygulamanın SFTP adımlarıyla aynı sırada çalıştı.
Erişilemeyen bir S3 uç noktasında hata SFTP istemcisine döndü. Gerçek R2 ve uygulamanın
SSH.NET istemcisiyle doğrulama §7.3'teki testlerle yapılır.

### 7.3 Sunucu tarafı

1. `.env`'e ekle (§5'teki yedek bloğu buna göre):
   ```ini
   R2_ACCOUNT_ID=<account id>
   R2_ACCESS_KEY_ID=<access key id>
   R2_SECRET_ACCESS_KEY=<secret>
   R2_BUCKET=abdera-backups
   BACKUP_GW_PASSWORD=<openssl rand -hex 24>

   Backup__Provider=Sftp
   Backup__Sftp__Host=backup-gw
   Backup__Sftp__Port=2022
   Backup__Sftp__Username=abdera
   Backup__Sftp__Password=<BACKUP_GW_PASSWORD ile aynı>
   Backup__Sftp__PrivateKeyPath=
   Backup__Sftp__RemoteDirectory=/abdera
   # Eski yedekleri R2 lifecycle siler. Uygulama kilitli bir dosyayı silmeye çalışırsa
   # her gece "yedek başarısız" alarmı üretirdi; uygulamanın kendi silmesi kapatılır.
   Backup__RetentionDays=36500
   ```
2. `docker compose --profile prod up -d` → `docker compose ps` içinde `backup-gw` running.
3. Uygulamada **Yedekler** ekranı (`/dashboard/backups`) → **Şimdi yedekle**. Kayıt
   `Succeeded` olmalı. R2 → `abdera-backups` → `abdera/` altında `abdera-YYYYMMDD-HHmmss.sql.enc`
   görünmeli.
4. **Kilit testi:** R2 panelinden bu dosyayı silmeyi dene. Reddedilmeli.
5. **Hata testi:** `docker compose stop backup-gw` → Şimdi yedekle → kayıt `Failed` olmalı ve
   `Ops__AlertRecipients`'a e-posta gelmeli. Sonra `docker compose start backup-gw`.
6. **Geri yükleme provası** (`docs/16-backup-restore.md`): dosyayı R2 panelinden indir, şifresini
   çöz, boş bir veritabanına yükle, sayıları karşılaştır. Bu prova yapılmadan yedek kurulumu
   tamamlanmış sayılmaz.

**Yedek katmanları (özet):**

| Katman | Ne | Sıklık / saklama | Neye karşı |
|---|---|---|---|
| 1. Uygulama yedeği → R2 | `pg_dump` → AES-256 → R2 (Bucket Lock) | Günlük 03:00, 60 gün kilitli, 65. gün silinir | Veri silme/bozulma, sunucu ele geçirilmesi; hata olursa e-posta + audit |
| 2. Hetzner Backups | Sunucu disk imajı | Günlük, 7 adet | Sunucunun tamamen kaybı (`.env`, anahtarlar). DB için tek başına güvenilmez, çalışan Postgres'in anlık kopyası |
| 3. Deploy öncesi döküm | `pg_dump` (§13) | Her deploy'da, son 5 | Hatalı bir sürümün veriyi bozması |

Veri kaybı penceresi en fazla ~24 saat (son gece yedeğinden bu yana girilen kayıtlar). Bu
kabul edilemezse sonraki adım `pgBackRest` ile sürekli WAL arşivi (R2'ye S3 olarak) olur.
Ayrı bir karar gerektirir.

**Otomatik izleme:** Ayrı bir cron gerekmez. Yedek başarısız olursa (R2'ye yazılamaması dahil)
uygulama `Ops__AlertRecipients`'a e-posta atar ve `audit_log`'a `backup.failed` yazar. Hiç
yedek alınmazsa (ör. API kapalıysa) `SystemHealthMonitor` son başarılı yedek 30 saati geçince
uyarır, 48 saatte "unhealthy" der. API tamamen düşmüşse bu alarm da gidemez; onu §10'daki dış
`/health` kontrolü yakalar.

**Veritabanı dökümünde OLMAYANLAR:** `/opt/abdera/.env` ve `abdera_dpkeys` volume'ü (oturum
anahtarları). `.env`'in bir kopyası parola yöneticisinde dursun. Oturum anahtarı kaybolursa
herkes bir kez yeniden giriş yapar.

`Backup__EncryptionKey` ve R2 anahtarlarını parola yöneticisinde sakla. Sunucu kaybolursa
yedekler şifreleme anahtarı olmadan açılamaz.

## 8. Veri taşıma (Supabase → Hetzner)

Önce bir kez **prova** yap (akış aynı), sonra geçiş gününde (§9) tekrarla.

1. **Supabase sürümünü öğren:**
   ```bash
   docker run --rm postgres:16-alpine psql "<SUPABASE_DIRECT_URL>" -Atc "show server_version;"
   ```
2. **Dump al**. `pg_dump` sürümü, Supabase sunucusuyla aynı veya daha yeni olmalı. Etiketi
   buna göre seç: 15 → `postgres:16`, 17 → `postgres:17`. Hedef sunucu 16 olduğu için
   **düz SQL** formatı kullanılır (custom format yeni→eski sürüme geri yüklenemez).
   ```bash
   cd ~
   docker run --rm -v "$PWD:/out" postgres:<SUPABASE_MAJOR>-alpine \
     pg_dump "<SUPABASE_DIRECT_URL>" --schema=public --no-owner --no-privileges \
     --format=plain --file=/out/supabase.sql
   # Kaynak 17 ise PG16'nın tanımadığı ayarı temizle:
   sed -i '/^SET transaction_timeout/d' supabase.sql
   ```
   Supabase'in kendi şemaları (`auth`, `storage`, `realtime`…) bilinçli olarak alınmaz.
   Uygulamanın tüm tabloları ve `__EFMigrationsHistory` `public` içinde.
3. **Hedefi sıfırla ve yükle.** API'yi durdur ki boş şemaya kendi migration'larını yazmasın:
   ```bash
   cd /opt/abdera
   docker compose stop api web caddy
   docker compose exec -T db sh -c 'dropdb -U "$POSTGRES_USER" --force "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
   docker compose exec -T db sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < ~/supabase.sql
   ```
   Hata verirse (genelde Supabase'e özgü bir `ALTER ... OWNER`, `GRANT` ya da extension
   satırı) ilgili satırı dosyadan çıkarıp tekrar dene.
4. **Doğrula:** `docs/16-backup-restore.md` §4'teki sayım ve tutarlılık sorgularını hem
   Supabase'de hem burada çalıştır; sayılar birebir aynı olmalı.
5. **Başlat:**
   ```bash
   docker compose --profile prod up -d
   docker compose logs api | grep -i migrat    # eksik migration varsa uygulanır
   ```
6. `rm ~/supabase.sql` (kişisel veri içeriyor, sunucuda bırakma).

**Oturumlar:** Supabase'deki oturum anahtarları veritabanında, burada ise dosya volume'ünde
tutuluyor. Taşıma sonrası herkes **bir kez yeniden giriş yapar**; veri etkilenmez.

## 9. Geçiş günü (cut-over)

Okulun en sakin saatini seç (ör. pazar sabahı). Hedef kesinti 15–30 dk.

1. **T-1 gün:** Mevcut alan adının DNS TTL'ini 300 sn'ye indir. §1–§7 tamamlanmış, §8
   provası başarılı olmalı.
2. **Dondur:** Yöneticilere duyuru yap. Vercel'de cron'ları kapat ve API'yi durdur, örneğin
   geçersiz bir `ConnectionStrings__Default` ile redeploy. Bundan sonra Supabase'e yazma
   olmamalı.
3. **§8 adım 2–5**'i gerçek veriyle çalıştır.
4. **DNS:** Canlı alan adını `<SUNUCU_IP>`'ye çevir. `.env`'de `FRONTEND_ORIGIN` ve
   `PUBLIC_DOMAIN` bu alan adı olmalı; değiştiysen `docker compose --profile prod up -d`.
5. **WhatsApp:** Meta panelinde webhook URL'i Vercel adresini
   (`abdera-web-nine.vercel.app`) gösteriyorsa `https://<alan-adı>/api/...` olarak güncelle
   ve doğrulamayı yeniden yap.
6. **Duman testi:**
   - [ ] `/health` Healthy, `/login` 200
   - [ ] Admin, öğretmen ve veli girişi
   - [ ] Aidat ve takvim ekranları açılıyor, kayıt sayıları beklenen gibi
   - [ ] `docker compose logs api | grep -i notification`: dağıtıcı çalışıyor
   - [ ] "Şimdi yedekle" → R2'de yeni dosya
   - [ ] Sistem sağlığı ekranı yeşil; bir test alarm e-postası gidiyor

## 10. Günlük işletim

**Güncelleme (deploy):** Normal yol §13'teki GitHub Actions akışı. Actions çalışmıyorsa
aynı betik sunucuda elle çağrılır. Betik deploy öncesi dökümü ve sağlık kontrolünü kendisi
yapar:
```bash
abdera-deploy "$(git ls-remote https://github.com/mennansevim/abdera.git refs/heads/main | cut -f1)"
```
§13 kurulmadan önceki ilk deploy'larda:
```bash
cd /opt/abdera && git pull --ff-only origin main
docker compose --profile prod up -d --build && docker image prune -f
```

**Kod geri alma:** `git checkout <önceki-commit> && docker compose --profile prod up -d --build`.
Migration uygulanmışsa şema geri gelmez; o durumda §10'daki döküm ya da uygulama yedeği
kullanılır (`docs/16-backup-restore.md`).

**İzleme:**
- Uygulamanın kendi alarmları: `Ops__AlertRecipients` (DB erişilemez, yedek eski veya başarısız).
- Dışarıdan erişilebilirlik: UptimeRobot veya Better Stack'in ücretsiz planıyla
  `https://<alan-adı>/health` için 5 dakikalık kontrol ve e-posta alarmı. Sunucu tamamen
  düşerse uygulama kendi alarmını gönderemez; bu yüzden dış kontrol gerekli.
- Disk: `df -h /` ve `docker system df`, ayda bir.

**Ayda bir:** `sudo apt update && sudo apt -y full-upgrade`; çekirdek güncellendiyse
`sudo reboot`. Container'lar `restart: unless-stopped` ile kendiliğinden kalkar.

**Üç ayda bir:** `docs/16-backup-restore.md` geri yükleme provası.

## 11. Sonra: statik site ve Cloudflare

- **Statik site** (ör. `okulum.com`): Cloudflare Pages'te ücretsiz barındır, bu sunucuya
  dokunmaz. Aynı sunucuda sunulacaksa `Caddyfile`'a ayrı bir site bloğu eklenir.
- **Cloudflare proxy (turuncu bulut)** `panel.` alt alan adında açılacaksa önce iki değişiklik
  gerekir:
  1. SSL/TLS modu **Full (strict)** olmalı.
  2. `Caddyfile`'da Cloudflare IP aralıkları `trusted_proxies` olarak tanımlanmalı ve istemci
     IP'si `CF-Connecting-IP` başlığından alınmalı. Aksi halde API tüm istekleri Cloudflare
     IP'lerinden geliyor görür ve giriş denemesi sınırı herkesi aynı kovaya koyar.

  Bu değişiklik ayrı bir PR'dır. Yapılana kadar kayıt gri bulutta kalmalı.

## 12. Geri dönüş ve kapanış

- Vercel ve Supabase **en az 2 hafta** durdurulmuş halde, silinmeden tutulur. İlk saatlerde
  geri dönmek için DNS'i eski adrese çevirmek yeter. Ama bu arada Hetzner'e yazılan veri
  Supabase'de yoktur; geri dönmeden önce buradan alınan döküm oraya yüklenmelidir.
- 2 hafta sorunsuz geçince:
  - Supabase'den son bir arşiv dökümü alınıp proje silinir, Vercel projesi kapatılır.
  - `.claude/skills/abdera-deploy` Vercel yerine §13'teki "Deploy" workflow'unu takip edecek
    şekilde güncellenir.
  - Vercel'in GitHub entegrasyonu kaldırılır. Aksi halde `main`'e her push Vercel'e de deploy
    eder.
  - `docs/17-technical-architecture.md` §7 güncellenir ve `docs/10-decisions.md`'ye barındırma
    kararı yazılır.

## 13. GitHub Actions ile otomatik deploy

Hedef: `main`'e push → CI (`.github/workflows/ci.yml`) yeşil → sunucu o commit'e otomatik
güncellenir → canlı kontrol. CI kırmızıysa deploy **hiç başlamaz**. Aynı anda iki deploy
koşmaz. Sağlık kontrolü geçmezse sunucu önceki commit'e kendiliğinden döner.

```
push main ──► CI (test + build + e2e) ──yeşil──► Deploy workflow ──SSH──► abdera-deploy <sha>
                                                                            ├─ deploy öncesi pg_dump
                                                                            ├─ git checkout <sha>
                                                                            ├─ compose up --build
                                                                            ├─ /health bekle (3 dk)
                                                                            └─ başarısızsa önceki commit
```

### 13.1 Tasarım kararları

- **İmajlar sunucuda derlenir** (bugünkü compose akışı, kod değişikliği yok). CX33'te derleme
  birkaç dakika sürer. İleride imajlar Actions'ta derlenip GHCR'ye itilebilir, sunucu yalnızca
  çeker. Daha hızlı olur ve test edilen imajın aynısı canlıya çıkar, ama compose'da `image:`
  değişikliği gerektirir. Ayrı iş.
- **SSH anahtarı yalnızca tek bir komutu çalıştırabilir.** `authorized_keys`'te
  `command=` + `restrict` ile zorlanır. Anahtar sızsa bile kabuk açılamaz; yalnızca `main`'de
  zaten var olan bir commit deploy edilebilir.
- **Betik repodan değil `/usr/local/bin`'den çalışır.** Betik repodaki kopyasından çalışsaydı,
  `git checkout` onu koşarken değiştirebilirdi (bash betikleri satır satır okur).
- **Deploy öncesi döküm zorunlu.** `pg_dump` başarısız olursa deploy durur. Son 5 döküm
  `~/predeploy/` altında tutulur.
- **Geri dönüş yalnızca kodu kapsar.** Yeni sürüm bir migration uyguladıysa şema geri gelmez.
  Eklemeli migration'larda (yeni kolon/tablo) eski kod çalışmaya devam eder. Yıkıcı bir
  migration'da `~/predeploy/` dökümünden elle geri yükleme gerekir (`abdera-migration`
  skill'inin geri alınabilirlik kontrolü bu yüzden önemli).
- **Port 22 herkese açık kalmalı.** GitHub runner'larının IP aralığı çok geniş, §1'deki
  "yalnızca kendi IP'n" kısıtı bu akışla uyumsuz. Koruma: yalnızca anahtarla giriş (§2),
  fail2ban ve zorlanmış komut.
- **Onay adımı (opsiyonel).** GitHub Environment `production`'a "Required reviewers"
  eklenirse her deploy telefondan tek tıkla onaylanmadan başlamaz. Başlangıçta açık tutmak
  iyi olur; alışınca kapatılabilir.

### 13.2 Repodaki dosyalar

- `deploy/hetzner/abdera-deploy`: sunucuda `/usr/local/bin/abdera-deploy` olarak kurulan
  betik (zorlanmış komut).
- `.github/workflows/deploy.yml`: CI yeşil → SSH → betik → canlı kontrol. Repo değişkeni
  `DEPLOY_ENABLED=true` olmadan hiçbir şey yapmaz; sunucu hazır olmadan merge edilmesi
  güvenli.
- `docker-compose.yml` içindeki `backup-gw` servisi (§7).

### 13.3 Bir kerelik kurulum

1. **Deploy anahtarı** (kendi bilgisayarında; sunucuda bırakma):
   ```bash
   ssh-keygen -t ed25519 -N "" -f abdera-actions -C github-actions-deploy
   ```
2. **Sunucuda** (PR merge edildikten, `/opt/abdera` güncellendikten sonra):
   ```bash
   sudo install -m 755 /opt/abdera/deploy/hetzner/abdera-deploy /usr/local/bin/abdera-deploy
   # abdera-actions.pub içeriğini zorlanmış komutla ekle:
   echo 'command="/usr/local/bin/abdera-deploy",restrict <abdera-actions.pub içeriği>' >> ~/.ssh/authorized_keys
   ```
   Betik repoda değişirse bu `install` satırı yeniden çalıştırılır. Betik kendini
   güncellemez; bu bilinçli bir tercih.
3. **GitHub** → repo *Settings → Environments → New environment* → `production`:
   - *Deployment branches*: yalnızca `main`
   - *Required reviewers*: kendin (opsiyonel, §13.1)
   - *Environment secrets*:
     - `DEPLOY_HOST` = sunucu IP'si
     - `DEPLOY_SSH_KEY` = `abdera-actions` (private key) dosyasının tamamı
     - `DEPLOY_KNOWN_HOSTS` = `ssh-keyscan -t ed25519 <SUNUCU_IP>` çıktısı. Sunucunun kimliği
       sabitlenir, araya giren biri anahtarı kullanamaz.
4. **Aç:** repo *Settings → Secrets and variables → Actions → Variables* →
   `DEPLOY_ENABLED` = `true` ve `DEPLOY_URL` = `https://panel.<alanadin.com>`. Environment
   değil **repo** değişkeni olmalı: iş koşulu (`if`) environment yüklenmeden değerlendirilir.
   `DEPLOY_ENABLED` yokken workflow hiç çalışmaz.
5. **Deneme:** *Actions → Deploy → Run workflow* (sha boş). Yeşil bitmeli. Sunucuda
   `ls ~/predeploy` ile yeni bir döküm görülmeli.
6. Yerel `abdera-actions` private key dosyasını sil (kopyası yalnızca GitHub secret'ında).

### 13.4 Kullanım

- **Normal:** `main`'e merge/push → ~10–15 dk sonra canlıda. CI süresi dahil; takip Actions
  sekmesinde.
- **Geri alma:** *Actions → Deploy → Run workflow* → `sha` = önceki iyi commit. Betik yalnızca
  `main` geçmişindeki commit'leri kabul eder.
- **Başarısız deploy:** Workflow kırmızı olur, GitHub e-posta gönderir. Sunucu zaten önceki
  sürüme dönmüştür. Log: Actions çıktısı + sunucuda `docker compose logs api`.
