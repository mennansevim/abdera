# Miralay — işletim kılavuzu

Bu belge `arch/multi-tenant` branch'indeki platformun nasıl kurulup işletileceğini anlatır. Tasarım ve
gerekçeler `ARCHITECTURE.md`'dedir; burada yalnızca yapılacaklar var.

## 1. Parçalar

| Parça | Yer | Nerede çalışır |
|---|---|---|
| Platform API | `platform/api` (.NET 10, kendi PostgreSQL'i) | Ayrı, küçük platform sunucusu |
| Platform paneli | `platform/web` (Next.js) | Platform sunucusu |
| Sunucu ajanı | `deploy/host/abdera-host` | Her okul sunucusu (`/usr/local/bin`) |
| Okul şablonu | `deploy/instance/compose.yml` | Her okul sunucusu (`/usr/local/share/abdera`) |
| Paylaşılan Caddy | `deploy/host/caddy/` | Her okul sunucusu, tek konteyner (`abdera-caddy`) |
| İmaj hattı | `.github/workflows/images.yml` | GitHub Actions (sürüm etiketinde) |

## 2. Platform sunucusunu kurmak

1. Sunucu: 2 GB RAM yeterli. Yalnızca operatör IP'lerinden (veya VPN'den) erişilir; 443 dışında kapı açık değil.
2. Platform için SSH anahtarı üret (okul sunucularına bu anahtarla bağlanır):
   `ssh-keygen -t ed25519 -f /srv/platform/platform_ssh_key -N ""`
3. Cloudflare: okul alan adının (örn. `abdera.app`) bölgesinde yalnızca **DNS düzenleme** yetkili bir API jetonu.
4. R2: iki anahtar.
   - Okullara dağıtılan: yalnızca **nesne okuma + yazma**. **Silme yetkisi verme** (§16.9). Bucket'ta nesne kilidi
     ve 60/65 günlük yaşam döngüsü kuralı açık; `*/suspended/` öneki kuralın dışında.
   - Senin yönetim anahtarın platformda durmaz.
5. `platform/.env` (repoya girmez):
   ```
   PLATFORM_DOMAIN=platform.abdera.app
   PLATFORM_ALLOWED_IPS=<operatör IP'leri, boşlukla>
   ACME_EMAIL=ops@abdera.app
   PLATFORM_DB_PASSWORD=<rastgele>
   PLATFORM_BOOTSTRAP_EMAIL=<senin e-postan>
   PLATFORM_BOOTSTRAP_PASSWORD=<en az 12 karakter, ilk girişten sonra boşaltılabilir>
   PLATFORM_SSH_KEY_PATH=/srv/platform/platform_ssh_key
   SCHOOLS_BASE_DOMAIN=abdera.app
   PRICE_PER_SEAT=<kullanıcı başı yıllık liste fiyatı, TRY>
   PRICE_ADDON_WHATSAPP=<yıllık>   PRICE_ADDON_AI=<yıllık>   PRICE_ADDON_BANKING=<yıllık>
   CLOUDFLARE_ZONE_ID=...
   CLOUDFLARE_API_TOKEN=...
   R2_ACCOUNT_ID=...
   R2_BUCKET=abdera-backups
   R2_SCHOOL_ACCESS_KEY_ID=...
   R2_SCHOOL_SECRET_ACCESS_KEY=...
   ```
6. `docker compose -f platform/compose.yml --env-file platform/.env up -d --build`
7. Panele gir, **TOTP'yi kur** (kimlik doğrulayıcıya gizli anahtarı ekle). TOTP olmadan okul yönetilemez.

### Anahtar yedeği (kritik)

`keys` volume'ü (Data Protection anahtarları) tüm okulların sırlarını çözer. Kaybolursa platform okulların
veritabanı parolalarını, yedek anahtarlarını ve jetonlarını okuyamaz. Kurulumdan hemen sonra ve ayda bir:

```
docker run --rm -v abdera-platform_keys:/k alpine tar czf - -C /k . | gpg -c > platform-keys-$(date +%F).tgz.gpg
```

Dosyayı platform sunucusu dışında, parola yöneticisindeki parolayla sakla. Platform veritabanının dökümüyle
birlikte **geri yükleme provası** yap: döküm + anahtar → boş bir sunucuda platform açılıyor ve bir okulun
telemetrisi okunuyor.

