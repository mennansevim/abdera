# İlerleme Günlüğü

Oturumlar arası kaldığı yerden devam edebilmek için tutulan çalışma günlüğü. Her faz tamamlandığında buraya bir bölüm eklenir; bir sonraki oturum en üstteki "Devam noktası" bölümünü okuyarak başlar. Tasarım kararları için `10-decisions.md`, API yüzeyi için `07-api.md`, migration sırası için `08-migrations.md` — burada yalnızca "ne yapıldı, ne kaldı, nasıl doğrulandı" tutulur.

## Devam noktası (şu an)

**`redesign/sicak-atolye` dalı — Faz 1-4'ün tamamı tamamlandı (docs/15-product-phases.md). Faz 1-3 push edildi; Faz 4 bu turda eklendi, henüz commit edilmedi.**

### Faz 4 — Sağlık kontrolü, yedekleme, e-posta alarmı

Yeni `Modules/Ops/` modülü: `BackupRun`/`SystemHealthStatus` (domain), `IBackupStorage`/`IEmailSender` portları + `Fake`/gerçek ikili implementasyonları (WhatsApp/Banking ile aynı desen), `BackupService`/`SystemHealthMonitor` (`BackgroundService` - CLAUDE.md'nin Hangfire/Quartz yasağı nedeniyle, Faz 3'teki gibi mevcut zamanlayıcı deseni korundu), `GET/POST /api/backup-runs`, `GET /api/system/health`. Kullanıcı kararları: yedekleme hedefi **kendi sunucusuna SFTP/SSH** (Google Drive/S3 alternatifleri sunuldu, SFTP seçildi), saklama süresi **1 ay, parametrik** (`Backup__RetentionDays`), e-posta **"en kolay" - Gmail SMTP** önerildi. Ayrıntılı kararlar: `docs/10-decisions.md` G.

