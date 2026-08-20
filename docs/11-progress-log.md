# İlerleme Günlüğü

Oturumlar arası kaldığı yerden devam edebilmek için tutulan çalışma günlüğü. Her faz tamamlandığında buraya bir bölüm eklenir; bir sonraki oturum en üstteki "Devam noktası" bölümünü okuyarak başlar. Tasarım kararları için `10-decisions.md`, API yüzeyi için `07-api.md`, migration sırası için `08-migrations.md` — burada yalnızca "ne yapıldı, ne kaldı, nasıl doğrulandı" tutulur.

## Devam noktası (şu an)

**Faz 6 — Banka entegrasyonu (sanal IBAN) tamamlandı, commit'lendi, push bekliyor.** Kod, testler (13 birim + 5 Testcontainers entegrasyon, tüm suite 141/141 yeşil), frontend "Banka" sayfası, doküman güncellemeleri (`docs/10-decisions.md` E1, yeni `docs/12-bank-integration.md`, `02/03/07/08/09-*.md` ve README güncellemeleri, CLAUDE.md'ye yeni bölüm) ve `docker compose` canlı doğrulaması hepsi bitti. Kullanıcının kendisi istedi (master prompt'ta yoktu) - bkz. "Faz 6" bölümü aşağıda.

- [ ] Commit + push (push için kullanıcı onayı gerekiyor — public repo). Henüz commit edilmedi, bir sonraki adım bu.

Commit sonrası sıradaki faz: **Phase 7 — Gelişim takibi ve hatırlatmalar** (Progress modülünün kalanı: `skill_definitions`/`skill_assessments`/`practice_assignments`, doğum günü ve paket bitiş bildirimleri — `NotificationJobType.Birthday`/`PackageEnding` enum'da tanımlı ama hiçbir use-case üretmiyor, `NotificationMessageBuilder.BuildAsync`'e henüz eklenmedi —, dashboard `GET /api/dashboard/today`). Başlarken önce `docker compose up` ile Faz 5-6'nın hâlâ ayakta olduğunu doğrula, sonra `docs/00-master-prompt.md` + `docs/02-modules.md`'nin Progress bölümünü oku.

**Gerçek sağlayıcı seçimi hâlâ açık soru** (`docs/10-decisions.md` E1) - kullanıcı henüz PayTR/Papara İşletme/banka ürünü arasında karar vermedi, `Banking__Provider=Fake` ile ilerleniyor. Gerçek sağlayıcı seçilince yalnızca `IBankPaymentProvider`'ın yeni bir implementasyonu + `Webhooks.cs`'deki `VerifySharedSecret`'ın o sağlayıcının gerçek imza şemasıyla değiştirilmesi gerekiyor - iş mantığının geri kalanı (eşleştirme, admin çözümleme, testler) değişmez.

## Faz 0 — Tasarım (tamamlandı, push edildi)

`docs/00` – `docs/10` tasarım paketi, `CLAUDE.md`, 4 skill (`abdera-commit`, `abdera-module`, `abdera-migration`, `abdera-notification`). Uygulama kodu yok.

## Faz 1 — Çalışan iskelet (tamamlandı, push edildi)

Auth (cookie oturumu, `PasswordHasher<T>`), health check, Docker Compose (db/api/web), EF Core migration altyapısı, Serilog, `FakeWhatsAppClient` iskeleti, Next.js iskelet.

**Bulunan/düzeltilen gerçek buglar:**
- Alpine imajında eksik `tzdata`/`krb5-libs`/`icu-data-full` → Npgsql/timezone çöküşü
- `Program.cs`'te `Build()` öncesi eager config okuma → `WebApplicationFactory` test override'ını kırıyordu
- Enum'lar JSON'da sayı olarak gidiyordu → `JsonStringEnumConverter` eklendi
- Boş response body (`204`/boş `200`) → frontend `response.json()` çöküyordu
- Data Protection anahtarları kalıcı değildi → her restart'ta oturumlar sessizce geçersiz kalıyordu

## Faz 2 — Kişiler ve takvim (tamamlandı, push edildi)

People modülü (Student/Guardian/Teacher/Instrument/Enrollment), Scheduling modülü (LessonSeries/Lesson/TeacherAvailability/TeacherTimeOff/SchoolCalendarDay), ders serisi üretimi (`LessonGenerator`, pure function).

**Bulunan/düzeltilen gerçek bug:** `.OrderBy()` bir `record`'a projeksiyondan SONRA eklenince EF Core "could not be translated" fırlatıyordu (yalnızca HTTP üzerinden endpoint çağrılınca ortaya çıktı, DB'ye yazılan satır sayılırsa fark edilmiyordu) — `Modules/Scheduling/Features/Calendar.cs`. Kural CLAUDE.md'ye işlendi.

