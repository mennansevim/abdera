# Abdera — Çalışma Kuralları

Bu dosya kod yazarken tekrar tekrar anlatılmaması gereken mimari kararları sabitler. Tasarım gerekçeleri için `docs/10-decisions.md`; alan modeli için `docs/02-modules.md` ve `docs/03-erd.md`.

## Stack

.NET 10 (LTS) · ASP.NET Core Minimal API · EF Core 10 + Npgsql · PostgreSQL 16 · FluentValidation · `PasswordHasher<T>` + httpOnly cookie oturumu (tam ASP.NET Core Identity değil — kendi minimal `users` tablomuz var, bkz. `docs/03-erd.md`) · Serilog · Next.js 16 (App Router) + TypeScript + Tailwind + TanStack Query (shadcn/ui Phase 2'de UI büyüyünce eklenecek).

Yeni bağımlılık eklemeden önce sor: bu ölçekte (6–8 öğretmen, ~150 öğrenci) gerçekten gerekli mi, yoksa `BackgroundService` / `DbContext` / dahili bir sınıfla zaten çözülüyor mu? Şüpheye düşersen eklemeden bırak, `docs/10-decisions.md`'ye not düş.

## Backend yapısı — modül başına dikey dilim

Master prompt'un `api/application/domain/infrastructure` dört katmanını **kullanma**. Bunun yerine modül başına:

```
Modules/Billing/
  Domain/          # entity, enum, invariant — Spring MVC/EF'e bağımlı değil
  Features/        # her use-case tek dosya: request + handler + endpoint kaydı
    RecordPayment.cs
    SendPaymentReminder.cs
  Persistence/      # IEntityTypeConfiguration<T>, seed data
```

- **Repository pattern yok.** `AbderaDbContext` zaten Unit of Work + Repository; handler'lar doğrudan ona bağımlı.
- Tek `AbderaDbContext`, modül başına ayrı context açma.
- Modüller arası erişim EF navigation property üzerinden değil, açık bir servis/sorgu üzerinden olur — `Billing`, `Scheduling`'in iç entity'lerine doğrudan join atmaz.
- Use-case büyüdüğünde dosyayı böl, ama "her katman için bir dosya" diye önden bölme.

## Veri kuralları

- Para: `decimal(12,2)` + ayrı `currency` kolonu. **`double`/`float` asla.**
- Zaman: veritabanında her zaman `timestamptz` (UTC instant). Yerel gösterim/hesaplama `Europe/Istanbul` ile uygulama katmanında yapılır, saat dilimi konfigürasyondan okunur — hardcode etme.
- Dışa açık kaynak id'leri: UUID. Sıralı int public API'de görünmez.
- Mutasyona açık her tabloda `created_at`, `updated_at`. Eşzamanlı düzenleme riski olan tablolarda optimistic concurrency (`xmin` veya `rowversion` kolonu).
- Finansal/audit kayıt **silinmez** — durum kolonu veya soft delete kullanılır (`CancelledAt`, `Status=INACTIVE`).
- Para, takvim ve rıza (consent) değiştiren her use-case `audit_log`'a yazar: kim, ne zaman, hangi kayıt, önceki/yeni değer.

## Kritik veritabanı kısıtları (bunları migration'dan düşürme)

```
UNIQUE (lesson_series_id, start_at)                         -- mükerrer ders üretimi engeli
UNIQUE (type, reference_type, reference_id) ON notification_jobs   -- idempotency anahtarı
UNIQUE (provider_event_id) ON whatsapp_webhook_events
UNIQUE (enrollment_id, period) ON receivables
CHECK (end_at > start_at)
CHECK (amount >= 0)
```

`price_list_items`: aynı enstrüman × ders süresi için çakışan yürürlük tarihi aralığı olamaz — uygulama katmanında kontrol edilir (aralık çakışması genel `EXCLUDE` kısıtıyla ifade edilemeyecek kadar tabloya özeldir).

## İş kuralları — kodda unutulmaması gerekenler

- **Fiyat snapshot'ı:** `Receivable` oluşurken tutar `PriceListItem`'dan kopyalanır ve birlikte saklanır (`priceListItemId` + `amount`). Sonraki bir zam geçmiş `Receivable`'ları değiştirmez.
- **Ders değişince eski job iptali:** bir `Lesson` `RESCHEDULED`/`CANCELLED` olduğunda, o derse bağlı bekleyen (`PENDING`) `NotificationJob` iptal edilir ve gerekiyorsa yeni saate göre yenisi kurulur. Bu invariant'ı bozan her değişiklik testle korunmalı.
- **Telafi kredisi:** dersten ≥24 saat önce iptal edilirse `MakeupCredit` oluşur (kaynak ders + son kullanma tarihi ile). Habersiz gelmeme (no-show) kredi doğurmaz, ücret tahakkuk eder.
- **Sessiz saat:** aidat hatırlatması ve doğum günü mesajı gibi zamanlanmış (cron kaynaklı) bildirimler yalnızca `Notifications__QuietHoursStart/End` penceresinde gönderilir; pencere dışı job bir sonraki pencere başına ötelenir. Ders hatırlatması (dersten 1 saat önce) bu kurala tabi değil.
- **WhatsApp 24 saatlik pencere:** `Guardian.conversationWindowExpiresAt` her gelen mesajda yenilenir. Pencere kapalıyken serbest metin yerine onaylı template kullanılır.
- **Opt-out:** gelen mesaj `dur`/`iptal`/`stop` içeriyorsa rıza kapatılır, bekleyen job'lar iptal edilir, tek teyit mesajı gönderilir.

## Program.cs'te konfigürasyon okuma kuralı

`WebApplicationFactory` (test altyapısı) konfigürasyon override'ını yalnızca `builder.Build()` çağrısı **sırasında/sonrasında** uygular. `Program.cs`'te `builder.Configuration`'ı `Build()`'den önce senkron okuyup bir karara/istisnaya bağlarsan (`var x = builder.Configuration["..."] ?? throw ...`), testler bu değeri asla göremez ve kırılır.

Kural: bağlantı dizesi, cookie ayarları, CORS origin gibi değerleri **DI çözümlenirken** (`AddDbContext<T>((sp, options) => ...)`, `.AddCookie(options => ...)` gibi deferred delegate'ler içinde) oku, üstte `var` ile eager okuma. İstisna: bir arayüzün hangi somut sınıfa bağlanacağı (`IWhatsAppClient` → `Fake`/`Cloud`) gibi **yapısal DI kayıtları** `Build()`'den önce karar verilmek zorunda — bunlar gerçek ortam değişkenlerini görür, yalnızca `WebApplicationFactory`'nin test-time overlay'ini göremez; bu bilinen ve kabul edilmiş bir sınır, `Program.cs`'teki yorum satırına bak.

## Çok tablolu sorgularda OrderBy sırası

Birden fazla `Join` sonrası özel bir `record`'a projekte eden (`new LessonResponse(...)`) bir sorguda `.OrderBy(...)`'ı bu projeksiyondan **sonra** koyma — EF Core, bir record constructor'ının alanına göre sıralamayı SQL'e çeviremez ve çalışma zamanında "could not be translated" istisnası fırlatır (`Program.cs` build zamanı yakalamaz, yalnızca o satır ilk çalıştığında patlar — testlerde bu endpoint'i gerçekten HTTP üzerinden çağırmazsan fark edilmez, bkz. `Modules/Scheduling/Features/Calendar.cs` ve `PeopleAndSchedulingFlowTests.cs`).

Doğru sıra: `Join(...).Join(...).OrderBy(x => x.Entity.Alan).Select(x => new Response(...))` — sıralama her zaman son projeksiyondan **önce**, ham/anonim ara tip üzerinde yapılır. Yeni bir sorgu handler'ı yazarken bir `record`'a projekte edip hemen ardından `OrderBy` eklemeden önce bunu hatırla; mümkünse handler'ı gerçek bir `WebApplicationFactory` testiyle en az bir kez HTTP üzerinden çağırarak doğrula (yalnızca DB'ye yazılan satırı saymak yetmez).

## Elle JSON string'i asla string interpolation ile kurma

`audit_log.before_json/after_json` gibi `jsonb` kolonlarına yazılacak metni **asla** `$"{{\"amount\":{tutar}}}"` gibi string interpolation ile kurma — bir `decimal`'i interpolate etmek makinenin/konteynerin işletim sistemi kültürüne bağımlıdır (örn. tr-TR'de `1200,00`), bu da geçersiz JSON üretip `DbUpdateException` ile 500'e yol açar (gerçek bir prod bug'ı olarak bulundu, bkz. `BulkUpdate.cs`/`Payments.cs`/`MarkAttendance.cs` git geçmişi). Ayrıca elle kaçış (escaping) yapılmadığı için bir metin alanı tırnak/backslash içerirse de bozulur.