**Teknik seçimler:** SFTP için SSH.NET (Renci.SshNet, tek yeni bağımlılık - CLI'ya `Process.Start` ile çağrı yapmak yerine test edilebilir bir kütüphane tercih edildi), şifreleme için .NET'in yerleşik `AesGcm`'i (AES-256-GCM, ek bağımlılık yok), e-posta için yerleşik `System.Net.Mail.SmtpClient` (düşük hacimli alarm postası için MailKit gereksiz görüldü). `pg_dump` gerçek çalıştırılıyor - API imajına `postgresql16-client` eklendi (Dockerfile), CI'a da kurulum adımı eklendi (`.github/workflows/ci.yml`).

**Gerçek bir tasarım hatası bu oturumda bulunup düzeltildi:** `BackupService.RunPgDumpAsync` başta bağlantı bilgisini `Environment.GetEnvironmentVariable("ConnectionStrings__Default")` ile ham ortam değişkeninden okuyordu - bu, `WebApplicationFactory`'nin test-time `IConfiguration` override'ını (Testcontainers bağlantı dizesi) tamamen görmezden gelirdi, testlerde gerçek Postgres yerine boş/yanlış bir bağlantıya düşerdi. `IConfiguration.GetConnectionString("Default")` okuyacak şekilde düzeltildi (Program.cs'teki `ResolveConnectionString` ile aynı desen) - entegrasyon testi yazılırken fark edildi, canlıya hiç çıkmadı.

**Bulunan ikinci bir gerçek durum (kod hatası değil, ortam):** Bu oturumda host disk'i (`/System/Volumes/Data`) 433GB/460GB dolulukla neredeyse tamamen doldu (~130MB boş kaldı), `npm run build`'in standalone kopyalama adımı `ENOSPC` ile patladı. `.next` klasörü silinip `docker image prune` ile geçici alan açıldı, sonrasında (muhtemelen macOS'un kendi arka plan temizliği ile) alan 3GB'a çıktı ve build/docker rebuild tamamlandı. Bu, projeyle ilgisiz ama gerçek bir Mac disk sorunu - kullanıcıya ayrıca bildirildi.

**Testler:** `Unit/OpsDomainTests.cs` (10 test - şifreleme round-trip, sağlık karar mantığı `SystemHealthMonitor.Evaluate`, alarm soğuma süresi), `Integration/OpsFlowTests.cs` (4 test, Testcontainers - manuel tetikleme gerçek `pg_dump`+şifreleme+Fake depolamayla `Succeeded` üretiyor, liste sayfalanıyor, sağlık uç noktası DB'yi yansıtıyor, admin olmayan istek 401). `dotnet test` → 189/189 yeşil. `npm run build`/`lint` temiz.

**`docker compose` ile canlı doğrulama (bu oturumda yapıldı):** `postgresql16-client`'ın gerçek imaja eklendiği doğrulandı (`pg_dump --version` → 16.15 konteyner içinde). Gerçek admin oturumuyla manuel yedekleme tetiklendi → gerçek pg_dump (55KB) → AES-GCM şifreleme → Fake depolamaya yükleme → `BackupRun.Succeeded`, hepsi ~1 saniyede. `SystemHealthMonitor`'ın bir sonraki turda `Healthy`'ye döndüğü, eşikler geçici sıfırlanarak zorlanan bir `Unhealthy` durumunda alarm e-postasının gerçekten `FakeEmailSender`'a düştüğü, `/dashboard` üzerindeki kırmızı/yeşil şeridin ve `/dashboard/backups` sayfasının (geçmiş tablo + "Şimdi yedek al" düğmesi) tarayıcıda doğru çalıştığı doğrulandı. Sıfır veritabanında 13 migration'ın tamamı (Ops dahil) hatasız uygulandı.

**Kapsam dışı bırakılan tek parça:** gerçek SFTP sunucusuna karşı canlı doğrulama - kullanıcı henüz sunucu bağlantı bilgilerini paylaşmadı. `SftpBackupStorage` yazılıp derlendi ama yalnızca `FakeBackupStorage` ile uçtan uca test edildi. Kullanıcı `Backup__Sftp__*` bilgilerini girip `Backup__Provider=Sftp` yaptığında bu adım ayrıca doğrulanmalı - bkz. `docs/09-testing.md` Faz 4 notları, `docs/16-backup-restore.md` (yeni - geri yükleme runbook'u, uygulama içi "restore" düğmesi bilinçli olarak eklenmedi, `docs/10-decisions.md` G9).

- [ ] Faz 4 commit edilecek (`abdera-commit` skill), push için ayrıca kullanıcı onayı istenecek.
- [ ] Kullanıcı SFTP sunucu bilgilerini paylaştığında: `.env`'e `Backup__Provider=Sftp` + `Backup__Sftp__*` girilip gerçek bir yedekleme+geri yükleme provası (`docs/16-backup-restore.md`) yapılmalı.

## Devam noktası (önceki)

**`redesign/sicak-atolye` dalı — Faz 1-3 (`docs/15-product-phases.md`) tamamlandı. Faz 1-2 doğrulaması commit `e04d1e5` ile kaydedildi; Faz 3 bu turda eklendi, henüz commit edilmedi. Push için ayrıca kullanıcı onayı gerekiyor (repo public).**

### Faz 1-2 doğrulaması (commit `e04d1e5`)

Bu dalda önceki bir oturumda Faz 1 (veli-ekle popup'ı, takvim enstrüman filtresi/renkleri/detay popup'ı, Ayarlar sayfası, Mesaj Merkezi şablon editörü+placeholder sürükle-bırak+önizleme) ve Faz 2 (toplu ödeme, gider defteri, telafi kredisi kartı, parola korumalı Maliyet Takibi) kodlanmıştı ama hiç commit edilmemiş, `dotnet test`/`npm run build` çalıştırılmamış, gerçek bir veritabanına karşı denenmemişti. Bu turda tamamı doğrulandı:

- **Gerçek bug bulundu ve düzeltildi:** `backend/src/Abdera.Api/Persistence/Migrations/20260822120000_Expenses.cs` elle yazılmıştı ama `.Designer.cs` eşleniği ve `[Migration]`/`[DbContext]` attribute'ları yoktu - EF Core bu migration'ı hiçbir zaman keşfetmiyordu, `__EFMigrationsHistory`'ye hiç yazılmıyordu, `expenses` tablosu gerçek veritabanında **hiç oluşmuyordu** (`docker compose` ile sıfır ve dolu veritabanında ayrı ayrı doğrulandı - tablo yoktu). Elle yazılmış dosya silinip `abdera-migration` skill'iyle `dotnet ef migrations add Expenses -o Modules/Billing/Persistence/Migrations` ile doğru şekilde yeniden üretildi (bu arada eksik olan `CK_expenses_amount` (`amount > 0`) veritabanı kısıtı da eklendi - önceden yalnızca uygulama kodunda kontrol ediliyordu, CLAUDE.md'nin "kısıtı veritabanına yazmadan bırakma" kuralına aykırıydı). Sıfır veritabanı + mevcut (9 migration'lı) veritabanı + `Down()` üçü de `docker compose`'da canlı doğrulandı.
- **Eksik test kapsamı tamamlandı:** `Expenses.cs` ve `BulkPayments.cs` için hiç entegrasyon testi yoktu (CLAUDE.md: her use-case en az bir mutlu yol + bir invariant-ihlali testiyle gelir) - bu, migration bug'ının neden hiç yakalanmadığını da açıklıyor (test factory `MigrateAsync()` kullanıyor, gerçek migration'ları uyguluyor - bir endpoint testi olsaydı bug'ı yakalardı). `ExpensesFlowTests.cs` (yeni, 2 test) ve `PricingAndBillingFlowTests.cs`'e 2 yeni test (toplu ödeme mutlu yol + borç aşımı reddi) eklendi.
- `dotnet test` → 164/164 yeşil. `npm run build`/`npm run lint` temiz (bir kullanılmayan `setName` uyarısı düzeltildi).
- **Tarayıcıda gerçek admin oturumuyla uçtan uca denendi** (`docker compose` `db`+`api` + `npm run dev` frontend): veli yokken input'ların gizlendiği ve popup ile eklenebildiği, takvimde enstrüman filtresinin ve aynı saatte 4 dersin (farklı renk + baş harf kısaltması) doğru göründüğü, ders detay popup'ının tarih/saat/süre/öğretmen/katılım/durum gösterdiği, Ayarlar sayfasındaki kalıcı şifre değiştirmenin çalıştığı, Maliyet Takibi'nin parola doğrulaması + gider ekleme/listelemesinin çalıştığı (migration düzeltmesi burada kanıtlandı), Aidatlar'daki toplu ödemenin (10 ay, tek seferde) tüm ayları `ödendi` işaretlediği, telafi hakkı kartının doğru render olduğu, Mesaj Merkezi'nin gönderim kayıtlarında ders türü/veli/öğrenci adının göründüğü ve şablon editörünün placeholder sürükle-bırak + canlı önizlemesinin çalıştığı doğrulandı.
- **Küçük bulgu, aynı turda düzeltildi:** Aidatlar sayfasındaki fiyat listesi bölümü kullanıcının istediği "Kurs Bedeli" adı yerine "Fiyat Listeleri" başlığını taşıyordu - işlevsel olarak istenen şeyi zaten yapıyordu (enstrüman+süre bazlı birim fiyat), yalnızca isimlendirme farkıydı. Kullanıcı onayıyla `price-lists-section.tsx`'teki başlık "Kurs Bedeli" + açıklayıcı alt metin olarak güncellendi (commit `ecca3d5`).
- Ortamla ilgili not (kod hatası değil): `abdera-web-api-1` bir kez host RAM baskısıyla SIGSEGV ile çöktü, yeniden başlatılınca sorunsuz devam etti - bu davranış daha önce de görülmüş ve kayda geçirilmiş (bkz. aşağıdaki "UI takip" bölümü).

### Faz 3 — Mesaj otomasyonu (bu turda eklendi, henüz commit edilmedi)

- Yeni `NotificationAutomationSettings` (Modules/Messaging/Domain) - tek satırlık, sabit id'li (`SingletonId`) kurum ayarı: hatırlatma süresi (15/30/45/60, uygulama katmanında doğrulanır), aktif/pasif, üçüncü RSVP seçeneği açık/kapalı. `GET/PUT /api/notification-automation-settings` (AdminOnly). `PUT` audit_log'a yazar (JsonSerializer, önceki/yeni değer); hatırlatma süresi değişirse bekleyen `LessonReminder` job'larının `scheduled_at`'i dersin gerçek saatine göre yeniden hesaplanır, otomasyon kapatılırsa bekleyenler iptal edilir (yeniden açılınca geçmişe dönük toparlama yapılmaz - yalnızca yeni üretilen dersler job alır).
- `LessonSeriesFeatures.GenerateForSeriesAsync` ve `ChangeRequests.ApproveAsync`'teki `config.GetValue("Notifications:LessonReminderMinutesBefore", 60)` çağrıları `NotificationAutomationSettings.GetCurrentAsync(db)` ile değiştirildi - appsettings yerine DB kaynaklı, admin panelden değiştirilebilir. `NotificationScheduler.ScheduleAsync` artık `LessonReminder` tipinde `IsEnabled=false` iken job açmıyor.
- **Üçüncü RSVP seçeneği** ("Evet ama biraz geç kalacağım"): `RsvpResponse.AttendingLate` enum değeri eklendi (DB'de `varchar` olarak tutulduğu için migration gerekmedi), `RsvpButtonPayload.AttendingLateAction`, `Webhooks.HandleRsvpButtonAsync`'e üçüncü dal, `Calendar.cs`/`Dashboard.cs`'teki RSVP özet sorgularına `HasAttendingLate` (öncelik sırası: Attending > AttendingLate > NotAttending > Unknown; Dashboard'daki kaba KPI toplamında bilinçli olarak Attending'e katlanıyor, yalnızca takvim detayında ayrı gösteriliyor).
- **Gerçek prod boşluğu kapatıldı:** `RsvpButtonPayload.Sign` önceden yalnızca dev simülatörü tarafından çağrılıyordu - gerçek `CloudApiWhatsAppClient.SendTemplateAsync`'in quick-reply buton payload'ını hiç per-ders override etmediği fark edildi (yalnızca body parametrelerini gönderiyordu). `IWhatsAppClient.SendTemplateAsync`'e opsiyonel `buttonPayloads` parametresi eklendi; `NotificationMessageBuilder.BuildLessonMessageAsync` yalnızca `lesson_reminder_rsvp` şablonu için (2 veya 3, `AllowAttendingLateResponse`'a göre) imzalı payload üretiyor; `CloudApiWhatsAppClient` bunları Meta'nın `components:[{type:"button",sub_type:"quick_reply",index,parameters:[{type:"payload",...}]}]` şemasıyla gönderiyor; `FakeWhatsAppClient` logluyor. `DevWhatsAppSimulator` üçüncü action'ı da destekliyor.
- **Frontend:** Mesaj Merkezi'ndeki "Otomatik gönderim ayarları" paneli artık tamamen yerel `useState` değil - `useAutomationSettings`/`useUpdateAutomationSettings` (`lib/messaging.ts`, yeni `api.put`) ile gerçek uca bağlı, sayfa yüklenince mevcut değerleri gösteriyor. Takvim detay popup'ı, dashboard haftalık ızgara popover'ı, öğretmenin günlük ders listesi ve veli portalının RSVP butonları (üç seçenekli: Geliyorum/Geç kalacağım/Gelemiyorum) `AttendingLate`'i gösterecek şekilde güncellendi.
- **Testler:** `MessagingDomainTests.cs`'e 8 yeni birim testi (izin verilen/verilmeyen dakika değerleri). `MessagingFlowTests.cs`'e 4 yeni entegrasyon testi (Testcontainers): üçüncü RSVP butonunun `LessonRsvp.Response=AttendingLate` yazdığı, hatırlatma süresi değişince bekleyen job'ların yeniden zamanlandığı (hem mevcut job hem yeni üretilecek dersler için), otomasyon kapatılınca bekleyenlerin iptal edilip yeni ders üretiminde job açılmadığı ve yeniden açılınca yeni derslerin tekrar job aldığı. **Test-izolasyon notu:** bu ayar sınıfın paylaştığı gerçek bir singleton DB satırı olduğu için testler önce bilinen bir temel duruma (`PUT` ile 60dk/açık) sıfırlanıyor - aksi hâlde testler arasında sıra bağımlılığı (bir testin bıraktığı değer diğerini bozuyor) oluşuyordu, ilk yazımda tam olarak bu yüzden bir test 3 kez üst üste çalıştırılınca kırıldı, düzeltilip 3 kez daha yeşil doğrulandı.
- `dotnet test` → 175/175 yeşil. `npm run build`/`npm run lint` temiz.
- **Tarayıcıda gerçek admin oturumuyla uçtan uca doğrulandı:** otomasyon süresini 60'tan 15'e değiştirip bekleyen bir job'ın `scheduled_at`'inin gerçekten 15 dakikaya güncellendiği API üzerinden teyit edildi; `POST /api/dev/whatsapp/simulate-rsvp` ile `rsvp_attending_late` gönderilip takvim detay popup'ında "Katılım: Geç kalacak" göründüğü doğrulandı.

- [ ] Faz 3 commit edilecek (`abdera-commit` skill), push için ayrıca kullanıcı onayı istenecek.
- [ ] Faz 4 (sağlık kontrolü + yedekleme + e-posta alarmı) henüz planlanmadı - SMTP sağlayıcısı, yedekleme hedefi/saklama süresi gibi kullanıcı kararları gerekiyor, ayrı bir turda ele alınacak.
- [ ] Gerçek WhatsApp sağlayıcısı seçilip Meta'ya şablon onayı için gönderilene kadar üçüncü buton (ve genel olarak `lesson_reminder_rsvp` şablonu) yalnızca `Fake` sağlayıcıda uçtan uca denenebilir (D2, değişmedi).

## Devam noktası (önceki - Faz 6 sonrası, hâlâ geçerli arka plan bilgisi)

**Faz 6 — Banka entegrasyonu (sanal IBAN) tamamlandı; veli portalı veri bağlantıları ile sağlamlaştırıldı.** Veli panelindeki aidat/telafi/IBAN/mesaj alanları artık mock değil; `GuardianOnly` kapsamlı salt-okunur API'lerden geliyor. Veli veri izolasyonunu doğrulayan entegrasyon testi eklendi. Suite şu an 160/160 yeşil, frontend lint/build temiz.

- [ ] Commit + push (push için kullanıcı onayı gerekiyor — public repo). Henüz commit edilmedi, bir sonraki adım bu.

Sıradaki ürün fazı: **Phase 7 — Gelişim takibi ve hatırlatmalar** (Progress modülünün kalanı: `skill_definitions`/`skill_assessments`/`practice_assignments`, doğum günü ve paket bitiş bildirimleri). Bunlar bu turda canlıya hazır görünüyormuş gibi UI'a eklenmedi; mevcut LessonNote akışı çalışır durumda.

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

## Denetim düzeltmeleri — SEC-1 (Kritik) + SEC-2 (Yüksek): webhook/RSVP imzasında boş secret fail-open

`docs/13-audit-fix-prompt.md`'deki 14 maddelik denetim listesinin ilk iki maddesi. `WebhookSignatureVerifier.IsValid` ve `RsvpButtonPayload.TryVerify`, `appSecret`/`signingKey` boş/tanımsız olduğunda ayrı bir kontrol yapmadan boş anahtarla HMAC hesaplayıp karşılaştırıyordu — bu deterministik bir sonuç ürettiğinden, `WhatsApp__AppSecret`/`WhatsApp__PayloadSigningKey` canlıda tanımsız kalırsa imza doğrulaması sessizce herkese açık (fail-open) hale geliyordu.

### Yapılanlar

- Her iki metoda da `Modules/Banking/Features/Webhooks.cs`'teki `VerifySharedSecret` ile aynı desende bir guard eklendi: anahtar boşsa doğrudan `false`.
- **`Shared/ProductionSecretsGuard.cs`** eklendi — `Program.cs`'te `builder.Build()`'den hemen sonra çağrılıyor, `Production` ortamında bu iki değişkenden biri boşsa uygulama `InvalidOperationException` ile başlamayı reddediyor (Development'ta zorunlu değil).
- `Unit/MessagingDomainTests.cs`'e boş anahtarda her iki fonksiyonun da reddettiğini doğrulayan iki test eklendi; `Unit/ProductionSecretsGuardTests.cs` (4 test) startup guard'ı kapsıyor.
- `Integration/AbderaWebApplicationFactory.cs`'teki test konfigürasyonuna gerçek (boş olmayan) bir `WhatsApp:AppSecret`/`WhatsApp:PayloadSigningKey` eklendi — daha önce ikisi de boştu, `MessagingFlowTests.Webhook_does_not_reprocess_a_duplicate_provider_event_id` de boş anahtarla imzalıyordu; artık test secret'ıyla imzalıyor.
- `docs/06-whatsapp.md`'ye fail-closed davranışı ve `ProductionSecretsGuard`'ı açıklayan bir not eklendi.

### Testler

`dotnet test` → 147/147 yeşil (141 + 6 yeni: 2 imza guard testi + 4 `ProductionSecretsGuard` testi).

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

`.env`'de `WhatsApp__AppSecret` geçici olarak boşaltılıp `api` yeniden başlatıldı (Development'ta kasıtlı olarak hâlâ ayağa kalkıyor — guard yalnızca Production'da zorunlu kılıyor): boş anahtarla hesaplanmış geçerli bir HMAC ile `/api/webhooks/whatsapp`'a istek atıldığında `401` döndü (düzeltmeden önce bu istek `200` dönerdi). `.env` gerçek `dev-app-secret` değerine geri alınıp `api` yeniden başlatıldı, aynı body gerçek secret'la imzalandığında `200` döndü ve `/health` `Healthy` kaldı — regresyon yok.

Not: bu doğrulama sırasında frontend (`web`) servisinin `docker-compose up --build` sırasında `next build` adımında `SIGKILL` ile başarısız olduğu görüldü (muhtemelen build container'ının bellek limiti) — bu, bu maddenin değişikliğiyle ilgisiz, önceden var olan bir ortam kısıtı; yalnızca `db`+`api` ayağa kaldırılarak doğrulama tamamlandı. Frontend build'i ayrı bir not olarak burada kayda geçiriliyor, henüz düzeltilmedi.

### Kalan iş

Push edildi (e47fa0a). Sıradaki madde: SEC-3 (giriş ekranında rate limiting).

## Denetim düzeltmeleri — SEC-3 (Yüksek): giriş ekranında rate limiting / kaba kuvvet koruması yoktu

`docs/13-audit-fix-prompt.md` madde 3. 69 endpoint'in hiçbirinde `AddRateLimiter` yoktu, `/api/auth/login` sınırsız denenebiliyordu.

### Yapılanlar

- ASP.NET Core'un yerleşik `Microsoft.AspNetCore.RateLimiting`'i kullanıldı (shared framework'te geliyor, ek NuGet paketi eklenmedi).
- `Program.cs`'te iki fixed-window politika: `auth-login` (IP başına, varsayılan 5 istek/15 dk, `RateLimiting:LoginPermitLimit`/`LoginWindowMinutes` ile yapılandırılabilir) ve `webhooks` (IP başına dakikada 60 istek, `RateLimiting:WebhookPermitLimitPerMinute`). Politika delegate'leri istek anında `IConfiguration`'ı okuyor, bu yüzden test override'ları sorunsuz yansıyor.
- `/api/auth/login`, `/api/webhooks/whatsapp` (POST), `/api/webhooks/bank` (POST) ilgili politikalarla işaretlendi. Limit aşımı `429` döner.
- `AbderaWebApplicationFactory`'nin paylaşılan test config'inde `RateLimiting:LoginPermitLimit=10000` ile limit pratikte devre dışı bırakıldı - `MessagingFlowTests`/`AttendanceAndChangesFlowTests` gibi sınıflar `CreateAdminClientAsync`'i onlarca kez çağırıyor, gerçek 5'lik limit bunları kırardı.
- `AuthFlowTests.cs`'e yeni test: `_factory.WithWebHostBuilder(...)` ile ayrı, düşük limitli (`LoginPermitLimit=3`) bir host kurup 4. denemenin `429` döndüğünü doğruluyor.

### Testler

`dotnet test` → 148/148 yeşil (147 + 1 yeni).

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

`db`+`api` ayağa kaldırıldı, `/api/auth/login`'e art arda 6 hatalı-şifre isteği atıldı: ilk 5'i `401`, 6.'sı `429` döndü. Ardından `/api/webhooks/whatsapp`'a gerçek imzayla tek bir istek atılıp hâlâ `200` döndüğü ve `/health`'in `Healthy` kaldığı doğrulandı (rate limiter middleware'i webhook akışını bozmadı).

### Kalan iş

Push edildi (b2bd577). Sıradaki madde: SEC-4 (Login.cs'teki zamanlama kanalı).

## Denetim düzeltmeleri — SEC-4 (Orta): Login.cs'te kullanıcı numaralandırmasına yeten zamanlama kanalı

`docs/13-audit-fix-prompt.md` madde 4. Kullanıcı bulunamadığında hash doğrulama adımı (PBKDF2) hiç çalışmıyordu - yanıt mesajı aynı ama süre farklıydı (kayıtlı e-posta ~50-100ms, kayıtsız ~1ms), bu da e-posta numaralandırmasına yeten bir zamanlama kanalıydı. Ayrıca koddaki yorum ("aynı genel mesaj döner - kullanıcı numaralandırmasına karşı") gerçek davranışı yanlış tarif ediyordu (yalnızca mesajı eşitliyordu, süreyi değil).

### Yapılanlar

- `Login.cs`'e sabit, önceden hesaplanmış bir dummy kullanıcı/hash eklendi (`DummyUser`/`DummyPasswordHash`, tip başlatılırken bir kez hesaplanıyor).
- Kullanıcı `null` olduğunda artık bu dummy hash'e karşı `passwordHasher.VerifyHashedPassword` çağrılıyor (sonuç kullanılmıyor, yalnızca PBKDF2 maliyeti eşitleniyor), ardından aynı 401 dönülüyor.
- Yorum, koddaki gerçek davranışla (mesaj + süre eşitleme) eşleştirildi.
- `AuthFlowTests.cs`'e yeni test: var olmayan e-posta ile yanlış şifrenin aynı görünür yanıtı (title/detail) verdiğini doğruluyor. Zamanlama farkının kapanması CI'da flaky bir eşik gerektireceğinden otomatik teste bağlanmadı - canlı doğrulama ile ayrıca ölçüldü (aşağıya bakınız).

### Testler

`dotnet test` → 149/149 yeşil (148 + 1 yeni).

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

`RateLimiting__LoginPermitLimit` geçici olarak çok yükseltilip (`auth-login` politikasının timing ölçümünü etkilememesi için) 10'ar istekle iki grup ölçüldü: kayıtlı e-postaya yanlış şifre ortalama ~64ms, var olmayan e-postaya herhangi bir şifre ortalama ~55ms - önceki davranışta (dummy hash yokken) ikinci grup ~1ms civarında olurdu (hash hiç hesaplanmıyordu). Fark artık ölçüm gürültüsü seviyesinde, yapısal bir "adımı tamamen atla" farkı değil. `.env` sonra orijinal değerine geri alındı, gerçek admin girişinin hâlâ `200` döndüğü doğrulandı.

### Kalan iş

Push edildi (5f7e755). Sıradaki madde: UX-1 (mobil destek).

## Denetim düzeltmeleri — UX-1 (Yüksek): mobil desteği fiilen yoktu

`docs/13-audit-fix-prompt.md` madde 5. "Sıcak Stüdyo" tasarım konseptinin görseli bu oturumda erişilebilir değildi (yalnızca ismi geçiyordu, mockup/renk/tipografi detayı yoktu) - kullanıcıyla netleştirildi: bu turda yalnızca **fonksiyonel düzeltme** yapıldı (mevcut nötr renk/tipografi korunarak), tam görsel yeniden tasarım kapsam dışı bırakıldı.

### Yapılanlar

1. **`app-header.tsx`**: `md:` (768px) altında yatay nav gizlenip hamburger + sağdan açılan slide-in drawer'a geçiyor; `md` ve üstünde eski yatay nav aynen kalıyor. Drawer: backdrop tıklaması/X butonu/link tıklaması ile kapanıyor (route değişince kapatmak için `useEffect` içinde `setState` yerine - React'in "effect içinde senkron setState" lint kuralına takılmamak için - doğrudan `Link onClick`'te kapatılıyor). Hamburger butonu ve linkler `min-h-11`.
2. **`billing/price-lists-section.tsx`** ve **`billing/student-billing-section.tsx`**: tablolar `overflow-x-auto rounded-lg border` ile sarıldı (banking/notifications sayfalarındaki desenle aynı).
3. Denetimde adı geçen üç birincil aksiyon butonu `min-h-11` (44px) yapıldı: **`billing/student-billing-section.tsx`** "Ödeme al"/"Kaydet", **`change-requests/page.tsx`** "Onayla"/"Reddet", **`notifications/page.tsx`** "Yeniden dene". `price-lists-section.tsx`'teki "Fiyat listesi oluştur"/"Uygula" da aynı gerekçeyle eklendi.
4. `.claude/launch.json` eklendi (`run`/önizleme için frontend dev server tanımı) - bu oturumda tarayıcı doğrulaması için kullanıldı, sonraki oturumlar için de kalıcı.

### Testler

Bu madde saf frontend/UI - birim/entegrasyon testi kapsamı dışında (backend değişmedi, `dotnet test` zaten 149/149 yeşildi). `npm run build` ve `npm run lint` temiz (yalnızca `banking/page.tsx:49`'daki önceden var olan, bu değişiklikle ilgisiz bir `react/no-unescaped-entities` hatası kaldı - ayrı bir görev olarak flag'lendi).

### Canlı doğrulama (bu oturumda yapıldı)

`db`+`api` docker'da, frontend `npm run dev` ile (`NEXT_PUBLIC_API_BASE_URL` varsayılanı `localhost:8080`'i kullanıyor) ayrı ayrı ayağa kaldırıldı, tarayıcı 375×812 (mobil) ve masaüstü genişliklerinde denendi:
- Mobilde hamburger görünüyor, tıklanınca drawer (8 link + e-posta/rol + çıkış) backdrop ile açılıyor; X butonuyla kapanıyor.
- Masaüstünde (`resize_window` desktop preset) eski yatay nav aynen duruyor, hamburger görünmüyor.
- `/dashboard/billing`'de "Banka Öğrenci" seçilip receivables tablosu açıldı: tablo sarmalayıcısının `overflow-x: auto` olduğu ve sayfa genelinde yatay kaydırma oluşmadığı (`document.documentElement.scrollWidth === clientWidth`) doğrulandı; "Ödeme al" butonunun gerçek render yüksekliği `getBoundingClientRect()` ile **44px** ölçüldü.
- `/dashboard/notifications`'daki (önceden de doğru olan) tablo sarmalı hâlâ çalışıyor, regresyon yok.

Not: bu oturumda `docker compose up --build` ile frontend imajının `next build` adımında `SIGKILL` aldığı görüldü (muhtemelen build container'ının bellek limiti) - `npm run build` yerelde hatasız tamamlandığından bunun kod değişikliğiyle ilgisi olmadığı doğrulandı; bu yüzden canlı doğrulama `db`+`api` docker'da, frontend yerel `npm run dev` ile yapıldı. Frontend'in docker imajı build'i ayrı, önceden var olan bir ortam sorunu olarak kayda geçiriliyor.

### Kalan iş

Push edildi (4e9438a). Sıradaki madde: UX-2 (Geist fontu render edilmiyor).

## Denetim düzeltmeleri — UX-2 (Orta): Geist fontu indiriliyor ama Arial render ediliyordu

`docs/13-audit-fix-prompt.md` madde 6. `layout.tsx` Geist Sans'ı Google Fonts'tan yükleyip `--font-geist-sans` CSS değişkenini tanımlıyordu ama `globals.css`'teki `body { font-family: Arial, ... }` bu değişkeni hiç kullanmıyordu - iki font dosyası boşuna iniyor, uygulama Arial ile render ediliyordu.

### Yapılanlar

`globals.css`'teki `body { font-family: ... }` satırı `var(--font-geist-sans), Arial, Helvetica, sans-serif` yapıldı (Arial/Helvetica fallback zinciri olarak korundu).

### Testler

Saf CSS değişikliği - `npm run build`/`npm run lint` temiz (yalnızca önceden var olan, ilgisiz `banking/page.tsx:49` hatası kaldı).

### Canlı doğrulama (bu oturumda yapıldı)

Frontend `npm run dev` ile ayağa kaldırılıp tarayıcıda `/login` sayfasında `getComputedStyle(document.body).fontFamily` çalıştırıldı: önceden `"Arial, Helvetica, sans-serif"` dönerken artık `"Geist, \"Geist Fallback\", Arial, Helvetica, sans-serif"` dönüyor.

### Kalan iş

Push edildi (b3817ec). Sıradaki madde: UX-4 (kullanılmayan karanlık tema CSS'i).

## Denetim düzeltmeleri — UX-4 (Düşük): kullanılmayan karanlık tema CSS'i silindi

`docs/13-audit-fix-prompt.md` madde 7. `globals.css`'teki `@media (prefers-color-scheme: dark)` bloğu, `layout.tsx`'in gövdeye verdiği `bg-neutral-50 text-neutral-900` Tailwind sınıfları tarafından eziliyordu - karanlık tema ne çalışıyordu ne tasarlandı, yanıltıcı ölü kod. Kullanıcıya karar soruldu: "bloğu sil" (karanlık tema ileride ayrı bir görev olarak ele alınabilir).

### Yapılanlar

`globals.css`'teki `@media (prefers-color-scheme: dark) { :root { ... } }` bloğu tamamen silindi. `:root`'taki temel `--background`/`--foreground` değişkenleri ve `@theme inline` eşlemesi (Next.js şablonundan gelen, başka bir yerde `bg-background`/`text-foreground` olarak kullanılmıyor ama zararsız) dokunulmadan bırakıldı - denetim yalnızca yanıltıcı dark-mode bloğunu hedefliyordu.

### Testler

Saf CSS silme - `npm run build` temiz.

### Canlı doğrulama (bu oturumda yapıldı)

`npm run dev` ile ayağa kaldırılıp tarayıcı `prefers-color-scheme: dark` simüle edecek şekilde ayarlandı (`matchMedia('(prefers-color-scheme: dark)').matches === true` doğrulandı), `/login` sayfasında `body` arka planının hâlâ beyaz (`rgb(255,255,255)`) kaldığı doğrulandı - önceki davranışla birebir aynı (zaten hiç çalışmıyordu), yalnızca yanıltıcı kod kalktı.

### Kalan iş

Push edildi (732a1cf). Sıradaki madde: ARC-1 (optimistic concurrency).

## Denetim düzeltmeleri — ARC-1 (Yüksek): optimistic concurrency hiç uygulanmamıştı

`docs/13-audit-fix-prompt.md` madde 8. CLAUDE.md'nin kendi kuralı ("Eşzamanlı düzenleme riski olan tablolarda optimistic concurrency") kodda hiç örneği yoktu - iki admin aynı `Receivable`'a aynı anda ödeme işlerse ikinci yazma birincisini sessizce eziyordu.

### Yapılanlar

1. **Sürpriz:** Denetimin önerdiği `UseXminAsConcurrencyToken()` API'si Npgsql.EntityFrameworkCore.PostgreSQL 7.0'dan itibaren **kaldırılmış** ([npgsql/efcore.pg#3539](https://github.com/npgsql/efcore.pg/issues/3539)) - proje 10.0.3 kullanıyor. Güncel standart EF mekanizması: `builder.Property<uint>("Version").IsRowVersion();` (shadow property, domain entity'ye dokunulmadan) - sağlayıcı bunu otomatik `xmin` sistem koluna eşliyor. Detay: `docs/08-migrations.md` "Optimistic concurrency (xmin)".
2. `ReceivableConfiguration.cs` ve `BankIncomingTransactionConfiguration.cs`'e bu shadow property eklendi.
3. `abdera-migration` skill'iyle migration oluşturuldu: `011_add_optimistic_concurrency` (`AddOptimisticConcurrency`) - üretilen SQL **boş** (`dotnet ef migrations script` yalnızca `__EFMigrationsHistory`'ye satır ekliyor), çünkü `xmin` zaten var olan bir sistem kolonu; migration yalnızca model snapshot'ı günceller. `Down()` da yerel veritabanında test edildi (abdera-migration skill kuralı).
4. `GlobalExceptionHandler.cs`'e `DbUpdateConcurrencyException -> 409` dalı eklendi.
5. `Integration/ConcurrencyFlowTests.cs` (yeni dosya, 2 test, Testcontainers): aynı `Receivable`'ı/`BankIncomingTransaction`'ı iki ayrı `DbContext` ile okuyup çelişen şekilde değiştiriyor - ilk `SaveChangesAsync` başarılı, ikincisi `DbUpdateConcurrencyException` fırlatıyor; kazanan yazının sessizce ezilmediği de ayrıca doğrulanıyor.

### Testler

`dotnet test` → 151/151 yeşil (149 + 2 yeni).

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

1. Volume sıfırlanıp (`docker compose down -v`) `db`+`api` yeniden ayağa kaldırıldı: tüm 9 migration (`AddOptimisticConcurrency` dahil) sıfırdan hatasız uygulandı.
2. Gerçek bir teacher/student/enrollment/price-list/fee-plan/receivable HTTP üzerinden oluşturulup, **aynı receivable'a 5 tur boyunca gerçekten eşzamanlı** (arka planda `&`+`wait` ile) iki ödeme isteği atıldı - bu yalıtılmış bir test değil, gerçek çalışan uygulamaya karşı gerçek bir yarış: birden çok istek `409` ile döndü, response body'si tam olarak `{"title":"Kayıt başka bir işlemce güncellendi", "status":409, ...}` (yeni `DbUpdateConcurrencyException` dalı) - bazı geç denemeler ayrıca zaten var olan `"Çakışma"`/`"'Paid' durumundaki bir aidata ödeme kaydedilemez."` kuralına takıldı, ikisi birlikte doğru çalıştı.
3. `GET /api/students/{id}/billing` ile son durum kontrol edildi: `totalPaid` başarılı isteklerin toplamıyla birebir tutarlı, hiçbir yazma sessizce kaybolmadı/çift sayılmadı.

### Kalan iş

Push edildi (1bb45e8). Sıradaki madde: ARC-2 (üretilmeyen bildirim tipleri sessizce FAILED'a düşüyor).

## Denetim düzeltmeleri — ARC-2 (Orta): tanımlı ama üretilmeyen bildirim tipleri yanıltıcı FAILED hatası veriyordu

`docs/13-audit-fix-prompt.md` madde 9. `NotificationJobType.Birthday`/`PackageEnding` hiçbir use-case tarafından üretilmiyor; böyle bir job her nasılsa oluşursa `NotificationMessageBuilder.BuildAsync` sessizce `null` dönüyor, dispatcher da bunu "ilgili kayıt bulunamıyor" gibi yanıltıcı bir hatayla `FAILED`'a düşürüyordu.

### Yapılanlar

1. Yeni `Modules/Messaging/Domain/NotImplementedNotificationTypeException.cs` eklendi.
2. `NotificationMessageBuilder.BuildAsync`'in switch'ine `Birthday`/`PackageEnding` için bu istisnayı fırlatan bir dal eklendi.
3. `NotificationDispatcher.SendOneAsync` bu istisnayı yakalayıp `job.MarkFailed("Bu bildirim tipi henüz uygulanmadı (Faz 7).", ...)` ile açık bir hata yazıyor.
4. `docs/05-state-models.md`'ye `LessonChangeRequestStatus`'ün dört veli-etkileşimi durumunun (`AlternativeProposed`/`ParentConfirmationPending`/`ParentAccepted`/`ParentRejected`) hâlâ hiçbir yerde üretilmediğini doğrulayan bir "bilinçli eksik" notu eklendi (grep ile teyit edildi - yalnızca enum tanımında var, kodda hiç set edilmiyor).

### Testler

`Unit/MessagingDomainTests.cs`'e `[Theory]` ile iki tip için `BuildAsync`'in `NotImplementedNotificationTypeException` fırlattığını doğrulayan bir test eklendi (bu istisna db/clock'a hiç dokunmadan fırlatıldığı için gerçek DbContext gerekmiyor, saf birim testi). `dotnet test` → 153/153 yeşil (151 + 2 yeni).

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

Hiçbir use-case `Birthday` job'ı üretmediğinden, gerçek bir tane doğrudan SQL ile eklendi. İlk denemede dispatcher job'ı sessiz saat (A6, `Notifications:QuietHoursStart/End=21:00/09:00`) içinde bulup ertesi sabaha erteledi - bu, ARC-2 ile ilgisiz, zaten çalışan bir davranıştı (kısa süre kafa karıştırdı). Sessiz saat penceresini geçici olarak daraltıp (`03:00-03:01`) dispatch aralığını hızlandırarak (`5sn`) job yeniden tetiklendi: 5 deneme sonunda `Failed` durumuna düştü ve `last_error` tam olarak `"Bu bildirim tipi henüz uygulanmadı (Faz 7)."` oldu - panelde artık neden başarısız olduğu okunur. `.env` orijinaline geri alındı.

### Kalan iş

Push edildi (11c9a4c). Sıradaki madde: ARC-3 (sayfalama yok, sessiz 200 kayıt kesintisi).

## Denetim düzeltmeleri — ARC-3 (Orta): sayfalama yok, iki listede sessiz 200 kayıt kesintisi

`docs/13-audit-fix-prompt.md` madde 10. `Notifications`/`BankTransactions` listeleri `Take(200)` ile sessizce kesiliyordu, toplam sayı dönmüyordu. `/api/calendar`'a da tarih aralığı üst sınırı yoktu.

### Yapılanlar

1. Yeni `Shared/PagedResponse.cs`: ortak `PagedResponse<T>` zarfı + `Pagination.Normalize` (page/pageSize'ı 1..200 aralığına clamp'ler, varsayılan pageSize 50).
2. `Notifications.ListAsync` ve `BankTransactions.ListAsync`: `?page=&pageSize=` parametreleri eklendi, `CountAsync()` ile toplam sayı hesaplanıp `{ items, totalCount, page, pageSize }` zarfında dönülüyor.
3. `Calendar.ListAsync`: `to - from` 3 aydan (93 gün) fazlaysa `ValidationFailedException` (400) fırlatılıyor.
4. **Frontend:** `lib/messaging.ts`/`lib/banking.ts`'teki hook'lar yeni zarfı okuyacak şekilde güncellendi (`page`/`pageSize` parametreleri eklendi); `notifications/page.tsx` ve `banking/page.tsx`'e "Toplam N kayıt - sayfa X/Y" + Önceki/Sonraki pager eklendi (butonlar `min-h-11`, UX-1 tutarlılığı için), filtre değişince sayfa 1'e sıfırlanıyor.

### Testler

- `Unit`: yok (saf HTTP/EF davranışı, integration ile kapsandı).
- `Integration`: `MessagingFlowTests.Notifications_list_returns_paged_envelope_and_respects_page_size` (zarf şekli + `pageSize` sınırı), `PeopleAndSchedulingFlowTests.Calendar_rejects_date_range_wider_than_three_months` (94 gün → 400, 90 gün → 200), `BankingFlowTests`'teki mevcut liste testi yeni zarfı okuyacak şekilde güncellendi.
- `dotnet test` → 155/155 yeşil (153 + 2 yeni). `npm run build` temiz.

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

`db`+`api` ayağa kaldırılıp `curl` ile doğrudan doğrulandı: `/api/notifications?page=1&pageSize=2` ve `/api/bank-transactions?page=1&pageSize=2` doğru `{items,totalCount,page,pageSize}` şeklini döndü; `/api/calendar`'a 94 günlük aralık `400` (`"Tarih aralığı en fazla 3 ay olabilir."`), 90 günlük aralık `200` döndü. Frontend `npm run dev` ile ayrı ayağa kaldırılıp tarayıcıda `/dashboard/notifications` ve `/dashboard/banking` sayfaları gerçek admin oturumuyla açıldı: pager metni ("Toplam 1 kayıt - sayfa 1 / 1") doğru göründü, konsol hatası yok (yalnızca HMR/dev sunucusuna özgü websocket gürültüsü).

### Kalan iş

Push edildi (c69b68b). Sıradaki madde: ARC-5 (döngü içinde veritabanı sorgusu, N+1).

## Denetim düzeltmeleri — ARC-5 (Düşük): döngü içinde veritabanı sorgusu (N+1)

`docs/13-audit-fix-prompt.md` madde 11. `BulkUpdate.cs` kalem başına ayrı bir `CountAsync`, `PriceLists.cs` kalem başına ayrı bir `AnyAsync` çalıştırıyordu.

### Yapılanlar

1. `BulkUpdate.BuildPreviewAsync`: döngü öncesi tek bir `GroupBy` sorgusuyla tüm kalemlerin aktif ücret planı sayıları `Dictionary<Guid,int>`'e toplanıyor, döngü içinde `GetValueOrDefault` ile okunuyor.
2. `PriceLists.CreateAsync`: döngü öncesi tek `db.Instruments.Where(ids.Contains(...)).Select(id).ToListAsync()` ile var olan enstrüman id'leri bir `HashSet`'e toplanıyor, döngü içinde `Contains` ile kontrol ediliyor.

### Testler

Saf performans refaktörü - davranış değişmedi. Mevcut `PricingAndBillingFlowTests.Bulk_price_update_does_not_retroactively_change_existing_receivables` testi (`ActiveFeePlanCount` doğrulaması dahil) değişmeden yeşil kaldı; bu, refaktörün "1 eşleşme" durumunu zaten kapsadığını doğruluyor. `dotnet test` → 155/155 yeşil (yeni test eklenmedi, mevcutlar yeterli).

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

İki kalemli (Piyano + Gitar) bir fiyat listesi gerçekten oluşturuldu (`201`, her iki enstrüman tek sorguda doğrulandı), geçersiz bir enstrüman id'siyle deneme `404` döndü. Bulk-update önizlemesi her iki kalem için de `activeFeePlanCount: 0` döndürdü - bu, otomatik testin kapsamadığı "hiç eşleşme yok" (dictionary'de anahtar yok) durumunu da doğruluyor (`GetValueOrDefault` doğru şekilde 0'a düşüyor).

### Kalan iş

Push edildi (8a06211). Sıradaki madde: ARC-4 (FluentValidation kararı).

## Denetim düzeltmeleri — ARC-4 (Orta): kullanılmayan FluentValidation paketi kaldırıldı

`docs/13-audit-fix-prompt.md` madde 12. Paket `csproj`'da duruyordu ama kodda tek bir `AbstractValidator` yoktu - doğrulama her yerde elle `throw new ValidationFailedException(...)` ile yapılıyordu, iki desen bir arada durmak kafa karıştırıcıydı. Kullanıcıya iki seçenek soruldu (kaldır / gerçekten kullan) - kullanıcı **kaldır**'ı seçti.

### Yapılanlar

1. `Abdera.Api.csproj`'dan `FluentValidation` paket referansı kaldırıldı.
2. `docs/10-decisions.md`'ye C6 kararı eklendi: "bu ölçekte ayrı bir doğrulama kütüphanesi gereksiz görülüp kaldırıldı, mevcut elle doğrulama deseni korundu."

### Testler

Saf bağımlılık kaldırma - davranış değişmedi. `dotnet restore` + `dotnet build` temiz, `dotnet test` → 155/155 yeşil (değişmedi).

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

`db`+`api` paket kaldırıldıktan sonra sıfırdan build edildi, `/health` `Healthy`, gerçek admin girişi `200` döndü - paketin kaldırılması derleme veya çalışma zamanını bozmadı.

### Kalan iş

Push edildi (a4bd06e). Sıradaki madde: 13 (ARC-6/UX-3 kararları + Dashboard modülü).

## Denetim düzeltmeleri — madde 13 (ARC-6/UX-3): Dashboard modülü yazıldı, Veli web paneli kararı verildi

`docs/13-audit-fix-prompt.md` madde 13 kod yazmadan önce iki soru soruyordu - kullanıcıya soruldu: (a) Dashboard modülü şimdi mi yazılsın → **evet**, (b) Veli web paneli ayrı bir faz mı → **hayır, WhatsApp tek kanal kalsın**. Kararlar `docs/10-decisions.md`'ye E2 ve yeni F bölümü olarak yazıldı.

### Yapılanlar — Dashboard modülü

`abdera-module` skill'iyle yeni bir modül: `Modules/Dashboard/Features/Dashboard.cs` + `DashboardModule.cs`. `docs/02-modules.md` İstisna 1 (Dashboard salt-okunur, kendi tablosu yok, başka modüllerin tablolarını doğrudan `AbderaDbContext` üzerinden LINQ ile okuyabilir) ve `docs/07-api.md`'deki tam yanıt şekli izlendi:

```
GET /api/dashboard/today
{ todayLessons, attending, notAttending, noResponse, pendingChangeRequests, overduePayments, upcomingBirthdays, upcomingSchoolEvents }
```

- Rol bazlı kapsam (`docs/04-permissions.md`): Admin okul geneli, Teacher yalnızca kendi dersleri/öğrencileri (`AuthContext.ResolveTeacherScopeAsync` deseni). `overduePayments` Teacher için sorgu bile çalıştırılmadan her zaman `0` - mali özet tamamen Admin'e ait.
- `todayLessons` = `attending + notAttending + noResponse` (bir derste birden fazla veli RSVP'si varsa: herhangi biri Attending ise Attending, yoksa herhangi biri NotAttending ise NotAttending, hiç RSVP yoksa NoResponse).
- `upcomingBirthdays`/`upcomingSchoolEvents`: 30 günlük pencere, doğum günü ay/gün karşılaştırması (yıl bağımsız, artık yıl 29 Şubat düzeltmesiyle) belleğe çekilip C# tarafında hesaplanıyor - "Do not turn the dashboard into a BI project" (master prompt) uyarınca SQL tarafında karmaşık tarih matematiği kurulmadı, bu ölçekte (~150 öğrenci) sorun değil.
- **Frontend:** `lib/dashboard.ts` (`useDashboardToday`) + `dashboard/page.tsx`'teki yer tutucu bölüm gerçek 8 istatistik karosuyla (`StatTile`, `min-h-11`) değiştirildi; bekleyen değişiklik talebi/vadesi geçmiş aidat karoları `>0` olduğunda uyarı rengiyle vurgulanıp ilgili sayfaya link veriyor. Teacher'ın mevcut "Bugünkü Derslerim" (`TeacherTodayLessons`) listesi zaten master prompt'un Teacher UX'ini karşılıyor, dokunulmadı.

### Testler

`Integration/DashboardFlowTests.cs` (yeni, 1 test, Testcontainers): iki öğretmen + iki bugünkü ders + bir RSVP + bir bekleyen değişiklik talebi + yaklaşan doğum günü seed edilip Admin'in okul genelini, Teacher A'nın yalnızca kendi dersini (`todayLessons=1`, `overduePayments=0` her zaman) gördüğü doğrulanıyor; `todayLessons == attending+notAttending+noResponse` invariant'ı da kontrol ediliyor. `dotnet test` → 156/156 yeşil (155 + 1 yeni). `npm run build` temiz.

### `docker compose` ile canlı doğrulama (bu oturumda yapıldı)

`db`+`api` sıfırdan ayağa kaldırılıp gerçek bir teacher/student/enrollment + doğrudan SQL ile "bugün"e bir ders eklendi: admin görünümü `todayLessons=1`, `upcomingBirthdays=1` doğru döndü. İkinci bir öğretmen+ders eklenince admin görünümü `todayLessons=2`'ye çıktı ama ilk öğretmenin kendi girişiyle çektiği görünüm hâlâ `todayLessons=1` kaldı - rol izolasyonu gerçek HTTP istekleriyle doğrulandı. Frontend `npm run dev` ile ayrı ayağa kaldırılıp `/dashboard` sayfasında gerçek admin oturumuyla 8 karonun (`2 Bugünkü ders`, `1 Yaklaşan doğum günü` dahil) doğru göründüğü teyit edildi, konsol hatası yok.

### Kalan iş

Push edildi (ca3bf82). Sıradaki madde: OPS-1 (CI altı fazdır testleri çalıştırmıyor) - denetim listesinin son maddesi.

## Denetim düzeltmeleri — OPS-1 (Yüksek): CI'daki test bloğu açıldı

`docs/13-audit-fix-prompt.md` madde 14 - denetim listesinin son maddesi. `.github/workflows/ci.yml`'de yalnızca `guard-secrets` çalışıyordu, `backend-build-test` Faz 0'dan beri yorumdaydı - 156 testin koruma değeri yalnızca elle çalıştırmaya bağlıydı.

### Yapılanlar

1. `backend-build-test` job'ı açıldı: `dotnet restore/build/test` `backend/Abdera.slnx` üzerinde. `services: postgres` bloğu **eklenmedi** - Testcontainers zaten kendi Postgres'ini `ubuntu-latest` runner'ının hazır Docker daemon'ı üzerinden başlatıyor, ikinci bir Postgres gereksiz/çakışan olurdu (denetimin sunduğu iki seçenekten biri).
2. `frontend-build-lint` **bilinçli olarak yorumda bırakıldı** - `npm run lint` şu an `banking/page.tsx`'teki önceden var olan, ayrı bir görev olarak flag'lenmiş bir hatadan dolayı kırmızı dönüyor; o düzeltilmeden açılırsa CI ilk günden kırmızı kalırdı.
3. `docs/09-testing.md`'ye OPS-1 notu eklendi - "required check" ayarının GitHub repo ayarlarından (Settings → Branches) yapılması gerektiği, bunun kod değişikliği kapsamının dışında olduğu açıkça yazıldı.

### Doğrulama - bu madde için özel durum

Diğer 13 maddenin aksine bu değişiklik yerel `docker compose`/tarayıcı ile doğrulanamıyor - GitHub Actions yalnızca GitHub'ın kendi altyapısında çalışır. Yerel olarak yapılan doğrulama: `.github/workflows/ci.yml` Ruby'nin `YAML.load_file`'ı ile sözdizimi açısından doğrulandı (`act` bu makinede kurulu değil). **Asıl doğrulama, push sonrası gerçek bir GitHub Actions çalıştırması izlenerek yapılacak** - `gh run watch` ile push sonrası iş akışının gerçekten yeşil döndüğü teyit edilecek, kırmızı dönerse düzeltilip tekrar push edilecek (kullanıcı onayıyla).

### Kalan iş

Henüz commit yok - commit sonrası kullanıcı onayıyla push edilecek, push sonrası CI çalıştırması `gh run watch` ile izlenip sonuç raporlanacak.

## UI tasarım uygulaması — `docs/14-ui-design-prompt.md`

Kullanıcının paylaştığı 4 ekranlı mockup ("Abdera Müzik Okulu Yönetim Sistemi - Örnek Ekranlar") ile mevcut arayüz arasındaki farkı kapatmak için önce `docs/14-ui-design-prompt.md` yazıldı, sonra aynı oturumda uygulandı. Kapsam yalnızca `frontend/src` - backend endpoint'lerine ve `lib/*.ts` sözleşmelerine dokunulmadı.

### Ölçüm

`docker-compose` ile `db`+`api` ayağa kaldırılıp gerçek bir hafta programı seed edildi (2 öğretmen, 6 öğrenci, aynı gün/saatte çakışan 2 ders, geç/erken saatli 2 ders, 1 iptal edilmiş ders, 1 bekleyen değişiklik talebi, 1 `NeedsReview` banka işlemi, 1 veli+RSVP). `npm run dev` ile 390/834/1440px'te admin, öğretmen ve veli ekranlarının gerçek hâli görüldü; en görünür üç kusur doğrulandı: hafta ızgarası yalnızca ≥1280px'te açılıyordu (768-1279 arası ajanda görünümüne düşüyordu), saat penceresi sabit 09:00-19:00'a çakılıydı (pencere dışı ders `Math.min` clamp'iyle kenara yapışıyordu), aynı saatteki iki ders tam üst üste biniyordu (biri diğerini tamamen gizliyordu). KPI kartlarından biri (`"N kayıt · ₺X"`) 390px'te kırpılıyordu.

### Yapılanlar

- **Ortak yerleşim matematiği** — [week-grid-layout.ts](../frontend/src/lib/week-grid-layout.ts) (yeni): `computeHourWindow` (haftanın gerçek min/max ders saatinden pencere türetir, ders yoksa 09-19 varsayılanı) ve `layoutDayLessons` (klasik takvim çakışma algoritması: kümeleme + greedy sütun atama, çakışan dersleri yan yana yerleştirir). Hem [dashboard/page.tsx](../frontend/src/app/dashboard/page.tsx) hem [dashboard/calendar/page.tsx](../frontend/src/app/dashboard/calendar/page.tsx) buradan besleniyor - önceden iki ekranda ayrı kopyalanmış, ikisi de aynı üç kusuru taşıyan bir saat matematiği vardı. Sürükle-bırak state'i (calendar sayfasına özgü) ayrı kaldı; yalnızca konum/pencere hesabı ortaklaştırıldı.
- **Hafta ızgarası kırılımı** — dashboard önizlemesinde grid artık `md:` (≥768px) itibarıyla açılıyor (önceden `xl:`); tam takvim sayfası `xl:` kaldı çünkü sürükle-bırak için daha geniş sütun genişliği gerekiyor (bilinçli fark, tek bileşene zorlanmadı).
- **Katılım göstergesi** — enstrüman rengi zaten blok içinde metinle okunduğu için hafta ızgarası başlığındaki enstrüman-renk lejantı, RSVP durumunu gösteren bir lejanta (Geliyor/Cevap yok/Gelmiyor) çevrildi; her ders bloğunun sağ üstünde aynı üç renkten bir nokta var. İptal edilen dersler `line-through` + %55 opaklıkla ayrışıyor.
- **Ders detay popover'ı** — dashboard ızgarasındaki bloğa tıklamak artık doğrudan `/dashboard/calendar`'a gitmiyor; öğrenci/enstrüman/saat/öğretmen/RSVP özetiyle ortalanmış bir modal açıyor (`Escape` ile kapanır, "Takvimde aç" bağlantısı var). Anchored bir popover yerine ortalanmış modal seçildi - taşma/konumlandırma riskini sıfıra indiriyor, işlevsel gereksinimi (detay + kapatma + geçiş) karşılıyor.
- **KPI kartı** — ikincil değeri olan kart (`StatCard`'a yeni `secondaryValue` prop) iki satıra bölündü; sayı sözcük içerdiğinde (`"3 kayıt"`) `text-display` yerine `text-title` ölçeğine düşülüyor, çünkü tutar sınırsız büyüyebilir ve dar kartta kırpılıyordu (390px'te bulunup düzeltildi, bkz. altta).
- **Tipografi/renk token'ları** — [globals.css](../frontend/src/app/globals.css)'e 5 kademeli metin ölçeği (`--text-display/title/body/meta/micro`) ve önceden kod içinde dağınık duran hex'ler için token (`--sidebar-from/to`, `--today-tint`, `--success/danger/warning-soft/strong`) eklendi.
- **Öğretmen ders kartı** — [teacher-today-lessons.tsx](../frontend/src/app/dashboard/teacher-today-lessons.tsx): renkli kare blok yerine düz saat bloğu + sol kenarda 3px enstrüman şeridi; enstrüman rengi artık `lesson-colors.ts`'teki paylaşılan paletten (önceden karakter-hash'ine dayalı ayrı bir palet vardı, dashboard ızgarasıyla tutarsızdı). ≥1280px'te 2 sütun.
- **Veli "…" menüsü** — [parent/page.tsx](../frontend/src/app/parent/page.tsx): ayrı "+N öğrenci" ve çıkış butonları tek bir `HeaderMenu`'de birleşti (öğrenci seçimi + çıkış, `Escape`/dış tıklamayla kapanır). Ana Sayfa'daki Son Bildirimler artık göreli zaman (`"2 saat önce"`) + `line-clamp-2` kullanıyor; Mesajlar sekmesindeki tam geçmiş mutlak tarihte kaldı (orada daha faydalı).
- **Giriş ekranı** — seçili rol kartı artık `--brand-soft` zemin + 1.5px `--brand` kenarlıkla belirgin (önceki `ring/8` opaklığı mockup'a göre çok soluktu).
- **OPS-1 takibi** — `npm run lint` artık temiz döndüğü için (önceki oturumun bıraktığı not artık geçerli değil) `.github/workflows/ci.yml`'deki `frontend-build-lint` job'ı açıldı.

### Doğrulama

`npm run build` ve `npm run lint` temiz. Admin/öğretmen/veli akışları gerçek seed veriyle 390/834/1440px'te tarayıcıda gezildi: çakışan dersler yan yana görünüyor, saat penceresi haftanın gerçek erken/geç dersine göre büyüyor (DOM ölçümüyle doğrulandı - 11:00-24:00), popover açılıp `Escape` ile kapanıyor, veli "…" menüsü açılıp kapanıyor, KPI kartı 390px'te artık kırpılmıyor. Konsolda yeni bir hata/uyarı yok. Backend'e dokunulmadığı için `dotnet test` çalıştırılmadı.

### Bilerek atlanan/basitleştirilen

- Hafta ızgarası popover'ı, mockup'ın imzalı anchored konumlandırması yerine ortalanmış modal olarak yapıldı (yukarıda gerekçelendirildi).
- Tam takvim sayfasının sürükle-bırak ızgarası dashboard önizlemesiyle aynı bileşene birleştirilmedi, yalnızca yerleşim matematiği ortaklaştırıldı - sürükle-bırak state'i ekrana özgü kaldı.
- API'de karşılığı olmayan hiçbir mockup detayı bulunmadı; tüm alanlar mevcut `lib/*.ts` sözleşmeleriyle karşılandı.

### Kalan iş

Henüz commit yok - kullanıcı onayı bekleniyor. Push için de ayrıca onay istenecek (repo public).

## UI takip — gerçek hata bulguları ve genel tutarlılık taraması

Kullanıcı canlı önizlemeyi denerken iki şey bildirdi: "sürükle bırak çalışmıyor" ve "taşıdığım ders eski yerinde de kalıyor, yeni yere de taşıyor" + "genel hatları düzelt".

### Bulunan gerçek hata — ertelenen ders iki yerde görünüyor

Kök neden: bir ders ertelendiğinde backend (`Lesson.CreateRescheduled`, `ChangeRequests.cs`) eski kaydı **silmiyor**, `Rescheduled` durumuna çevirip yeni saat için ayrı bir satır açıyor (denetim izi - CLAUDE.md kuralı, doğru davranış). Ama üç frontend listesi (`dashboard/page.tsx` haftalık ızgara, `dashboard/calendar/page.tsx` tam takvim, `teacher-today-lessons.tsx` öğretmen günlük listesi) `Rescheduled` durumundaki eski kaydı hâlâ normal bir ders gibi eski saatinde gösteriyordu - kullanıcının gördüğü "iki yerde ders" tam olarak buydu. Düzeltme: üçünde de kaynağa en yakın noktada `lesson.status !== "Rescheduled"` filtresi eklendi (değişiklik geçmişi zaten `/dashboard/change-requests`'te ayrıca var, canlı takvimde tekrarlanmasına gerek yok).

Bu hatayı ararken kendi test sürüklemelerimden (bilerek geçmiş güne sürükleyip reddedildiğini doğrulamaya çalışırken, `computer` aracının fare tabanlı `left_click_drag`'inin native HTML5 drag event'lerini tetiklemediğini fark etmeden art arda denemeler) veritabanına 3 tane gerçek olmayan `Rescheduled`/yeni-ders çifti sızmıştı - hepsi SQL ile temizlendi, seed veri orijinal haline döndürüldü. Sürükle-bırak mekanizmasının kendisi (gerçek `DragEvent` dispatch'iyle test edildi) bozuk değildi; "çalışmıyor" hissi hem bu görsel çift-ders hatasından hem de geçmiş güne taşımanın sessizce reddedilmesinden kaynaklanıyordu.

Ayrı bir not: bu tur sırasında `abdera-web-api-1` container'ı host RAM baskısı yüzünden SIGSEGV ile çöktü (kod hatası değil, `docker inspect` `OOMKilled=false` ama host'ta neredeyse boş RAM kalmamıştı) - yeniden başlatıldı, veri kaybı olmadı.

### "Genel hatları düzelt" — rakip araştırması + tutarlılık taraması

My Music Staff'ın pazarlama sayfaları incelendi (enstrümana göre renkli ders blokları, tıklayınca hızlı-ekle/detay paneli, ay/hafta/gün/ajanda görünüm geçişi - bkz. kaynak). Abdera'da enstrüman renk kodlaması ve tıkla-detay-aç zaten vardı (bu turun ilk yarısında eklendi); görünüm geçişi (ay/gün) kapsam dışı bırakıldı - master prompt'un "dashboard'u BI projesine çevirme" uyarısına ve mevcut ölçeğe (haftalık tek görünüm yeterli) göre.

Asıl bulgu: `Öğrenciler`, `Öğretmenler`, `Ders Değişikliği Talepleri`, `Bildirimler` sayfaları hâlâ uygulamanın en eski, hiç tasarlanmamış hâlindeydi (`neutral-*` gri paleti, `rounded-md`, düz ASCII ▲▼ oku, `bg-neutral-900` siyah buton) - sidebar'dan bu sayfalara geçildiğinde tutarsız bir sıçrama oluyordu. Dördü de `app-card`/`.field`/`--brand` token sistemine, `Icon` bileşenine ve `--text-*` ölçeğine taşındı; hiçbir veri/mantık/endpoint değişmedi, yalnızca JSX/className.

**Bilerek kapsam dışı bırakıldı:** `billing/*` (price-lists-section.tsx, student-billing-section.tsx) ve `banking/page.tsx` hâlâ eski stilde. İkisi de para hesaplarına dokunan, CLAUDE.md'nin `decimal`/audit kurallarının geçerli olduğu ekranlar - dar bir zaman penceresinde acele bir re-skin'in mantığı da bozma riski taşıması bilerek alınmadı. Ayrı bir görev olarak flag'lendi.

### Doğrulama

`npm run build` + `npm run lint` temiz. Rescheduled-filtre düzeltmesi gerçek `DragEvent` dispatch'iyle uçtan uca test edildi: bir ders geleceğe taşındı, eski slot boşaldı, yalnızca yeni slotta göründü. Öğrenci detay panelindeki veli/kayıt listesi (API container'ı yeniden başlattıktan sonra) doğru veriyle render olduğu doğrulandı. Backend'e dokunulmadı, `dotnet test` çalıştırılmadı.

Sources: [My Music Staff - Calendar & Attendance](https://www.mymusicstaff.com/calendar-attendance/)

### Kalan iş

Henüz commit yok. Sıradaki: `billing`/`banking` sayfalarının aynı tutarlılık geçişi (ayrı görev olarak ele alınmalı - para hesabı içeriyor).
