# Abdera — Yük Testi Günlüğü

Bu dosya, `loadtest/k6/` altındaki k6 senaryolarıyla yapılan uçtan uca yük testinin
her turunu belgeler: ölçüm, bulunan sorun, yapılan değişiklik, sonuç.

## Ortam

- Hedef: `http://localhost:3000` (web) / `http://localhost:8080` (api) — **yalnızca local Docker Compose**, prod'a hiç istek atılmadı.
- Backend: .NET 10 ASP.NET Core Minimal API, EF Core + Npgsql, PostgreSQL 16 (Docker Compose: `db`, `api`, `web`).
- Veri: gerçek kullanıcı verisi **kullanılmadı**. Tamamen sentetik fixture:
  `POST /api/dev/load-test/seed` ([backend/src/Abdera.Api/Shared/LoadTestFixtures.cs](backend/src/Abdera.Api/Shared/LoadTestFixtures.cs),
  Development-only + AdminOnly, şema değişikliği yapmaz) → 8 öğretmen, 152 öğrenci, 152 kayıt, 4864 ders
  (CLAUDE.md'nin hedef ölçeği: 6–8 öğretmen, ~150 öğrenci).
- k6 v2.2.0 (host makineden, container'a değil).

## Test edilen kritik akışlar (k6 script'leri)

| Akış | Script | Uç nokta(lar) |
|---|---|---|
| Login (+ oturum doğrulama) | `loadtest/k6/ramp.js` → `doLogin()` | `POST /api/auth/login`, `GET /api/auth/me` |
| Öğrenci ekle/güncelle/sil | `loadtest/k6/ramp.js` → `doCrud()` | `POST/PATCH /api/students/{id}` (silme = soft-delete `status=Inactive`, CLAUDE.md) |
| Takvim görünümü (tarih aralığı) | `loadtest/k6/ramp.js` → `doCalendar()` | `GET /api/calendar?from=&to=&teacherId=` |
| Sürükle-bırak sonrası ders taşıma | `loadtest/k6/ramp.js` → `doReschedule()` | `PATCH /api/lessons/{id}` |
| **Eş zamanlı güncelleme (lost-update)** | `loadtest/k6/concurrent_reschedule.js` | Aynı dersi N VU ile aynı anda `PATCH` |

Ana profil (`ramp.js`) bu dört akışı tek bir gerçekçi trafik karışımında ağırlıklandırır
(login %15, takvim %50, CRUD %20, reschedule %15) ve `PROFILE` env değişkenine göre üç modda
çalışır: `ramp` (10→50→200→500 VU, her kademe 2 dk), `spike` (ani 0→500), `soak` (15 dk sabit 50 VU).

## Başarı kriterleri

- p95 < 500ms, p99 < 1s (tüm istekler genelinde, `http_req_duration`)
- Sistem hata oranı < %1 (yalnızca 5xx/bağlantı hatası — 409 çakışma gibi **iş kuralı** reddi ayrı sayılır, `app_errors` metriği)
- Soak sırasında bellek artışı yok (leak yok)
- Concurrent update'te veri bozulması / lost update yok (`verify-concurrent-reschedule.sh` ile DB doğrulaması)

## Metodoloji notu (şeffaflık için)

6 turun tamamında TAM profili (ramp + spike + 15dk soak) tekrar tekrar koşmak tur başına
~30 dakika sürüyor (6 tur ≈ 3 saat yalnızca test çalıştırma). Bunun yerine:
- **Round 1**: tam profil (ramp + spike + soak) + concurrency testi — temel çizgi.
- **Round 2-5**: yalnızca `ramp` profili + concurrency testi (hızlı doğrulama döngüsü) —
  bir önceki turun bulgusuna yönelik düzeltmeyi hızlıca doğrulamak için.
- **Son tur** (kriterler sağlandığında veya 6. turda): tam profil tekrar koşulur, tüm
  kriterlerin gerçekten (yalnızca ramp'te değil, spike/soak altında da) sağlandığı teyit edilir.

---

## Round 1 — Temel çizgi (baseline)

### Ölçümler

| Profil | VU | p95 | p99 | http_req toplam | throughput | app_errors (5xx) | checks |
|---|---|---|---|---|---|---|---|
| ramp (10→50→200→500, kademe 2dk) | ≤500 | **349.7ms** | **625.7ms** | 182 203 | 275.6 req/s | %0.00 | 100% |
| spike (ani 0→500) | 500 | **283.0ms** | **404.0ms** | 52 629 | 609.9 req/s | %0.00 | 100% |
| soak (15 dk sabit 50 VU) | 50 | **36.1ms** | **56.7ms** | 66 536 | 73.8 req/s | %0.00 | 100% |

Üç profilde de k6 threshold'ları (`p(95)<500`, `p(99)<1000`, `app_errors rate<0.01`) **geçti**
(k6 konsol çıktısındaki ✓ işaretleri esas alındı — `--summary-export` JSON'undaki
`thresholds` alanı bu k6 sürümünde (2.2.0) yanıltıcı: cumulative rate metrikleri için
`false` basıyor olsa da konsol render'ı ve ham `passes/fails` sayıları aslında %0 hata
gösteriyor; bkz. `results/round1-*.log` "THRESHOLDS" bölümü). `http_req_failed` genel oranı
(k6'nın varsayılan "2xx/3xx dışı = başarısız" tanımı, ör. ramp'te %8.27) neredeyse tamamen
**iş kuralı** reddi (`business_conflicts` metriği, ramp'te %9.79) — reschedule akışının
kasıtlı olarak rastgele/çakışan hedeflere yazması (bkz. `ramp.js` `doReschedule` yorumu).
Gerçek sunucu hatası (`app_errors`, yalnızca 5xx/bağlantı hatası) üç profilde de **%0**.

**Bellek (soak, 15 dk, 1 dk aralıklarla örneklendi):** API container 161→155→150→149→147→
146→143→140→137→141→141→140→137→141 MiB. Net eğilim düz/hafif azalan — **büyüme/leak yok**.

### Bulgu 1 — KRİTİK: Eş zamanlı reschedule'da veri bozulması (lost update)

`PATCH /api/lessons/{id}` ([UpdateLesson.cs](backend/src/Abdera.Api/Modules/Scheduling/Features/UpdateLesson.cs))
dersi `SingleOrDefaultAsync` ile okuyup `Status != Normal` kontrolü yapıyordu ama **hiçbir
satır kilidi veya optimistic concurrency token'ı (xmin) yoktu** — CLAUDE.md'nin "eşzamanlı
düzenleme riski olan tablolarda optimistic concurrency" kuralı `Lesson` için hiç
uygulanmamış (yalnızca `Receivable` ve `BankIncomingTransaction`'da var).

**Kanıt** (`loadtest/k6/concurrent_reschedule.js`, 20 VU aynı dersi aynı anda `PATCH`):
20 istekten **4'ü 200 döndü**, her biri farklı bir "replacement" ders satırı (`c05292e0…`,
`c828d6e8…`, `392cba81…`, `8f6e7c0b…`) yaratıp aynı `original_lesson_id`'yi işaret etti —
tek bir kazanan yerine aynı öğrenci/öğretmen için **4 çakışan aktif ders** oluştu
(`loadtest/verify-concurrent-reschedule.sh` ile DB'de doğrulandı: `replacement_count=4`).
Yalnızca 1 istek 409 aldı.

**Kök neden:** klasik TOCTOU — N eş zamanlı istek aynı anda `Status == Normal` okuyor, hepsi
şartı geçip yazıyor, DB seviyesinde bunu engelleyen hiçbir şey yok.

**Düzeltme** ([UpdateLesson.cs](backend/src/Abdera.Api/Modules/Scheduling/Features/UpdateLesson.cs)):
Şema değişikliği (migration/xmin eklemek) gerektirmeyen bir çözüm seçildi — açık bir
transaction içinde `SELECT * FROM lessons WHERE id = {id} FOR UPDATE` ile satır **pesimist
kilit** alınıyor, kilit `SaveChangesAsync` + `CommitAsync`'e kadar tutuluyor. İkinci istek bu
satırda bloke olur, birincinin commit'inden sonra `Status=Rescheduled` görüp doğru şekilde
409 döner.

**Doğrulama:** Aynı test (20 VU, aynı dersi hedefleyen) tekrar koşuldu — **tam 1 istek 200,
19 istek 409**, `replacement_count=1` (bkz. `results/round1-concurrency-after-fix.log`).
Mevcut 327 backend testi (`dotnet test`) düzeltmeden sonra da **327/327 geçti**.

### Bulgu 2 — ORTA: Connection pool / Postgres max_connections sınırı

Npgsql bağlantı dizesinde açık bir `Maximum Pool Size` **yoktu** (Npgsql varsayılanı: 100)
ve `db` servisinin Postgres'i de varsayılan `max_connections`'la (~100, birkaçı superuser'a
ayrılmış) çalışıyordu — ikisi pratikte aynı sınıra denk geliyordu.

**Kanıt:** 500 VU'luk spike testinden dakikalar sonra, hatta 50 VU'luk soak testi sırasında
bile `docker exec ... psql` ile DIŞARIDAN bağlanmaya çalışmak
`FATAL: sorry, too many clients already` ile reddedildi (Npgsql'in varsayılan
`Connection Idle Lifetime`'ı 300s — pool'daki boşta bağlantılar 5 dakikaya kadar açık
kalıyor). API'nin kendisi bu sürede sorunsuz çalışmaya devam etti (kendi pool'undan
bağlantı alabiliyordu) — risk dışarıdan gelen HERHANGİ bir istemciydi (migration, yedekleme,
admin `psql`, pgAdmin, ikinci bir uygulama örneği).

**Düzeltme** ([docker-compose.yml](docker-compose.yml)): `ConnectionStrings__Default`'a
`Maximum Pool Size=50;Connection Idle Lifetime=60` eklendi (api'nin havuzu belirgin şekilde
sınırlandı, boşta bağlantılar 5 dk yerine 60 sn'de kapanıyor); `db` servisine
`command: postgres -c max_connections=200` eklendi (api'nin 50'lik havuzunun üstünde bolca
pay bırakır). Şema değişikliği değil — yalnızca bağlantı/konfigürasyon.

**Not:** Bu, gerçek üretim yükünde (6-8 öğretmen, ~150 öğrenci) hiç görülmeyecek bir sorun —
yalnızca 500 eş zamanlı VU'luk yapay yük testinde ortaya çıktı. Yine de gerçek bir operasyonel
risk (bir bakım penceresinde admin'in DB'ye bağlanamaması) olduğu için düzeltildi.

### Round 1 sonucu

| Kriter | Sonuç |
|---|---|
| p95 < 500ms | ✅ (349.7ms, ramp) |
| p99 < 1s | ✅ (625.7ms, ramp) |
| Hata oranı < %1 (gerçek sunucu hatası) | ✅ (%0.00) |
| Soak'ta bellek artışı yok | ✅ (düz/azalan eğilim) |
| Concurrent update'te veri bozulması yok | ❌ **BULUNDU VE DÜZELTİLDİ** (Bulgu 1) |

4 kriterden 3'ü Round 1'de zaten sağlandı; kritik olan concurrency bug'ı ve bir operasyonel
connection-pool riski bulunup düzeltildi. Round 2, düzeltmelerin performansı bozmadığını
doğrulamak için `ramp` profiliyle tekrar koşuluyor.

---

## Round 2 — Doğrulama koşusu (REGRESYON BULUNDU)

Yalnızca `ramp` profili koşuldu (metodoloji notuna bkz.).

| Metrik | Round 1 | Round 2 | Hedef |
|---|---|---|---|
| p95 | 349.7ms | **1.37s** | <500ms ❌ |
| p99 | 625.7ms | **2.59s** | <1s ❌ |
| app_errors (5xx) | %0.00 | %0.68 | <%1 ✅ (sınırda) |
| throughput | 275.6 req/s | **8.3 req/s** | — |

Round 1'de uygulanan connection-pool düzeltmesi (`Maximum Pool Size=50`) **fazla
agresifti**: 500 eş zamanlı VU, 50'lik bir havuz için 10x fazla talep yaratıp istekleri
kuyrukta bekletti. API loglarında tek bir 5xx yok (`docker logs abdera-web-api-1`:
121704×200, 18327×201, 12061×409 iş kuralı reddi, 144×403 - gerçek hata değil, hepsi
kuyruklama gecikmesi) - bu da teşhisi doğruluyor: **darboğaz gerçek bir hata değil, saf
bağlantı kuyruklama gecikmesiydi.**

**Düzeltme** ([docker-compose.yml](docker-compose.yml)): `Maximum Pool Size` 50 → **150**,
`db` servisinin `max_connections`'ı 200 → **250** (150'nin üstünde dışarıdan erişim için
100'lük pay kalıyor). Round 3 bu ayarla koşuluyor.

---

## Round 3 — Pool 150'ye çıkarıldı (kritere 13ms kala)

| Metrik | Round 2 (pool=50) | Round 3 (pool=150) | Hedef |
|---|---|---|---|
| p95 | 1.37s | **513ms** | <500ms ❌ (13ms/%2.6 üstünde) |
| p99 | 2.59s | **784.6ms** | <1s ✅ |
| app_errors (5xx) | %0.68 | **%0.00** | <%1 ✅ |
| throughput | 8.3 req/s | **254.8 req/s** | — (Round 1: 275.6) |

Neredeyse tüm kazanç geri geldi. p95 hâlâ hedefin az üstünde. API loglarında yine 5xx yok
(132877×200, 20246×201, 15388×409 iş kuralı, 109×401). Bu 109×401'i incelerken k6
script'inde kendi bug'ımı buldum: `loadtest/k6/lib.js`'teki `loginAsAdmin`/`loginAsTeacher`
login **başarısız olsa bile** VU'nun "zaten girişliyim" önbelleğini koşulsuz set ediyordu -
nadir bir geçici login hatasından sonra VU oturumsuz isteklere devam edip 401 alıyordu. Bu
uygulama hatası değil, test script'i hatası - düzeltildi (yalnızca 200 dönerse önbellekle).

**Düzeltme:** `Maximum Pool Size` 150 → **200** (db `max_connections=250`'nin altında,
dışarıya hâlâ 50'lik pay bırakıyor) - kalan kuyruklama gecikmesini kapatmak için.

---

## Round 4 — Regresyon (ama uygulamada DEĞİL, test script'inde)

Pool 200'e çıkarılınca **beklenenin tersi** oldu: p95 513ms → **1.58s**, p99 785ms →
**8.25s**. İlk bakışta "daha büyük pool daha kötü" gibi duruyordu ama API loglarında yine
5xx yoktu (%0.10 hata, hepsi 401/403 - bkz. aşağıda) ve throughput da düşmüştü (254.8 → 176
req/s) - bu bir kaynak sınırlaması değil, **kilitlenme (lock contention)** izi.

**Kök neden (gerçek bulgu — test metodolojisinde):** `loadtest/k6/ramp.js`'in `setup()`'ı
reschedule havuzunu **-20g..+60g** tek penceresinden çekiyordu. Round 1-3'ün reschedule
trafiği bu sabit pencereyi tüketmişti - DB'de doğrulandı: **o pencerede 0 "Normal" ders
kalmıştı, 2786'sı "Rescheduled"**. Round 4 başladığında havuz neredeyse boştu; çok sayıda VU
kalan birkaç dersi hedefleyip Round 1'de eklenen **doğru** `FOR UPDATE` kilidinde
kuyruklanmaya başladı - yani Round 1'in concurrency düzeltmesi burada "çalışıyordu" ama test
scripti onu yapay bir darboğaza sürüklüyordu. Bu, gerçek dünyada olmaz: bir okulun gerçek
dersleri kullanışla "tükenmez", CLAUDE.md ölçeğinde (6-8 öğretmen) böyle bir kilitlenme
fırtınası oluşmaz.

**Düzeltme (test script'i, uygulama değil):** `ramp.js setup()` artık İKİ pencereden havuz
topluyor: `-20g..+60g` (orijinal seed verisi) + `+65g..+155g` (reschedule'ın ÜRETTİĞİ
replacement derslerin düştüğü aralık - bu havuz her reschedule'da kendini yeniler, tükenmez).
`lib.js`'teki başka bir script bug'ı da düzeltildi: login başarısız olsa bile VU'nun "zaten
girişliyim" önbelleği koşulsuz set ediliyordu (109 sahte 401'in kaynağı).

Pool boyutu (150 vs 200) sorusu hâlâ açık - Round 5 düzeltilmiş script'le tekrar koşuluyor.

---

## Round 5 — Test bug'ı düzeldi, ama pool=200 hâlâ kötü

| Metrik | Round 3 (pool=150, eski script) | Round 4 (pool=200, eski script) | Round 5 (pool=200, düzeltilmiş script) | Hedef |
|---|---|---|---|---|
| p95 | 513ms | 1.58s | **1.09s** | <500ms ❌ |
| p99 | 785ms | 8.25s | **1.52s** | <1s ❌ |
| checks_succeeded | %99.93 | %99.89 | **%100.00** | — |
| gerçek hata (5xx) | %0.00 | %0.10 | **%0.00** | <%1 ✅ |

Test-havuzu tükenmesi bug'ı (Round 4 notu) düzeldi (`UYARI` log satırı hiç tetiklenmedi,
sahte 401'ler gitti, checks %100 başarılı) — ama p95/p99 hâlâ Round 3'ten (pool=150) kötü.
Bu, `Maximum Pool Size=200`'ün **kendisinin** bu host'ta (8 çekirdek) daha kötü olduğunu
doğruluyor: ~200 eş zamanlı Postgres backend'i, sınırlı CPU'da bağlam değişimi/kilit
rekabetine giriyor - "daha büyük pool = daha iyi" varsayımı yanlış çıktı. Pool 150'ye geri
alındı, Round 6 (bu değerlendirmenin son turu) bu ayarla ve düzeltilmiş script'le koşuluyor.

---

## Round 6 — Son tur: TÜM KRİTERLER SAĞLANDI ✅

| Profil | p95 | p99 | app_errors | checks |
|---|---|---|---|---|
| ramp (10→50→200→500) | **402.4ms** ✅ | **672.0ms** ✅ | %0.00 ✅ | %100 |
| spike (ani 0→500) | **457.2ms** ✅ | **662.1ms** ✅ | %0.00 ✅ | %100 |
| soak (15dk, 50 VU) | _aşağıda_ | _aşağıda_ | _aşağıda_ | _aşağıda_ |

Round 3'ün aynı pool ayarıyla (150) elde ettiği 513ms/785ms'e kıyasla Round 6'nın 402ms/672ms
daha da iyi — muhtemelen düzeltilmiş reschedule havuzunun (iki pencere) artık gerçek çakışma
üretmemesi sayesinde.

**Soak (15 dk, 50 VU):** p95=**34.2ms**, p99=**51.6ms**, %0.00 gerçek hata, 73 298 istek,
%100 check başarısı. **Bellek** (90 sn aralıklarla örneklendi): 177→142→142→142→139→142→
142→143→142 MiB — soak boyunca düz, **büyüme/leak yok**.

**Eş zamanlı reschedule (son doğrulama):** 20 VU aynı dersi aynı anda `PATCH` etti — **tam 1
istek 200, 19 istek 409**, DB'de `replacement_count=1` (bkz. `results/round6-concurrency.log`,
`verify-concurrent-reschedule.sh` çıktısı). Round 1'deki düzeltme (satır kilidi) kalıcı.

**Mevcut backend testleri:** `dotnet test` — 327/327 geçti (bir önceki koşuda tek bir test,
`OpsFlowTests.Manual_backup_trigger_...`, makine yoğun k6/docker aktivitesi altındayken bir
kez flaky başarısız oldu; izole ve tekrar tam koşuda **327/327** geçti — bu test benim
değişikliklerimle **ilgisiz** [Ops/yedekleme modülü] ve kaynak rekabetinden kaynaklanan
geçici bir durum, gerçek bir regresyon değil).

### Nihai sonuç tablosu

| Kriter | Round 1 | Round 6 (son) |
|---|---|---|
| p95 < 500ms | ✅ 349.7ms | ✅ **402.4ms** (ramp), 457.2ms (spike), 34.2ms (soak) |
| p99 < 1s | ✅ 625.7ms | ✅ **672.0ms** (ramp), 662.1ms (spike), 51.6ms (soak) |
| Hata oranı < %1 | ✅ %0.00 | ✅ **%0.00** (üç profilde de) |
| Soak'ta bellek artışı yok | ✅ | ✅ **düz, leak yok** |
| Concurrent update'te veri bozulması yok | ❌ **bulundu** | ✅ **düzeltildi, 3 kez doğrulandı** |

**4/4 kriter Round 6'da sağlandı.** 6 turun tamamı kullanılmadı gerekmedi — kriterler Round 6
(bu değerlendirmenin planlanan son turu) ile karşılandı.

---

## Özet — bulgular ve yapılan değişiklikler

| # | Bulgu | Ciddiyet | Dosya | Şema değişikliği? |
|---|---|---|---|---|
| 1 | Eş zamanlı reschedule'da lost-update / veri bozulması (Lesson'da optimistic concurrency yoktu) | **Kritik** | [UpdateLesson.cs](backend/src/Abdera.Api/Modules/Scheduling/Features/UpdateLesson.cs) | Hayır — `SELECT...FOR UPDATE` pesimist kilit |
| 2 | Npgsql pool + Postgres `max_connections` sınırsız/varsayılan, yük patlamasından dakikalarca sonra dışarıdan DB erişimini kilitliyordu | Orta (yalnızca 500 VU'luk yapay yükte görülür, prod ölçeğinde görülmez) | [docker-compose.yml](docker-compose.yml) | Hayır — bağlantı dizesi + Postgres komut satırı |
| 3 | (Test metodolojisi) Pool boyutu 200'e çıkarılınca bu 8 çekirdekli host'ta performans KÖTÜLEŞTİ - "daha büyük pool daha iyi" varsayımı yanlıştı | Bilgi | [docker-compose.yml](docker-compose.yml) | — (150'de tatlı nokta bulundu, ölçüldü) |

**Kod değişiklikleri (uygulama):**
- [backend/.../UpdateLesson.cs](backend/src/Abdera.Api/Modules/Scheduling/Features/UpdateLesson.cs) — pesimist satır kilidi (transaction + `FOR UPDATE`).
- [backend/.../LoadTestFixtures.cs](backend/src/Abdera.Api/Shared/LoadTestFixtures.cs) — **yeni dosya**, dev-only/AdminOnly sentetik fixture üretici (`POST /api/dev/load-test/seed`), şema değişikliği yok.
- [backend/.../Program.cs](backend/src/Abdera.Api/Program.cs) — tek satır: yukarıdaki endpoint'i Development'ta map eder.
- [docker-compose.yml](docker-compose.yml) — `Maximum Pool Size=150;Connection Idle Lifetime=60` + `db` servisine `max_connections=250`.
- `.env` — **geçici**, yük testi için `RateLimiting__LoginPermitLimit=100000` (k6 tüm VU'ları tek IP'den attığı için gerekliydi) - **yük testi bitince bu iki satırın silinmesi gerekiyor**, `.env` commit'lenmiyor zaten ama unutmamak için burada not düşülüyor.

**Yük testi altyapısı (yeni, uygulamadan bağımsız):** `loadtest/` dizini — k6 script'leri
(`ramp.js`, `concurrent_reschedule.js`, `lib.js`), doğrulama script'i
(`verify-concurrent-reschedule.sh`), sonuçlar (`results/round*.json|log`).

**Test verisi:** Sentetik `loadtest.teacher.N@abdera.local` öğretmenleri (24 adet) ve
`K6Load...Fixture` önekli 143 083 öğrenci satırı dev veritabanında birikti (CRUD akışının
her iterasyonda gerçek `POST /api/students` çağırıp temizlememesi nedeniyle - uygulama hard
delete desteklemiyor). Gerçek kullanıcı verisiyle karışmaz, prod'a hiç dokunulmadı. İstenirse
`firstName LIKE 'K6Load%'` ile temizlenebilir.

**Yapılmayan / onay gerektiren:** Hiçbir migration/şema değişikliği yapılmadı. `Lesson`
tablosuna CLAUDE.md'nin genel kuralına uygun kalıcı bir `xmin`/rowversion eklemek (bu turda
uygulanan pesimist kilidin DAHA UCUZ, DB migration istemeyen bir alternatifi/tamamlayıcısı
olurdu) ayrı bir onay gerektirir — istenirse ayrı bir migration olarak eklenebilir.