Kural: her zaman `System.Text.Json.JsonSerializer.Serialize(new { ... })` kullan — hem kültürden bağımsızdır (JSON sayıları her zaman `.` ile yazar) hem kaçışı otomatik yapar. Program.cs ayrıca uygulamanın varsayılan thread kültürünü `CultureInfo.InvariantCulture`'a sabitler (savunma katmanı) — ama bu, `JsonSerializer` kullanma kuralının yerini tutmaz, yalnızca ek güvenlik.

## Kullanıcıya gösterilecek metinde `new CultureInfo("tr-TR")` kullanıyorsan Dockerfile'ı kontrol et

WhatsApp mesajı/tarih-para gösterimi gibi **veliye görünecek** metinlerde (yukarıdaki JSON kuralının tersine) `CultureInfo.GetCultureInfo("tr-TR")` ile açık biçimlendirme doğru ve gereklidir — ama Microsoft'un resmi `mcr.microsoft.com/dotnet/aspnet:*-alpine` imajı `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`'yu **varsayılan** taşır. Bu env değişkeni açıkken Dockerfile'da `icu-data-full` kurulu olsa bile .NET ICU'yu hiç yüklemez ve herhangi bir adlandırılmış kültür çağrısı (`new CultureInfo("tr-TR")` / `CultureInfo.GetCultureInfo("tr-TR")`) `CultureNotFoundException` fırlatır — yalnızca invariant kültür desteklenir.