## 3. Okul sunucusu eklemek

1. Temiz bir Ubuntu/Debian sunucu (64 GB → ~65 okul; okul başına 768 MB rezerv).
2. Repoyu sunucuya kopyala ve çalıştır:
   `sudo deploy/host/setup-host.sh "$(cat platform_ssh_key.pub)" ops@abdera.app`
   Bu, Docker'ı, ajanı, okul şablonunu, paylaşılan Caddy'yi kurar ve platform anahtarını
   `command="/usr/local/bin/abdera-host",restrict` kısıtıyla ekler.
3. Sunucunun anahtar parmak izini al: `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` (`SHA256:...`).
4. Panel → Sunucular → Ekle (ad, adres, kullanıcı `root`, parmak izi, genel IP, toplam bellek) → **Yokla**.
   Parmak izi tutmazsa bağlantı reddedilir (TOFU yok).

## 4. Sürüm yayımlamak

1. `main`'deki commit'e değil, bu branch'te: `git tag v2026.10.20.1 && git push origin v2026.10.20.1`
   (ya da Actions → Images → elle tetikle).
2. İş bitince `release.json` artefaktını indir; Panel → Sürümler → Kaydet'e yapıştır. Platform imajları
   digest ile kaydeder; katalogdaki modüller sihirbazda bu sürüme göre listelenir.

## 5. Okul kurmak

