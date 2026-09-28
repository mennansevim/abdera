# Hetzner Cloud Kurulum Runbook'u

Durum: **Runbook — henüz uygulanmadı.** Abdera'yı tek bir Hetzner Cloud sunucusunda, repodaki
hazır `docker compose --profile prod` kurulumuyla (db + api + web + Caddy) yayına alır ve bugünkü
Vercel + Supabase yayınından veriyi taşır. Railway alternatifi: `docs/18-railway-migration.md`.

Uygulama kodunda değişiklik gerekmez. Tüm komutlar sırayla, kopyala-yapıştır çalıştırılabilir;
`<...>` ile işaretli yerler doldurulur.

## 0. Önkoşullar

- Hetzner Cloud hesabı ve bir proje (`abdera`).
- Alan adı ve DNS yönetimi (Cloudflare veya kayıt firması). Örneklerde `panel.okulum.com`.
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

# --- Yedek (§7'de Storage Box kurulduktan sonra) ---
Backup__Provider=Sftp
Backup__EncryptionKey=...               # Vercel'deki MEVCUT anahtar - yoksa: openssl rand -base64 32
Backup__Sftp__Host=<uXXXXXX>.your-storagebox.de
Backup__Sftp__Port=23
Backup__Sftp__Username=<uXXXXXX>
Backup__Sftp__PrivateKeyPath=/app/keys/backup_ed25519
Backup__Sftp__Password=
Backup__Sftp__RemoteDirectory=/abdera

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

## 7. Yedekleme hedefi: Hetzner Storage Box

Uygulamanın şifreli günlük yedeği sunucunun **dışında** durmalı. Sunucu imajı yedeği (§1) ile
aynı yerde durmamalı.

1. Hetzner Console → **Storage Boxes** → BX11 (1 TB), aynı bölge. **SSH support** ve
   **External reachability** açık olsun. Mümkünse ana hesap yerine yalnızca `abdera/`
   klasörünü gören bir **alt hesap (sub-account)** aç ve aşağıda onu kullan: sunucudaki anahtar
   sızarsa Storage Box'un geri kalanına erişilemez.
2. Yedek anahtarını sunucuda üret ve Storage Box'a yükle (Storage Box ilk kurulumda parola
   ister):
   ```bash
   ssh-keygen -t ed25519 -N "" -f ~/backup_ed25519 -C abdera-backup
   ssh-copy-id -p 23 -s -i ~/backup_ed25519.pub <uXXXXXX>@<uXXXXXX>.your-storagebox.de
   sftp -P 23 -i ~/backup_ed25519 <uXXXXXX>@<uXXXXXX>.your-storagebox.de <<< "ls"   # parolasız girmeli
   ```
3. Anahtarı API container'ının okuyabileceği kalıcı volume'e koy (`app` kullanıcısı = 1654):
   ```bash
   cd /opt/abdera
   docker compose cp ~/backup_ed25519 api:/app/keys/backup_ed25519
   docker compose exec -u root api sh -c 'chown app:app /app/keys/backup_ed25519 && chmod 600 /app/keys/backup_ed25519'
   rm ~/backup_ed25519      # kopyası volume'de; .pub kalabilir
   docker compose restart api
   ```
4. Uygulamada **Yedekler** ekranından (`/dashboard/backups`) "şimdi yedekle"yi çalıştır. Kayıt
   `Succeeded` olmalı ve dosya Storage Box'ta `/abdera` altında görünmeli.
5. `docs/16-backup-restore.md` provasını bu dosyayla en az bir kez yap. Yedeğin geri
   yüklenebildiği görülmeden kurulum tamamlanmış sayılmaz.