Gerçek bir prod bug'ı olarak Faz 5'te bulundu: `NotificationDispatcher`, WhatsApp mesaj metnini tr-TR formatında biçimlendirmeye çalışırken bu istisnayı fırlatıp job'ları sessizce `FAILED`'a düşürüyordu — yerel test koşusu (macOS/Linux, Alpine değil) bunu hiç yakalamadı, yalnızca `docker compose up` ile canlı denemede ortaya çıktı. Kural: Dockerfile'da `icu-data-full` kurulumundan hemen sonra `ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` satırının durduğunu doğrula — biri diğeri olmadan işe yaramaz. Yeni bir named-culture çağrısı eklerken mutlaka `docker compose up` ile canlı doğrula, birim testi bu sınıf bug'ı yakalamaz (bkz. `docs/11-progress-log.md` Faz 5 notu).

## WhatsApp entegrasyonu

`IWhatsAppClient` arayüzünün iki implementasyonu vardır:
- `FakeWhatsAppClient` — mesajı veritabanına yazar + loglar, Meta hesabı gerektirmez. **Dev/test varsayılanı.**
- `CloudApiWhatsAppClient` — gerçek Meta Cloud API.

Provider seçimi `WhatsApp__Provider` ortam değişkeninden gelir; kod içinde hardcode edilmez. Yeni bildirim tipi eklerken her iki implementasyonu da güncelle — bkz. `abdera-notification` skill'i.

Webhook imzası (`X-Hub-Signature-256`) her istekte doğrulanır, doğrulanamayan istek reddedilir. Buton payload'larında tahmin edilebilir dahili id kullanılmaz — imzalı/opak referans (`WhatsApp__PayloadSigningKey`) kullanılır.