Panel → `+`. Sihirbaz okulun kimliği, sürümü, modülleri, yöneticisi, kuralları, aidatı ve entegrasyonlarını
toplar. "Kur" sonrası 12 adım (`ARCHITECTURE.md` §6) canlı izlenir. Bitince okul detayında
**Geçici parolayı göster** ile yöneticinin ilk parolası alınır (gösterim audit'e yazılır); yönetici ilk girişte
değiştirmek zorundadır.

Bir adım hata verirse: **Yeniden dene** kaldığı adımdan sürer. Vazgeçilirse **İptal et ve temizle**: okul hiç
veri almadıysa sunucudaki her şey hemen silinir, DNS kaydı ve kapasite bırakılır.

**Paket ve fiyat:** fiyatın tek ekseni personel hesabı sayısıdır (yönetici + öğretmen; veli ve öğrenci
sayılmaz). Sihirbazda kullanıcı sayısı, kullanıcı başı fiyat ve seçilen her ücretli eklentinin yıllık fiyatı
girilir; liste fiyatı (`PRICE_*`) boş alanlara gelir, okulla anlaşılan fiyat elle yazılır. Paket okul kaydına
donar; lisans yıllıktır, sihirbazda başlangıç günü seçilir (bitişi bir yıl sonrasının bir gün öncesi). Okul detayı →
**Paket → Lisansı yenile** bir yıl uzatır; bitişe 30 gün kala kutucukta uyarı çıkar, süre dolan okul kendiliğinden
kapanmaz. Okulda paket dolunca yeni personel hesabı açılmaz ve pasif öğretmen geri açılmaz; hesapsız öğretmen
kaydı ve var olan hesaplar etkilenmez. Sonradan okul detayı → **Paket**: kullanıcı sayısı değişirse okul
yeniden yapılandırılır (~30 sn), yalnızca fiyat değişirse okula dokunulmaz. Sahiplenilen okulda paket
girilmezse okul sınırsız kalır.

WhatsApp: her okulun kendi Meta (WABA) hesabı ve şablon onayı ayrı bir operasyon işidir; sihirbazdaki
anahtarlar girilmezse WhatsApp kapalı kurulur, sonra Modüller sekmesinden açılabilir.

### Okulun kendi alan adı

Okul paneli platform alt alanı yerine kendi adresinde (ör. `panel.okulum.com`) isterse: okul detayı → **Alan adı**.
Okulun web sitesi kendi yerinde kalır; sitesine bu adresi "Giriş" bağlantısı olarak koyar.

1. Alan adını kaydet (isteğe bağlı: eski adres yeni adrese yönlensin). Platformun kendi alanı ve başka okulun
   adresi reddedilir.
2. Panelin gösterdiği DNS kaydını müşteriye ver: alt alan için `CNAME → <kısaltma>.<platform alanı>`, kök alan için
   `A → sunucu IP'si`. Cloudflare kullanan okulda kaydın proxy'si kapalı olmalı (sertifikayı Caddy alır).
3. **DNS'i kontrol et:** alan adı okulun sunucusuna çözülüyorsa "Alan adını bağla" işlemi kurulur — ajan
   `route alias` ile Caddy bloğuna ikinci adresi ekler, platform `https` duman testi yapar, adres etkinleşir.
   Yönlendirmede `/api` ve `/health` asıl adreste kalır (telemetri, kurulum ve sertifika izleme oradan gider).
4. Kaldırma: **Alan adını kaldır** → `route alias-remove`; okul yalnızca platform adresinde açılır.

Ek adres askı/devam/güncellemede korunur, askıda askı sayfasını gösterir. Sertifika izleyicisi bağlı adresi de
denetler. Okul silinince (`purge`) alan adı serbest kalır.

## 6. Güncelleme

- **Tek okul:** okul detayı → Güncelle (yalnızca ileri sürüm). Ajan önce döküm alır, yeni imajı başlatır,
  sağlıksızsa eski imaja döner. Şema geri gelmez: migration'lar yalnızca ekleyici yazılmalı.
- **Filo:** Panel → Dağıtımlar → Başlat. Dalga 0 kanarya okullar, 24 saat sorunsuz bekler; dalga 1 kalanların
  ~%10'u; dalga 2 geri kalanı. Herhangi bir okulda hata olursa dağıtım durur. Sürümü sabitlenmiş okullar atlanır.

## 7. Askı, silme, prova

- **Askı:** ajan önce uygulamadan bağımsız şifreli döküm alıp R2'de `<okul>/suspended/` önekine kopyalar
  (saklama kuralı dışı), sonra okul askı sayfasına döner. Devam ettir tersini yapar.
- **Silme:** 1. aşama okulu durdurur, veriyi korur; 30 gün içinde **Silmeyi geri al** mümkündür. **Kalıcı sil**
  ancak 30 gün dolunca ajan tarafından kabul edilir; platform bu süreyi kısaltamaz.
- **Prova geri yükleme:** okulun dökümü aynı sunucuda `<okul>-drill` kopyasına geri yüklenir; WhatsApp,
  e-posta, banka, alarm ve yedek **kapalı** başlar, yayına alınmaz. Doğrulayınca kopyayı elle sil:
  `abdera-host destroy <okul>-drill && abdera-host purge <okul>-drill` (veri almadığı için beklemeden silinir).

## 7a. İzleme ve alarmlar

- **Telemetri:** platform 5 dakikada bir her okulun sağlık/sayı/yedek bilgisini okur; ulaşılamayan okulun kutucuğu kırmızıya döner.
- **Okul alarmları:** okul kendi sorunlarını (yedek, bildirim, sağlık) imzalı olarak bildirir; Panel → Alarmlar.
- **Sertifika:** platform 6 saatte bir her okulun alan adına TLS ile bağlanır. Sertifikanın bitmesine 14 günden az
  kaldıysa "Sertifika bitiyor" (3 günden az: kritik), zincir doğrulanmıyorsa "Sertifika geçersiz" alarmı düşer.
  Caddy normalde 30 gün kala yeniler; bu alarm yenilemenin durduğu anlamına gelir (DNS kaydı değişti, port 80
  kapandı, Let's Encrypt sınırı). Okul sunucusunda `docker logs abdera-caddy` ile bak. Aynı sorun için onaylanmamış
  alarm varken yenisi yazılmaz; onaylarsan ve sorun sürerse bir sonraki turda tekrar düşer.
  Ayarlar: `Certificates__CheckHours`, `Certificates__WarnDays`.

## 8. Sunucu çökerse

Platform otomatik bir şey yapmaz (§16.6). Sunucu dönünce okullar kendiliğinden kalkar (`restart: unless-stopped`).
Disk kaybedildiyse: yeni sunucu ekle, her okul için `abdera-host create` + `restore <döküm>` (döküm R2'deki son
günlük yedek ya da `suspended/` öneki). Veri kaybı en fazla son günlük yedekten bu yanadır.

## 9. Bugünkü okulun platforma taşınması (F5, bakım penceresinde)

Henüz yapılmadı; canlı okul `/opt/abdera`'da `docker compose --build` ile çalışmaya devam ediyor. Platform tarafı
hazır: **Var olan okulu taşı** (`POST /api/schools/adopt`). Sıra:

1. Yeni okul sunucusu platforma eklenir (§3).
2. Bakım penceresi duyurulur. Eski sunucuda: `docker compose exec -T db pg_dump -U abdera abdera | gzip > abdera-<tarih>.sql.gz`
3. Döküm yeni sunucuya `/srv/abdera/<okul>/dumps/` altına kopyalanır (dizin yoksa "Var olan okulu taşı" ilk
   adımlarda oluşturur; adım "döküm yok" hatası verirse dosyayı koyup **Yeniden dene**).
4. Panel → Var olan okulu taşı: aynı kısaltma, okulun bugünkü alan adı (özel alan adı), sürüm, modüller (bugün
   hepsi açık), WhatsApp/e-posta anahtarları, döküm adı, **kanarya** işaretli.
5. Platform: sunucu seç → sırlar → dosyalar → imajlar → başlat → dökümü geri yükle → sağlık → yönlendirme.
6. DNS: alan adının A kaydı yeni sunucuya çevrilir (özel alan adında platform DNS'e dokunmaz). Duman testi
   geçince okul etkin olur.
7. Eski `/opt/abdera` bir hafta durdurulmuş halde bekletilir, sonra kaldırılır. Eski oturum çerezleri geçersiz
   olur (yeni Data Protection anahtarları): kullanıcılar bir kez yeniden giriş yapar.

## 10. Yerel test ortamı (`deploy/local/`, macOS + Podman)

Gerçek okul imajları, gerçek ajan (SSH ile), gerçek paylaşılan Caddy ve yerel bir S3 ile tüm yapı Mac'te
denenir. Okullar `https://<kısaltma>.okul.localhost:8443` adresinde açılır. Cloudflare yerine sahte DNS (`*.localhost`
zaten 127.0.0.1'e çözülür), R2 yerine `rclone serve s3` kullanılır. Hiçbir dış hesap gerekmez.

```bash
deploy/local/up.sh                              # depo (5050), yerel S3 (9000), Caddy (8088/8443), sunucu konteyneri (2222)
deploy/local/build-release.sh 2026.10.16.1      # okul imajlarını derleyip yerel depoya iter
deploy/local/platform.sh                        # platform API'si (8090); veritabanı .out/platform-db.env
(cd platform/web && npm run dev)                # panel: http://localhost:3100
python3 deploy/local/seed.py                    # operatörün TOTP'si, sürümler, sunucu kaydı
python3 deploy/local/seed.py totp               # panele giriş kodu (operatör bilgileri .out/operator.env)
python3 deploy/local/seed.py school ornek "Örnek Müzik Okulu" progress library   # ya da paneldeki sihirbaz
deploy/local/seed-demo.sh ornek                 # okula 6 aylık örnek veri
python3 deploy/local/seed.py reveal a b && deploy/local/check-isolation.sh a b   # oturum izolasyonu
```

Notlar:
- Podman VM'inde SELinux açık; bind mount'lar `label=disable` ile bağlanır. VM'e yalnızca `localhost:5050`
  deposunu şifresiz kabul eden tek bir dosya eklenir: `/etc/containers/registries.conf.d/50-abdera-local-registry.conf`.
- Platform `http` üzerinde çalıştığı için okullara alarm adresi yazılmaz (okul uygulaması `https` olmayan adresle
  açılmaz). Alarm imzası platform testlerinde sınanır.
- Caddy'nin yerel kök sertifikası `.out/caddy-root.crt`. Okulu tarayıcıda açmak için ya uyarıyı kabul et ya da bu
  sertifikaya güven.
- `.out/` repoya girmez (anahtarlar, parolalar, sürüm kayıtları).
- Yerelde platform girişinde iki adımlı doğrulama istenmez (`Platform__RequireTotp=false`, `platform.sh`); doğrulama
  kodu alanı boş bırakılır. Production bu ayarla açılmaz.

2026-10-16 yerel provasında bulunup düzeltilenler: kanarya bekleme süresinin hiç dolmaması (dağıtım 0. dalgada
kalıyordu), bozuk imajda `compose up` hata dönünce ajanın geri dönüşü atlaması (okul kapalı kalıyordu), prova
kopyasının `destroy` ile silinememesi, başarısız güncellemeden sonra panelin okulu bozuk sürüme geçiyor göstermesi.