## Faz 3 — Devam, RSVP, ders değişikliği, telafi kredisi (tamamlandı, push edildi)

Attendance modülü (LessonRsvp, LessonAttendance), LessonChangeRequest durum makinesi, MakeupCredit (Billing modülünün ilk parçası — ≥24 saat önce iptal → kredi kuralı).

## Faz 4 — Fiyatlandırma ve aidat (tamamlandı, push edildi)

Pricing modülü (PriceList/PriceListItem, önizlemeli toplu zam), Billing modülü tamamlandı (FeePlan/Receivable/Payment), `OverdueReceivableSweeper` arka plan işi.

**Bulunan/düzeltilen gerçek bug (önemli):** `audit_log.before_json/after_json` (jsonb) kolonlarına yazılan metin string interpolation ile kuruluyordu (`$"{{\"amount\":{tutar}}}"`) — konteynerin/OS'un kültürü tr-TR olduğunda `decimal.ToString()` virgüllü ondalık üretip geçersiz JSON'a, dolayısıyla `DbUpdateException`/500'e yol açıyordu. Canlı `docker compose` testinde bulundu. Düzeltme: her yerde `JsonSerializer.Serialize(new {...})` + `Program.cs`'te `CultureInfo.InvariantCulture` varsayılan thread kültürü olarak sabitlendi (savunma katmanı). CLAUDE.md'ye kalıcı kural olarak işlendi.

## Faz 5 — WhatsApp (tamamlandı, push edildi)

### Yapılanlar

- **Messaging modülü** (`Modules/Messaging/`): `Domain/` (NotificationJob, NotificationJobType, WhatsAppMessage, WhatsAppWebhookEvent, MessageTemplate, RsvpButtonPayload, WebhookSignatureVerifier, QuietHours, IWhatsAppClient), `Features/` (NotificationScheduler, NotificationMessageBuilder, Webhooks, DeterministicIntents, Notifications — admin liste/retry, DevWhatsAppSimulator — dev-only), `Infrastructure/` (FakeWhatsAppClient, CloudApiWhatsAppClient, NotificationDispatcher — `BackgroundService`), `Persistence/` (4 `IEntityTypeConfiguration`), `MessagingModule.cs` (DI + endpoint kaydı).
- **Diğer modüllerin entegrasyonu:** `LessonSeriesFeatures.CreateAsync/GenerateAsync` ders üretirken `LessonReminder` job'ı kuruyor; `CancelLesson`/`ChangeRequests.ApproveAsync` bekleyen job'ları iptal edip gerekirse yenisini kuruyor (A4); `MakeupCredits.cs` `MakeupApproved` bildirimi kuruyor; `Billing/Features/SendPaymentReminder.cs` admin'in elle aidat hatırlatması tetiklemesini sağlıyor.
- **Migration'lar** (`Persistence/Migrations/`): `20260820094010_Messaging` (4 tablo + `UNIQUE(type,reference_type,reference_id)` + `UNIQUE(provider_event_id)` + indexler), `20260820094055_SeedMessageTemplates` (4 şablon: `lesson_reminder_rsvp` docs/06'daki tam metin, diğer üçü kendi taslağımız — hepsi Meta onayı bekliyor, D2).
- **Dockerfile düzeltmesi (gerçek bug, bu oturumda bulundu):** Microsoft'un resmi `aspnet:10.0-alpine` imajı `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`'yu varsayılan taşıyor — Phase 1'de kurulan `icu-data-full` paketi bu env değişkeni açıkken hiç kullanılmıyordu. İlk kez Faz 5'te `new CultureInfo("tr-TR")` çağrısı (WhatsApp mesaj metni biçimlendirme) gerçek trafikte çalıştırılınca `CultureNotFoundException` ile ortaya çıktı — `NotificationDispatcher` job'ları sessizce `FAILED`'a düşürüyordu. Düzeltme: `Dockerfile`'a `ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` eklendi.

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

