# İlerleme Günlüğü

Oturumlar arası kaldığı yerden devam edebilmek için tutulan çalışma günlüğü. Her faz tamamlandığında buraya bir bölüm eklenir; bir sonraki oturum en üstteki "Devam noktası" bölümünü okuyarak başlar. Tasarım kararları için `10-decisions.md`, API yüzeyi için `07-api.md`, migration sırası için `08-migrations.md` — burada yalnızca "ne yapıldı, ne kaldı, nasıl doğrulandı" tutulur.

## Devam noktası (şu an)

**Faz 5 — WhatsApp tamamlandı ve `main`'e push edildi** (commit `a0ef126`). Kod, testler (birim + Testcontainers entegrasyon, 123/123 yeşil), frontend "Bildirimler" sayfası, doküman güncellemeleri ve `docker compose` canlı doğrulaması hepsi bitti.

Sıradaki faz: **Phase 6 — Gelişim takibi ve hatırlatmalar** (Progress modülünün kalanı: `skill_definitions`/`skill_assessments`/`practice_assignments`, doğum günü ve paket bitiş bildirimleri — `NotificationJobType.Birthday`/`PackageEnding` enum'da tanımlı ama hiçbir use-case üretmiyor, `NotificationMessageBuilder.BuildAsync`'e henüz eklenmedi —, dashboard `GET /api/dashboard/today`). Başlarken önce `docker compose up` ile Faz 5'in hâlâ ayakta olduğunu doğrula, sonra `docs/00-master-prompt.md` + `docs/02-modules.md`'nin Progress bölümünü oku.

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

## Faz 5 — WhatsApp (tamamlandı, push bekliyor)

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

### Kalan tek iş: Git

Henüz commit yok. Bir sonraki oturum: `git add -A`, Türkçe commit mesajı (temp dosya + `git commit -F` — apostrof/Türkçe karakter kaçış sorunları için), push öncesi kullanıcıdan onay (`AskUserQuestion`, public repo).

**Ortam notu:** Bu oturumda yerel test için repo kökünde bir `.env` oluşturuldu (gerçek `.gitignore`'lu, commit'lenmeyecek, sahte/dev değerleriyle: `WhatsApp__Provider=Fake`, `Bootstrap__AdminEmail=admin@example.com` vb.). Bir sonraki oturumda aynı `.env` duruyorsa tekrar oluşturmaya gerek yok; duruyor mu diye önce kontrol et.