## Banka entegrasyonu (Faz 6, `docs/10-decisions.md` E1)

Master prompt'un başlangıçta hariç tuttuğu ("do not add ... bank reconciliation, bank integration") bir kapsam — kullanıcının açık onayıyla eklendi, kapsamı yalnızca **gelen havalenin otomatik `Receivable`'a işlenmesi** (sanal IBAN). Online ödeme/checkout, e-fatura, muhasebe entegrasyonu hâlâ kapsam dışı.

`IBankPaymentProvider` arayüzünün WhatsApp'takiyle aynı ikili yapısı vardır:
- `FakeBankPaymentProvider` — sahte bir IBAN üretir, gerçek bir sağlayıcı hesabı gerektirmez. **Dev/test varsayılanı.**
- Gerçek sağlayıcı (PayTR/Papara İşletme/banka Sanal IBAN ürünü) **henüz seçilmedi** — seçilince yeni implementasyon eklenir, `Banking` modülünün geri kalanı değişmez.

**Kritik iş kuralı — belirsizlikte asla otomatik davranma.** Gelen bir banka işlemi bir veliye (sanal IBAN üzerinden) kesin bağlanır, ama hangi `Receivable`'a sayılacağı yalnızca **tek bir net aday** varsa (tutar birebir eşleşiyor, ya da açıklamadaki dönem tekil bir adayı işaret ediyor) otomatik işlenir. Birden fazla aday veya hiç aday yoksa işlem `NeedsReview`'da kalır, admin elle çözer. Bu kuralı gevşetmek (örn. "en yakın tutara say") parada yanlış öğrenciye/aidata ödeme yazma riski taşır — ayrıntı ve algoritma: `docs/12-bank-integration.md`.

Otomatik eşleşen ödemelerde `Payment.CreatedBy` `null`'dır (bir admin yok) — `AuditLog.ActorUserId`'nin sistem-kaynaklı olayları `null` işaretlemesiyle aynı kural.

## Test stratejisi

- Zamanlama, RSVP, ücret hesaplama, ders-değişikliği onay kuralları → **saf birim testi**, gerçek veritabanı gerekmez.
- Testcontainers **yalnızca** gerçek Postgres davranışı gerektiren durumlarda: migration'lar, `FOR UPDATE SKIP LOCKED` yarışı, unique kısıt ihlalleri, webhook idempotency.
- Her yeni use-case en az bir mutlu yol + bir invariant-ihlali testiyle gelir.

## Dil

Kod, tip adı, değişken adı, commit mesajı gövdesi: **İngilizce**. Kullanıcı arayüzü metni ve WhatsApp mesaj şablonları: **Türkçe**. Domain terimleri için `docs/01-glossary.md`'deki eşleşmeyi kullan (örn. aidat → `Receivable`, telafi → `MakeupCredit`, veli → `Guardian`).

## Yapılmayacaklar

Mikroservis, Kafka/RabbitMQ/Redis/Kubernetes, Hangfire/Quartz gibi ek zamanlayıcı kütüphanesi, Repository pattern katmanı, sıfır implementasyonlu spekülatif arayüz (örn. AI özet arayüzü Phase 6'dan önce açılmaz), veli/öğrenci mobil uygulaması, online ödeme/e-fatura entegrasyonu — bunların hiçbiri MVP kapsamında değil ve eklenmesi `docs/10-decisions.md` üzerinden açıkça onaylanmadan yapılmaz.

**Not (E1 — kısmi istisna):** "Banka entegrasyonu" genel olarak yasak listesindeydi, ama kullanıcı Faz 6'da yalnızca **gelen havalenin otomatik `Receivable`'a işlenmesini** (sanal IBAN) açıkça onayladı — bkz. `docs/10-decisions.md` E1, `docs/12-bank-integration.md`. Online ödeme/checkout (veli sitede kart girip ödeme yapması), e-fatura, muhasebe entegrasyonu **hâlâ yasak** — bunlar E1'in onayladığı kapsamın dışında, ayrı bir onay gerektirir.