1. 7 migration (Faz 1-5 birikimli) sıfır veritabanında hatasız uygulandı, `message_templates` 4 satırla seed edildi.
2. Admin login → teacher/student/guardian/enrollment/lesson-series oluşturuldu → 11 ders üretildi → 11 `LessonReminder` job'ı doğru `scheduled_at` (ders saatinden 60 dk önce) ile kuruldu.
3. En yakın job'un `scheduled_at`'i elle geçmişe çekildi → `NotificationDispatcher` (1 dakikalık `PeriodicTimer`) job'ı `FOR UPDATE SKIP LOCKED` ile aldı → **globalization bug'ı burada yakalandı** → Dockerfile düzeltildi, `api` yeniden build edildi → job ikinci denemede `Sent` oldu, `whatsapp_messages`'a outbound kayıt düştü (Fake client log'u doğrulandı).
4. `POST /api/dev/whatsapp/simulate-rsvp` (imzalı buton payload'ı) → `lesson_rsvps` tablosuna doğru `guardian_id`/`response`/`source=WhatsApp` yazıldı.
5. `POST /api/dev/whatsapp/simulate-text` ile tüm deterministik intent'ler (`ders`/`aidat`/`telafi`/`okula yaz`/bilinmeyen mesaj → sessiz kalma) ve opt-out (`dur`) canlı denendi — rıza kapandı, 10 bekleyen job iptal oldu, tek teyit mesajı yazıldı, `audit_log`'a `guardian.opted_out` düştü.
6. Webhook imza reddi (401, sahte imza) ve `RetryManually`'nin geçersiz durumdan çağrılması (409 ProblemDetails) doğrulandı.
7. Frontend "Bildirimler" sayfası (`/dashboard/notifications`) tarayıcıda gerçek admin oturumuyla denendi: filtre butonları, hata mesajı tooltip'i, "Yeniden dene" butonu (Failed → Pending) çalışıyor.

### Bu oturumda düzeltilen diğer bug

`Webhooks.cs`'nin `HandleTextMessageAsync`'i derlenmeyen bir çağrı içeriyordu (`DeterministicIntents.TryResolve` — böyle bir metot yok, gerçek metot `ResolveAsync`). Düzeltildi: `IWhatsAppClient` enjekte edildi, `ResolveAsync` sonucu `SendFreeTextAsync` ile gerçekten gönderiliyor ve outbound `WhatsAppMessage` olarak loglanıyor. `CloudApiWhatsAppClient`'a da `SendFreeTextAsync` eklendi (iki implementasyon senkron kalsın diye — `abdera-notification` skill kuralı).

### Testler (yazıldı, hepsi yeşil)

- **Birim** (`Unit/MessagingDomainTests.cs`, 27 test): `NotificationJob` durum geçişleri (Claim/MarkSent/MarkFailed/Cancel/Reschedule/RetryManually + geçersiz geçiş `ConflictException`), `QuietHours.IsWithinQuietHours`/`ResolveSendTime` (gece yarısını saran pencere dahil), `RsvpButtonPayload.Sign/TryVerify` (kurcalanmış payload reddi — signature'ı koruyup referenceToken'ı değiştirme senaryosu dahil), `WebhookSignatureVerifier.IsValid` (geçerli/geçersiz/eksik imza).
- **Entegrasyon** (`Integration/MessagingFlowTests.cs`, Testcontainers, 12 test): ders serisi oluşturunca her ders için `LessonReminder` job'ı kuruluyor, regenerate mükerrer açmıyor (unique kısıt), rızası kapalı veliye job açılmıyor, ders iptali bekleyen job'ı iptal ediyor, değişiklik talebi onayı eski job'ı iptal edip yeni `LessonReminder` + `LessonRescheduled` job'ları açıyor, `NotificationDispatcher` gerçekten Fake client üzerinden gönderip `Sent`'e çekiyor (test'te `Notifications:DispatchIntervalSeconds=1` override'ı ile - gerçek 60sn beklemeye gerek yok), RSVP buton webhook'u `LessonRsvp` yazıyor, deterministik intent'ler (`ders`/`okula yaz`) doğru metni outbound olarak kaydediyor, opt-out rızayı kapatıp bekleyen job'ları iptal edip tek teyit mesajı yazıyor, webhook geçersiz imzada 401 dönüyor, webhook aynı `provider_event_id`'yi iki kez işlemiyor.
- **Bilinçli test boşluğu:** Sessiz saatin gerçek dispatch anındaki davranışı (A6) yalnızca birim testinde (`QuietHours` saf fonksiyonları) kapsanıyor - `IClock` bu projede gerçek `SystemClock`, testte "şu an sessiz saat içinde" durumunu deterministik kuramıyoruz. Riski düşük: dispatcher'daki çağrı tek satırlık düz bir if (`QuietHours.AppliesTo` + `IsWithinQuietHours` + `ResolveSendTime`), üçü de ayrı ayrı birim testli.

### Dockerfile/globalizasyon bug'ının test körlüğü (önemli, kalıcı not)

Yerel `dotnet test` bu bug'ı **hiç yakalayamaz** çünkü test host'u macOS/Linux'ta çalışıyor (ICU zaten var) - bug yalnızca Alpine + `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true` kombinasyonunda ortaya çıkıyor. Bu sınıf bug'ı yakalamanın tek yolu gerçek `docker compose up` canlı doğrulaması - CLAUDE.md'ye yeni bir kural olarak işlendi ("Kullanıcıya gösterilecek metinde `new CultureInfo(...)` kullanıyorsan Dockerfile'ı kontrol et").

### Tamamlanan diğer işler

- **Frontend:** `/dashboard/notifications` sayfası (`frontend/src/app/dashboard/notifications/page.tsx` + `frontend/src/lib/messaging.ts`) - durum filtresi, hata mesajı tooltip'i, `FAILED` için "Yeniden dene" butonu. `app-header.tsx`'e admin-only link eklendi. Tarayıcıda gerçek admin oturumuyla denendi (bkz. yukarıdaki madde 7).
- **Doküman:** `docs/07-api.md` (yeni uç noktalar ✅), `docs/02-modules.md` (Messaging bağımlılık yönü gerçeğe göre düzeltildi), `docs/08-migrations.md` (Messaging/SeedMessageTemplates migration'ları + seed içeriği eklendi, Progress'in önüne geçtiği not düşüldü), `README.md` (Faz 5 durum paragrafı + zamanlayıcı/dev-endpoint satırları güncellendi), `frontend/src/app/dashboard/page.tsx` (Faz 4-5 için kalan eski placeholder metni güncellendi).

### Bu oturumda düzeltilen diğer bug

`Webhooks.cs`'nin `HandleTextMessageAsync`'i derlenmeyen bir çağrı içeriyordu (`DeterministicIntents.TryResolve` — böyle bir metot yok, gerçek metot `ResolveAsync`). Düzeltildi: `IWhatsAppClient` enjekte edildi, `ResolveAsync` sonucu `SendFreeTextAsync` ile gerçekten gönderiliyor ve outbound `WhatsAppMessage` olarak loglanıyor. `CloudApiWhatsAppClient`'a da `SendFreeTextAsync` eklendi (iki implementasyon senkron kalsın diye — `abdera-notification` skill kuralı).

### Git

Commit `a0ef126` + doküman düzeltmesi `da7f94b` olarak `main`'e push edildi (kullanıcı onayıyla).

**Ortam notu:** Bu oturumda yerel test için repo kökünde bir `.env` oluşturuldu (gerçek `.gitignore`'lu, commit'lenmeyecek, sahte/dev değerleriyle: `WhatsApp__Provider=Fake`, `Bootstrap__AdminEmail=admin@example.com` vb.). Sonraki oturumlarda aynı `.env` duruyorsa tekrar oluşturmaya gerek yok; duruyor mu diye önce kontrol et.

## Faz 6 — Banka entegrasyonu / sanal IBAN (tamamlandı, push bekliyor)

Kullanıcının kendi isteğiyle başladı ("gönderen ismi ile para ibana yattığında otomatik ödemeyi uygulamaya yansıtma gibi bir şey olur mu?") — master prompt'ta yoktu, hatta açıkça hariç tutulmuştu ("do not add ... bank reconciliation, bank integration"). Üç seçenek sunuldu (isim eşleştirme/sanal IBAN/ekstre içe aktarma), kullanıcı **sanal IBAN + tam otomasyon**'u seçti, ardından **gerçek sağlayıcıyı henüz seçmeden Fake ile ilerlemeyi** ve **Progress modülünden önce Phase 6 olarak** yapılmasını seçti (bkz. `docs/10-decisions.md` E1).

### Neden isim eşleştirmesi değil

Gönderen adı tek başına güvenilmez (farklı hesaptan gönderim, aynı isimli birden fazla veli, ad/soyad varyasyonu) - para söz konusu olduğunda yanlış eşleştirme kabul edilemez. Sanal IBAN "hangi veli" sorusunu kesin cevaplıyor, geriye yalnızca "hangi Receivable" sorusu kalıyor - o da tutar (+ opsiyonel açıklama-dönem ipucu) ile çözülüyor, **belirsizse asla tahmin edilmiyor**.

### Yapılanlar

- **Banking modülü** (`Modules/Banking/`): `Domain/` (`VirtualIban`, `BankIncomingTransaction`, `IBankPaymentProvider`, `PaymentMatcher` - saf eşleştirme fonksiyonu), `Features/` (`AssignVirtualIban`, `Webhooks` - hem gerçek webhook hem dev simülatörünün çağırdığı ortak işleme fonksiyonu, `BankTransactions` - admin liste/resolve, `DevBankSimulator` - dev-only), `Infrastructure/` (`FakeBankPaymentProvider`), `Persistence/` (2 `IEntityTypeConfiguration`), `BankingModule.cs`.
- **`Payment.CreatedBy` nullable'a çevrildi** - otomatik eşleşen ödemelerde bir admin yok, `AuditLog.ActorUserId`'nin sistem-olaylarını `null` işaretleme kuralına uyumlu hale getirildi (migration additive, veri kaybı yok).
- **Eşleştirme algoritması** (`PaymentMatcher.Match`, saf fonksiyon, DB'ye bağımlı değil): açıklamada `YYYY-MM` deseni bir tek adayı işaret ediyorsa ve tutar o adayın kalan bakiyesini karşılıyorsa → o adaya; yoksa tutar tam olarak yalnızca bir açık `Receivable`'ın kalan bakiyesine eşitse → ona; ikisi de net değilse → `NeedsReview`.
- **Migration** (`20260820130609_Banking`): `virtual_ibans` (`UNIQUE(iban)`), `bank_incoming_transactions` (`UNIQUE(provider, provider_transaction_id)`, `CHECK(amount>0)`), `payments.created_by` nullable'a çevrildi.
- **Dev-only simülatör:** `POST /api/dev/bank/simulate-transaction` gerçek webhook'un çağıracağı aynı `Webhooks.ProcessIncomingTransactionAsync`'i çağırıyor - iki yol hiç sapmıyor (WhatsApp'taki simulate-text/simulate-rsvp ile aynı desen).
- **Gerçek webhook** (`POST /api/webhooks/bank`): imza şeması henüz seçilmedi (sağlayıcı seçilmedi), bu yüzden paylaşılan-sır (`Banking__WebhookSharedSecret` + `X-Bank-Webhook-Secret` başlığı, sabit-zamanlı karşılaştırma) ile korunuyor - sağlayıcı seçilince gerçek imza şemasına değiştirilecek tek nokta burası.
- **Frontend:** `/dashboard/banking` sayfası - veliye sanal IBAN atama (zaten atanmışsa gösterme), gelen işlemler listesi (durum filtresi), `NeedsReview` için "Bu aidata say" (Receivable ID gir) / "Hiçbirine sayma" aksiyonları.

### Testler (yazıldı, hepsi yeşil)

- **Birim** (`Unit/BankingDomainTests.cs`, 13 test): `VirtualIban.Deactivate` iki kez çağrılamaz, `BankIncomingTransaction` durum makinesi (`Matched`'ten sonra tekrar eşleşme/`Ignore` reddi, `NeedsReview`'dan `Matched`'e geçiş serbest), `PaymentMatcher`'ın tüm dallı yolu (tek net eşleşme, iki aynı-tutarlı aday belirsiz kalır, açıklama-dönem eşleşmesi amount-only'e önceliklidir, açıklama eşleşmesi kalan bakiyeyi karşılamazsa reddedilir, açıklamadaki dönem adaylar arasında yoksa amount-only'e düşer).
- **Entegrasyon** (`Integration/BankingFlowTests.cs`, 5 test, Testcontainers): ikinci aktif sanal IBAN ataması 409, net tutar eşleşmesi otomatik `Payment` + `Receivable` güncelleme (`CreatedBy=null`), **belirsiz tutar hiçbir Receivable'a dokunmadan NeedsReview'da kalır** (en kritik test), admin elle çözebilir (`CreatedBy`=admin), aynı `provider_transaction_id` iki kez işlenmez.
- **Test yazarken bulunan/düzeltilen hatalar (ürün bug'ı değil, test izolasyonu):** Aynı test sınıfındaki birden fazla `SeedReceivableAsync` çağrısı aynı enstrüman+süre+tip+tarih aralığıyla `price_list_items` çakışma kısıtına takılıyordu (durationMinutes'i `Interlocked.Increment` ile tekilleştirildi); birkaç assertion tüm tabloyu filtresiz `SingleAsync()`/`CountAsync()` ile sorguluyordu - `IClassFixture` aynı DB'yi sınıftaki TÜM testler arasında paylaştığı için başka testlerin satırlarını da yakalıyordu, ilgili id'ye göre filtrelenerek düzeltildi.

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

1. Migration sıfırdan değil, Faz 1-5'in üzerine (mevcut veritabanına) hatasız uygulandı.
2. Teacher/student/guardian/enrollment/price-list/fee-plan/receivable oluşturuldu, veliye sanal IBAN atandı (`POST /api/guardians/{id}/virtual-iban`) → ikinci atama denemesi doğru şekilde 409 döndü.
3. `POST /api/dev/bank/simulate-transaction` ile tam tutarlı bir işlem gönderildi → `Receivable` `Paid`'e geçti, `Payment` (`method=Transfer`, `created_by=null`) oluştu, `bank_incoming_transactions.status=Matched`, `audit_log`'a `receivable.auto_payment_matched` düştü.
4. Yeni bir dönem için ikinci bir `Receivable` açılıp kasıtlı yanlış tutarlı bir işlem gönderildi → `NeedsReview`'da kaldı, hiçbir `Receivable` etkilenmedi.
5. `GET /api/bank-transactions?status=NeedsReview` + `POST /api/bank-transactions/{id}/resolve` ile admin elle çözdü → `Receivable` `Partial`'a geçti, bu sefer `Payment.created_by` gerçek admin id'si oldu.
6. Aynı `providerTransactionId` iki kez gönderildi → yalnızca bir `bank_incoming_transactions` satırı oluştu (idempotency).
7. Gerçek webhook'a paylaşılan sır olmadan istek atıldı → 401.
8. Frontend `/dashboard/banking` sayfası tarayıcıda gerçek admin oturumuyla denendi: veli seçince atanmış IBAN görünüyor, `NeedsReview` listesinden "Hiçbirine sayma" ile bir işlem gerçekten `Ignored`'a düştü ve listeden kayboldu.

### Kalan iş: Git

Henüz commit yok - bir sonraki adım commit + kullanıcı onayıyla push.