6. Storage Box → **Snapshots → Automatic snapshots**: günlük, 10 adet (BX11'e dahil). Sunucu
   ele geçirilip Storage Box'taki yedekler silinse bile bu anlık görüntüler yalnızca Hetzner
   Console'dan yönetilir, sunucudaki SSH anahtarıyla silinemez.

**Yedek katmanları (özet):**

| Katman | Ne | Sıklık / saklama | Neye karşı |
|---|---|---|---|
| 1. Uygulama yedeği | `pg_dump` → AES-256 → Storage Box (SFTP) | Günlük 03:00, 30 gün | Veri silme/bozulma; hata olursa e-posta + audit |
| 2. Hetzner Backups | Sunucu disk imajı | Günlük, 7 adet | Sunucunun tamamen kaybı (DB için tek başına güvenilmez: çalışan Postgres'in anlık kopyası) |
| 3. Storage Box snapshot | Yedek klasörünün anlık görüntüsü | Günlük, 10 adet | Sunucu ele geçirilip yedeklerin silinmesi |

Veri kaybı penceresi en fazla ~24 saat (son gece yedeğinden bu yana girilen kayıtlar). Bu
kabul edilemezse sonraki adım `pgBackRest` ile sürekli WAL arşivi (Storage Box'a SFTP,
dakika hassasiyetinde geri dönüş) - özel Postgres imajı ve ayrı izleme gerektirir, ayrı karar.

### 7.1 Google Drive: Storage Box'ın yerine değil, yanına

Uygulama bugün yalnızca SFTP'ye yedek gönderebiliyor (`Backup__Provider=Sftp`). Drive'ı
**tek hedef** yapmak için iki yol var, ikisi de önerilmiyor:

- Yeni bir `IBackupStorage` (Google Drive API) yazmak. Kod + test işi. Ayrıca kişisel Gmail
  hesabında service account'ların kendi depolama kotası yok; kullanıcı OAuth token'ı gerekiyor.
  Token süresi dolarsa ya da iptal edilirse yedek sessizce durur.
- Sunucuda `rclone serve sftp` ile Drive'ı SFTP gibi göstermek. Kod gerekmez, ama yedeğin
  çalışması sunucudaki ek bir servise ve yine bir OAuth token'ına bağlı kalır.

Her iki durumda sunucu Drive'da silme yetkisine sahip olur. Storage Box snapshot'larının
(katman 3) verdiği "sunucu ele geçirilse bile silinemez" korumasının Drive'da tam karşılığı
yok; çöp kutusu ve sürüm geçmişi kısmi koruma sağlıyor.

**Önerilen:** Storage Box birincil hedef kalsın (~4 €/ay, kod yok). Drive istenirse
**ikinci, bağımsız kopya** olsun. Sunucuda günde bir kez `rclone`, Storage Box'taki şifreli
dosyaları Drive'a kopyalar:

```bash
sudo apt -y install rclone
rclone config          # "sb": sftp (Storage Box alt hesabı, port 23), "gdrive": drive
# Günlük 04:30 (uygulama yedeği 03:00'te biter):
( crontab -l 2>/dev/null; echo '30 4 * * * rclone copy sb:abdera gdrive:abdera-yedek --max-age 48h >> /var/log/abdera-rclone.log 2>&1' ) | crontab -
```

Dosyalar zaten AES-256 ile şifreli, Google içeriği okuyamaz. Veri yine de yurt dışında durur;
KVKK değerlendirmesine dahil edilmeli. `rclone` yeni bir sunucu aracı (uygulama bağımlılığı
değil). Drive kopyası kırılırsa uygulama bunu fark etmez; log'a ayda bir bakılmalı.

**Otomatik izleme:** Ayrı bir cron gerekmez. Yedek başarısız olursa uygulama
`Ops__AlertRecipients`'a e-posta atar ve `audit_log`'a `backup.failed` yazar. Hiç yedek
alınmazsa (ör. API kapalıysa) `SystemHealthMonitor` son başarılı yedek 30 saati geçince
uyarır, 48 saatte "unhealthy" der (`Ops__BackupStaleAfterHours` / `...UnhealthyAfterHours`).
API tamamen düşmüşse bu alarm da gidemez; onu §10'daki dış `/health` kontrolü yakalar.

**Veritabanı dökümünde OLMAYANLAR** (Hetzner Backups dışında ayrıca sakla):
`/opt/abdera/.env` ve `abdera_dpkeys` volume'ü (oturum anahtarları + SFTP anahtarı). `.env`'in
bir kopyası parola yöneticisinde dursun. SFTP anahtarı kaybolursa yenisi üretilir, oturum
anahtarı kaybolursa herkes bir kez yeniden giriş yapar.

`Backup__EncryptionKey` değerini ayrıca bir parola yöneticisinde sakla. Sunucu kaybolursa
yedekler bu anahtar olmadan açılamaz.

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
   - [ ] "Şimdi yedekle" → Storage Box'ta yeni dosya
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

### 13.2 Repoya eklenecek dosyalar (tek PR)

**`deploy/hetzner/abdera-deploy`**
```bash
#!/usr/bin/env bash
# GitHub Actions'ın SSH ile çalıştırabildiği TEK komut (authorized_keys "command=").
# Elle de çağrılabilir: abdera-deploy <40 karakterlik commit sha>
set -euo pipefail

SHA="${1:-${SSH_ORIGINAL_COMMAND:-}}"
[[ "$SHA" =~ ^[0-9a-f]{40}$ ]] || { echo "Geçersiz commit: '$SHA'" >&2; exit 2; }

exec 9>/tmp/abdera-deploy.lock
flock -n 9 || { echo "Başka bir deploy sürüyor." >&2; exit 3; }

cd /opt/abdera
git fetch --quiet origin main
git merge-base --is-ancestor "$SHA" origin/main || { echo "$SHA main'de değil." >&2; exit 4; }

PREV="$(git rev-parse HEAD)"
if [ "$PREV" = "$SHA" ]; then echo "Zaten $SHA'da."; exit 0; fi

# Deploy öncesi döküm - alınamazsa deploy yapılmaz (set -e + pipefail).
mkdir -p ~/predeploy
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' \
  | gzip > ~/predeploy/"$(date +%F-%H%M)-${PREV:0:7}.sql.gz"
ls -1t ~/predeploy/*.sql.gz | tail -n +6 | xargs -r rm --

deploy() {
  git checkout --quiet --force --detach "$1"
  docker compose --profile prod up -d --build --remove-orphans
}
healthy() {
  for _ in $(seq 1 36); do
    curl -fsS http://127.0.0.1:8080/health >/dev/null 2>&1 && return 0
    sleep 5
  done
  return 1
}

deploy "$SHA"
if healthy; then
  docker image prune -f >/dev/null
  echo "OK: ${PREV:0:7} -> ${SHA:0:7}"
else
  echo "Sağlık kontrolü geçmedi, ${PREV:0:7}'e geri dönülüyor." >&2
  deploy "$PREV"
  healthy || echo "UYARI: geri dönüşten sonra da sağlıksız - elle bak." >&2
  exit 1
fi
```

**`.github/workflows/deploy.yml`**
```yaml
name: Deploy

on:
  workflow_run:
    workflows: [CI]
    types: [completed]
    branches: [main]
  workflow_dispatch:
    inputs:
      sha:
        description: "Deploy edilecek commit (40 karakter). Boş = main'in son hali. Geri almak için eski bir commit ver."
        required: false

concurrency:
  group: deploy-production
  cancel-in-progress: false

jobs:
  deploy:
    # CI PR'larda da koşuyor; yalnızca main'e yapılan push'un yeşil CI'ı deploy eder.
    if: >-
      github.event_name == 'workflow_dispatch' ||
      (github.event.workflow_run.conclusion == 'success' && github.event.workflow_run.event == 'push')
    runs-on: ubuntu-latest
    timeout-minutes: 30
    environment:
      name: production
      url: https://panel.okulum.com
    steps:
      - name: Hedef commit
        id: target
        env:
          RUN_SHA: ${{ github.event.workflow_run.head_sha }}
          INPUT_SHA: ${{ inputs.sha }}
          REPO: ${{ github.repository }}
        run: |
          SHA="${RUN_SHA:-$INPUT_SHA}"
          [ -n "$SHA" ] || SHA="$(git ls-remote "https://github.com/$REPO.git" refs/heads/main | cut -f1)"
          echo "sha=$SHA" >> "$GITHUB_OUTPUT"

      - name: Sunucuda deploy
        env:
          SSH_KEY: ${{ secrets.DEPLOY_SSH_KEY }}
          KNOWN_HOSTS: ${{ secrets.DEPLOY_KNOWN_HOSTS }}
          HOST: ${{ secrets.DEPLOY_HOST }}
          SHA: ${{ steps.target.outputs.sha }}
        run: |
          install -m 700 -d ~/.ssh
          printf '%s\n' "$SSH_KEY" > ~/.ssh/deploy && chmod 600 ~/.ssh/deploy
          printf '%s\n' "$KNOWN_HOSTS" > ~/.ssh/known_hosts
          ssh -i ~/.ssh/deploy "deploy@$HOST" "$SHA"

      - name: Canlı kontrol
        run: |
          curl -fsS --retry 5 --retry-delay 5 https://panel.okulum.com/health
          curl -fsS -o /dev/null https://panel.okulum.com/login
```

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
4. **Deneme:** *Actions → Deploy → Run workflow* (sha boş). Yeşil bitmeli. Sunucuda
   `ls ~/predeploy` ile yeni bir döküm görülmeli.
5. Yerel `abdera-actions` private key dosyasını sil (kopyası yalnızca GitHub secret'ında).

### 13.4 Kullanım

- **Normal:** `main`'e merge/push → ~10–15 dk sonra canlıda. CI süresi dahil; takip Actions
  sekmesinde.
- **Geri alma:** *Actions → Deploy → Run workflow* → `sha` = önceki iyi commit. Betik yalnızca
  `main` geçmişindeki commit'leri kabul eder.
- **Başarısız deploy:** Workflow kırmızı olur, GitHub e-posta gönderir. Sunucu zaten önceki
  sürüme dönmüştür. Log: Actions çıktısı + sunucuda `docker compose logs api`.
