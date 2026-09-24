# Kararlar ve Açık Sorular

Master prompt (`docs/00-master-prompt.md`) gözden geçirilirken bulunan boşluklar, kesilen fazlalıklar ve onaylanan kararlar. Yeni bir kural eklerken önce burayı kontrol et — sessizce ikinci bir karar üretme.

## Onaylanmış kararlar

| # | Konu | Karar |
|---|---|---|
| — | Repo yapısı | Monorepo — backend, frontend, infra, docs tek repoda |
| — | Stack | .NET 10 (LTS) + ASP.NET Core, Java/Spring Boot yerine (gerekçe: bellek/soğuk başlangıç, aşağıda) |
| — | 4. enstrüman | Keman (`VIOLIN`) eklendi, kendi yetenek tanımlarıyla (`INTONATION`, `BOW_CONTROL`, `LEFT_HAND_POSITION`) |
| A2 | Telafi hakkı doğuşu | Dersten **≥24 saat önce** iptal → 1 `MakeupCredit`. Habersiz gelmeme (no-show) kredi doğurmaz, ücret yine tahakkuk eder. |
| — | Enrollment ↔ enstrüman tutarlılığı | Bir öğrenci bir öğretmene ancak o öğretmenin **çaldığı** (`TeacherInstrument`) bir enstrüman için kaydedilebilir — açık veri hatasını (piyano öğretmenine bateri kaydı) önler. `Modules/People/Features/Enrollments.cs`. |
| — | LessonSeries çakışma kontrolü | Aynı öğretmen veya aynı öğrenci için gün+saat+tarih aralığı çakışan iki `ACTIVE` seri oluşturulamaz (409). Kontrol seri oluşturulurken yapılır, occurrence bazlı değil — Phase 2'de tekil ders/telafi/değişiklik henüz yok, bu yeterli. `Modules/Scheduling/Features/LessonSeriesFeatures.cs`. |
| — | Ders üretim penceresi | Varsayılan 10 hafta (`Scheduling__GenerationWeeks`), seri oluşturulunca otomatik tetiklenir. Pencereyi elle uzatmak için `POST /api/lesson-series/{id}/generate` — Phase 5'e kadar otomatik/periyodik bir zamanlayıcı yok (bilinçli, master prompt'ta da şart koşulmuyor). |
| — | İptal ile erteleme (reschedule) ayrımı | `LessonChangeRequest.proposed_start_at/end_at` ERD'de NOT NULL — yani bu talep her zaman **yeni bir saat önerir**, düz iptal değildir. Düz iptal ayrı bir uç noktadır: `POST /api/lessons/{id}/cancel` (Admin veya kendi dersi için Teacher). Telafi kredisi (A2) yalnızca **iptalde** doğar, ertelemede doğmaz — ders zaten farklı bir saatte gerçekleşecek, telafiye gerek yok. |
| — | Okul kaynaklı iptal her zaman kredi doğurur | A2'nin 24 saat kuralı yalnızca veli kaynaklı iptale uygulanır. Okul kaynaklı iptalde (öğretmen hastalığı, tatil) bildirim süresine bakılmaksızın her zaman `MakeupCredit` doğar — velinin hatası olmayan bir durumda ücreti kaybetmemesi gerekir. `Modules/Scheduling/Features/CancelLesson.cs`. |
| — | Telafi kararı açıkça geçersiz kılınabilir | Yukarıdaki iki kural **varsayılandır**, kilit değildir: `POST /api/lessons/{id}/cancel` gövdesindeki `grantMakeupCredit` (`bool?`) alanı doldurulduğunda karar odur. Takvimdeki ders ayrıntısı penceresinde iki ayrı düğme var — **İptal et + telafi** (`true`) ve **Telafisiz iptal** (`false`) — çünkü tatil, yanlış açılmış ders ya da velinin telafi istemediği iptallerde okul kaynaklı iptalin zorla kredi doğurması yanlıştı (kullanıcı isteği: "dersi telafi etmeden de iptal edebilmeliyim"). Alan boş bırakılırsa eski otomatik türetme çalışır. Seçim `audit_log`'a `MakeupCreditEarned` + `PolicyMakeupCredit` + `MakeupCreditOverridden` olarak yazılır: "bu öğrenciye telafi neden verilmedi" sorusu tek satırdan yanıtlanabilmeli. |
| — | LessonChangeRequest'in Phase 3 kapsamı | Durum makinesindeki `PENDING → APPROVED/REJECTED` yolu tam çalışır. `ALTERNATIVE_PROPOSED`/`PARENT_CONFIRMATION_PENDING`/`PARENT_ACCEPTED`/`PARENT_REJECTED` durumları veliyle WhatsApp üzerinden etkileşim gerektirdiği için (Phase 5) enum'da tanımlı ama hiçbir use-case tarafından henüz üretilmiyor — bilinçli, kayda değer bir eksik (sessizce atlanmadı). |
| — | RSVP'yi kim ayarlar (Phase 3'te) | `POST /api/lessons/{id}/rsvp` yalnızca Admin'e açık, `source=ADMIN` ile kaydeder — WhatsApp (Phase 5) gelene kadar velinin sözlü/telefonla bildirdiği cevabı yönetici girer. Veli, dersin öğrencisiyle `student_guardians` üzerinden ilişkili olmalı, aksi halde `400`. |
| — | Yoklama düzeltmesi audit'e ne zaman düşer | İlk kayıt (öğretmen normal akış) audit'e düşmez; **düzeltme** (var olan kaydı güncelleme) her zaman düşer, aktör Admin ya da Teacher fark etmez — `docs/05-state-models.md`'nin "düzeltme audit_log'a düşer" kuralı. Admin'in **ilk kaydı** kendisi girerse (override) ayrıca audit'e düşer — `docs/04-permissions.md`'nin "gerekirse override edebilir, audit'e düşer" kuralı. |
| — | Bir kayda birden fazla aktif FeePlan olamaz | `POST /api/enrollments/{id}/fee-plan` zaten aktif (`active_until IS NULL`) bir plan varsa 409 döner — hangi tutarın "geçerli" olduğu belirsizleşmesin diye. Fiyat/enstrüman değişikliği gerekiyorsa önce eskisi `End()` ile kapatılmalı (bu akış henüz UI'da yok, API'de mevcut). |
| — | Receivable.OVERDUE geçişi ile ödeme geçişi ayrımı | `docs/05-state-models.md`'nin "OVERDUE -> PARTIAL: kısmi ödeme girildi" okundan anlaşılan: ödeme kaydı asla doğrudan OVERDUE üretmemeli, yalnızca vadeyi hiç kontrol etmeden Paid/Partial hesaplamalı. Bu yüzden `Receivable`'da tek bir `RecalculateStatus` yerine iki ayrı metot var: `RecordPaymentEffect` (yalnızca tutar) ve `MarkOverdueIfPastDue` (yalnızca gecelik sweeper çağırır). Tek metot bu ayrımı bozardı - bkz. `Modules/Billing/Domain/Receivable.cs` yorumu. |
| — | Vadesi geçmiş taraması ilk zamanlanmış iş | `OverdueReceivableSweeper` (saatlik `BackgroundService`) — daha önce "Phase 5'e kadar otomatik/periyodik zamanlayıcı yok" denmişti (ders üretimi bağlamında); bu, saf Billing'in kendi doğruluğu için gerekli olduğundan istisna: WhatsApp/Messaging'e bağımlı değil, Phase 5'i beklemedi. |
| — | `send-reminder` Phase 5'e ertelendi | `POST /api/receivables/{id}/send-reminder` Messaging modülüne (NotificationJob, WhatsApp) bağımlı — o modül olmadan anlamsız, bu yüzden Phase 4'te uygulanmadı, docs/07-api.md'de işaretsiz bırakıldı. |

## Java yerine .NET — gerekçe

| | Spring Boot 3 | ASP.NET Core 10 |
|---|---|---|
| Bellek (idle) | ~300 MB | ~80 MB |
| Soğuk başlangıç | 5–10 sn | <1 sn |

Bu ölçekte (6–8 öğretmen, ~150 öğrenci, ~500 ders/hafta) hiçbir stack'in throughput'u darboğaz olmaz; ucuz bir sunucuda fark yaratan bellek ve soğuk başlangıçta .NET önde. Bileşen karşılıkları `CLAUDE.md`'de.

**Ortam notu:** geliştirme makinesinde `.NET 8.0.123` kurulu; .NET 8 desteği Kasım 2026'da bitiyor. Proje `.NET 10` (LTS, Kasım 2028'e kadar) hedefler — `brew install --cask dotnet-sdk` ile güncellenmeli.

## A — Master prompt'ta tamamen eksik olup eklenenler

| # | Boşluk | Eklenen çözüm |
|---|---|---|
| A1 | Birim fiyat yönetimi yoktu — `FeePlan.amount` her kayda gömülüydü, toplu zam mekanizması yoktu (senin açık talebin) | Yeni **Pricing** modülü: `PriceList` + `PriceListItem`, toplu zam **önizlemeli** uygulanır. `Receivable` oluşurken tutar **snapshot** alınır — geçmişe dönük değişmez. |
| A2 | Telafi hakkının nereden doğduğu tanımsızdı | ≥24 saat önce iptal → kredi (yukarıda) |
| A3 | Öğretmen izni / okul tatili modellenmemişti; dashboard "teacher leave" diyordu ama karşılığı yoktu | `TeacherTimeOff`, `SchoolCalendarDay` (tatil + okul etkinliği, resital dahil — bkz. C5) |
| A4 | Ders değişince eski hatırlatma job'ı iptal edilmiyordu — klasik "yanlış saatte mesaj" hatası | Kural: `RESCHEDULED`/`CANCELLED` geçişinde bekleyen `LESSON_REMINDER` iptal edilir, gerekiyorsa yenisi kurulur |
| A5 | `NotificationJob` idempotency anahtarı verilmemişti | `UNIQUE (type, reference_type, reference_id)` |
| A6 | Sessiz saat yoktu — aidat/doğum günü mesajı gece gidebilirdi | `Notifications__QuietHoursStart/End` (09:00–21:00 varsayılan), yalnızca zamanlanmış job tiplerine uygulanır |
| A7 | WhatsApp 24 saatlik serbest-metin penceresi modellenmemişti | `Guardian.conversation_window_expires_at`, gelen mesajda +24s |
| A8 | Opt-out (STOP) akışı yoktu | `dur/iptal/stop` → rıza kapanır, bekleyen job'lar iptal edilir |

## B — Belirsiz, ileride karar bekleyen (sessizce kapatılmadı)

| # | Soru | Öneri (henüz onaylanmadı) |
|---|---|---|
| B1 | `PACKAGE` tipi paket hangi ders durumunda tükeniyor? | **Hâlâ uygulanmadı.** Phase 4, `PACKAGE`'ı bir `billing_type` seçeneği olarak (fiyat kalemi + ücret planı + tek seferlik `Receivable`) destekliyor, ama "N ders sonra paket biter, otomatik yeni `Receivable` üretilir" tüketim takibi (Attendance→Billing bağlantısı) kodda yok. Öneri hâlâ geçerli: `COMPLETED` + habersiz `ABSENT` düşer; `CANCELLED`/okul kaynaklı iptal düşmez — ama bu, Attendance modülünde bir "paket kredisi düş" adımı gerektirir, henüz yazılmadı. |
| B2 | Devamsızlık aidatı etkiliyor mu? | `MONTHLY` için fiilen doğrulandı: `Receivable.Amount`, `FeePlan`'dan sabit snapshot alınır, hiçbir kod yolu yoklama durumuna göre bu tutarı değiştirmiyor — "devamsızlık aidatı etkilemez" örtük olarak uygulanmış durumda. |
| D4 | Deploy hedefi: Cloudflare + managed Postgres mi, yoksa Raspberry Pi mi (diğer projelerinle tutarlı)? | Phase 7'den önce netleşmeli — yedekleme ve kaynak planlamasını etkiliyor |

**Not:** B3 ve B4 aşağıda kendi öneri notlarıyla birlikte kabul edilmiş kararlar olarak işlendi (net alternatifleri yoktu, tersi mantıksız olurdu); B1 kod düzeyinde hâlâ eksik, D4 hâlâ açık — ikisi de ilgili faz tamamlanmadan önce netleşmeli (B1: bir sonraki Billing/Attendance dokunuşu, D4: Phase 7).

## Kabul edilen varsayımlar (B3, B5 ve devamı)

| # | Konu | Karar |
|---|---|---|
| B3 | `Receivable.OVERDUE` nasıl oluşur | Gecelik job: `due_date < today AND status IN (UNPAID, PARTIAL)` → `OVERDUE`. Saklanan statü, türetilmiş görünüm değil (dashboard sorgusu indexlenebilsin diye). |
| B4 | Oturum mu token mı | httpOnly + Secure + SameSite=Lax cookie. JWT'nin refresh/iptal derdi 8 kullanıcılık sistemde karşılıksız. Türev boşluk: e-posta kanalı olmadığı için öğretmen şifre sıfırlama → yönetici geçici şifre atar, öğretmen ilk girişte değiştirir (`must_change_password`). |
| B5 | React mı Next.js mi | Next.js 15 (App Router) + TypeScript + Tailwind + shadcn/ui + TanStack Query. Öğretmen ekranı mobile-first + PWA. |

## C — Master prompt'ta önerilen ama bu ölçek için fazla olup kesilenler

| # | Fazlalık | Kesinti |
|---|---|---|
| C1 | 8 modül × 4 katman (`api/application/domain/infrastructure`) | Modül başına dikey dilim: `Domain/ Features/ Persistence/` |
| C2 | EF Core üstüne Repository pattern | `DbContext` zaten Unit of Work + Repository; handler'lar doğrudan kullanır |
| C3 | `ProgressSummaryGenerator` AI arayüzü Phase 0'da açılması | Sıfır implementasyonlu arayüz spekülatif soyutlama — Phase 6'ya ertelendi. **2026-09-24: kullanıcı isteğiyle açıldı, bkz. L bölümü.** |
| C4 | Her testte Testcontainers | Yalnızca gerçek Postgres davranışı gerektiren ~8 testte (bkz. `docs/09-testing.md`) |
| C5 | Dashboard'daki "upcoming recital" için ayrı entity | `SchoolCalendarDay`'e `EVENT` tipi olarak girdi, ayrı tablo açılmadı |
| C6 | FluentValidation (denetim ARC-4, `docs/13-audit-fix-prompt.md`) | Paket `csproj`'da duruyordu ama kodda tek bir `AbstractValidator` yoktu - doğrulama her yerde elle `throw new ValidationFailedException(...)` ile yapılıyor. Bu ölçekte (69 endpoint, çoğu tek-iki alanlık kontrol) ayrı bir doğrulama kütüphanesi gereksiz görülüp paket kaldırıldı; mevcut elle doğrulama deseni tek tutarlı yaklaşım olarak korundu. |

## D — Risk ve operasyon notları

| # | Risk | Not |
|---|---|---|
| D1 | Repo **public** | Secret hiçbir zaman commit'lenmez; `.gitignore` + `.env.example` (şablon, gerçek değer yok) ilk commit'te var |
| D2 | Meta WABA onayı günler–haftalar sürebilir, kod bunu beklememeli | `IWhatsAppClient`: `FakeWhatsAppClient` (dev varsayılanı) + `CloudApiWhatsAppClient`; dev-only sahte webhook uç noktası. WABA başvurusu ve `lesson_reminder_rsvp` template'i **paralel, bugün** başlatılmalı — kodla ilgisi yok. |
| D3 | KVKK — çocuk verisi + veli telefonu işleniyor | Zaman damgalı açık rıza (`notification_consent` + `consent_updated_at`), aydınlatma metni (ürün/hukuk tarafı, bu repo kapsamı dışı), saklama süresi, ayrılan öğrenci için silme (mali kayıt hariç) — Phase 7'den önce netleşmeli |
| D4 | Deploy hedefi belirsiz | Yukarıda B tablosunda — açık |
| D5 | Saat dilimi | Türkiye 2016'dan beri sabit UTC+3, DST yok. Yine de DB'de `timestamptz` (UTC instant), yerel hesap `Europe/Istanbul` konfigürasyondan |
| D6 | MVP birebir ders varsayıyor (`Lesson.studentId` tekil) | Bilinçli sınır — grup dersi (teori, orkestra) gelirse şema değişir |
| D7 | Kardeş indirimi | MVP dışı, ama `PriceList` tasarımı bunu ileride engellemez |

## E — MVP kapsamı sonradan genişletilen kararlar

`docs/00-master-prompt.md` satır 470 ve 1009: *"Do not add online payment, bank reconciliation... Do not implement... bank integration..."* — bilinçli bir MVP sınırıydı. Kullanıcı Faz 5'ten sonra bunu açıkça istedi ve aşağıdaki kapsam/yaklaşımı onayladı; CLAUDE.md'nin "Yapılmayacaklar" kuralının gerektirdiği açık onay budur.

| # | Konu | Karar |
|---|---|---|
| E1 | Banka entegrasyonu — kapsam | Yalnızca **gelen havale/EFT'nin otomatik olarak `Receivable`'a işlenmesi** (tahsilat). Online ödeme/checkout (veli sitede kart girip ödeme yapması), e-fatura, muhasebe entegrasyonu **hâlâ kapsam dışı** — bunlar ayrı, henüz onaylanmamış kararlar. |
| E1 | Yöntem | **Sanal IBAN** (isim eşleştirme değil — bkz. gerekçe aşağıda). Her veliye (`Guardian`) bir sanal IBAN atanır; o IBAN'a gelen her transfer sağlayıcının webhook'uyla bildirilir. |
| E1 | Sağlayıcı | **Henüz seçilmedi.** WhatsApp'taki D2 deseninin birebir aynısı: `IBankPaymentProvider` portu + `FakeBankPaymentProvider` (dev/test varsayılanı, gerçek sağlayıcı hesabı gerektirmez) ile kod bekletilmeden ilerler. Gerçek sağlayıcı (PayTR/Papara İşletme/banka Sanal IBAN ürünü) seçilince yalnızca yeni bir `IBankPaymentProvider` implementasyonu eklenir, iş mantığı değişmez. |
| E1 | Eşleştirme neden isimle değil tutarla | Gönderen adı güvenilmez (farklı hesaptan gönderim, aynı isimli birden fazla veli, ad/soyad varyasyonu) — parada yanlış eşleştirme kabul edilemez bir risk. Sanal IBAN zaten *hangi veli* olduğunu kesin verir; geriye yalnızca *hangi Receivable* sorusu kalır, bu da tutar (+ varsa açıklama alanındaki dönem bilgisi) ile çözülür. |
| E1 | Belirsiz eşleşme davranışı | Veli'nin birden fazla açık `Receivable`'ı varsa ve gelen tutar tam olarak yalnızca birine denk gelmiyorsa **otomatik uygulanmaz** — `NeedsReview` durumunda admin panelinde bekler, admin elle hangi aidata sayılacağını seçer. Sessizce tahmin etmek yerine insan onayına düşmek tercih edildi (WhatsApp opt-out/RSVP'deki "belirsizlikte otomatik davranma" ilkesiyle tutarlı). |
| E1 | `Payment.CreatedBy` | Otomatik eşleşen ödemelerde bir admin yok — `CreatedBy` nullable'a çevrildi (mevcut `AuditLog.ActorUserId`'nin zaten nullable olup sistem-kaynaklı olayları `null` ile işaretlediği kurala uyumlu, bkz. `guardian.opted_out`). |
| E1 | Faz | Phase 6 olarak Progress modülünün önüne alındı — Billing zaten hazır olduğu için doğal bir devam, Progress'in (yetenek takibi) kullanıcı için aciliyeti yok. |

Ayrıntılı akış/entity tasarımı: `docs/12-bank-integration.md`.

| # | Konu | Karar |
|---|---|---|
| E2 | Dashboard modülü zamanlaması (denetim ARC-6, `docs/13-audit-fix-prompt.md` madde 13) | `docs/02-modules.md`/`docs/07-api.md` bir Dashboard modülü ve `GET /api/dashboard/today` tanımlıyordu ama hiç yazılmamıştı, frontend ana sayfası yer tutucuydu. Kullanıcıya "şimdi mi, Faz 7'ye mi" soruldu - **şimdi yazılsın** kararı verildi (veri zaten yerinde: öğrenci/öğretmen/ders/aidat sayıları tek bir salt-okunur projeksiyon sorgusuyla toplanabiliyor). |

## F — Veli web paneli: WhatsApp tek kanal olarak kalır

Denetim UX-3 (`docs/13-audit-fix-prompt.md` madde 13): sistem veli verisini/RSVP'sini/aidat durumunu tutuyor ama velinin bakabileceği hiçbir web ekranı yoktu, yalnızca WhatsApp üzerinden etkileşim vardı - bu daha önce netleşmemiş bir belirsizlikti (ne yasak ne planlıydı). Kullanıcıya "ayrı bir faz olarak planlansın mı, yoksa WhatsApp tek kanal olarak mı kalsın" soruldu.

**Karar (ilk hali): WhatsApp tek kanal olarak kalır, ayrı bir veli web paneli planlanmıyor.**

Gerekçe: bir veli web paneli açmak yalnızca yeni ekranlar değil, yeni bir kimlik doğrulama modeli de gerektirir (veli nasıl giriş yapacak - e-posta yok, `docs/10-decisions.md` B4; telefon numarasıyla OTP mi, farklı bir mekanizma mı?) - bu, mevcut `User`/cookie-oturum modelinin dışında tamamen yeni bir yüzey. WhatsApp zaten RSVP, aidat hatırlatması, telafi ve opt-out akışlarını uçtan uca karşılıyor; veli tarafında ek bir kanal açmanın bu ölçekte (6-8 öğretmen, ~150 öğrenci) karşılığı şimdilik yok. Bu MVP sınırı bilinçli olarak korunuyor - ihtiyaç netleşirse ayrı bir faz olarak yeniden değerlendirilir.

**Karar F reversal (2026-08-21):** kullanıcı `/parent` web ekranındaki "Geliyorum/Gelemiyorum" yanıtının gerçekten veritabanına kaydedilmesini istedi - bu, RSVP'yi "kime ait" diye işaretleyebilmek için gerçek bir veli kimliği/oturumu gerektirdiğinden yukarıdaki karar bilinçli olarak kısmen tersine çevrildi. Kimlik doğrulama yöntemi olarak **telefon numarası + WhatsApp OTP** seçildi (Guardian zaten telefonla tanımlı, WhatsApp gönderim altyapısı zaten var - yukarıdaki gerekçede sorulan "hangi mekanizma" sorusunun cevabı bu). `users` tablosuna dokunulmadı; Guardian oturumu ayrı bir `ClaimsPrincipal` (Role=Guardian) üzerinden kurulur, `User`/şifre modeliyle hiçbir ilgisi yok - bkz. `Modules/People/Features/GuardianAuth.cs`, `Modules/People/Features/GuardianPortal.cs`, migration `012_guardian_login_codes`.

Kapsam bilinçli olarak dar tutuldu: veli yalnızca **kendi öğrencisinin listesini, kendi takvimini ve kendi RSVP'sini** görebilir/ayarlayabilir. Aidat ve mesaj geçmişi de salt-okunur olarak veli oturumuna bağlandı; ödeme yapma, aidat oluşturma veya okul adına serbest mesaj gönderme yetkisi yoktur. WhatsApp, zamanlanmış hatırlatmalar ve serbest-metin sohbet için birincil kanal olmaya devam ediyor - bu yalnızca velinin kendi verisine *bakabileceği* ek bir web erişimi, WhatsApp akışlarının yerini almıyor.

**Karar F (ikinci) reversal (2026-09-12):** kullanıcı, toplu kurulum (20 öğretmen × 35 öğrenci) sırasında veli girişinin **kalıcı şifre** ile olmasını istedi (OTP her seferinde kod istemek yerine). Kullanıcı adı = telefon numarası; ilk şifre çocuğun adı + veli adı + telefon son 4 hanesinden türeyen mnemonik bir desenle üretilir (`Shared/GuardianPasswordGenerator.cs`, örn. "Zeynep"/"Ayşe"/…4567 → `Zeyay4567`) ve WhatsApp'tan gönderilir. `Guardian` tablosuna `password_hash` (nullable) eklendi (migration `AddGuardianPasswordHash`); OTP akışı geri uyum için korundu ama birincil giriş artık şifre. `users` tablosuna hâlâ dokunulmadı — Guardian oturumu yine ayrı `ClaimsPrincipal` (Role=Guardian). Yeni uçlar: `POST /api/guardian/login` (telefon+şifre), admin `POST /api/guardians/{id}/reset-password` (şifre üret + WhatsApp gönder + düz metni bir kez döndür). Frontend `/parent/login` telefon+şifre birincil, OTP ikincil. Ayrıntı ve fix listesi: `docs/13-toplu-kurulum-ve-fix-list.md`. **Güvenlik notu:** şifre kamuya açık bilgiden türediği için yalnızca ilk şifredir; "ilk girişte değiştir" akışı FIX-BACKLOG'da.

## G — Faz 4: sağlık kontrolü, yedekleme, e-posta alarmı

Kullanıcının açık talebi ("aidatlar konusu çok önemli, burayı backup sistemi ile düşünmemiz gerekir... backup alma kısmını çok dikkate almalısın") — master prompt'ta yoktu, docs/15-product-phases.md Faz 4 olarak planlandı.

| # | Konu | Karar |
|---|---|---|
| G1 | Yedekleme hedefi | Kullanıcıya üç seçenek sunuldu: S3 uyumlu depolama (Backblaze B2/R2 - en basit kurulum), Google Drive (OAuth/servis hesabı gerektirir, en karmaşık), kendi sunucusuna SFTP/SSH. Kullanıcı **kendi sunucusuna SFTP/SSH**'i seçti - zaten erişimi olan bir sunucu, yeni bir bulut hesabı açmasına gerek yok. |
| G2 | SFTP kütüphanesi | CLI'daki `sftp`/`scp`'yi `Process.Start` ile çağırmak yerine **SSH.NET (Renci.SshNet)** eklendi - host-key doğrulama, kimlik bilgisi yönetimi, hata kodları CLI tarafında daha kırılgan olurdu; yedekleme veri güvenliği açısından kritik (kullanıcının kendi vurgusu), test edilebilir/olgun bir .NET kütüphanesi tercih edildi. Tek yeni bağımlılık. |
| G3 | Şifreleme | AES-256-GCM, .NET'in yerleşik `System.Security.Cryptography.AesGcm`'i ile - ek bir kütüphane gerekmedi (CLAUDE.md "gereksiz bağımlılık ekleme"). Anahtar `Backup__EncryptionKey` (base64, 32 byte) - `openssl rand -base64 32` ile üretilir, asla commit edilmez. |
| G4 | Saklama süresi | 1 ay (kullanıcının talebi), `Backup__RetentionDays` ile parametrik (varsayılan 30) - kullanıcı isterse .env'den değiştirebilir, kod değişikliği gerekmez. |
| G5 | Yedekleme zamanlayıcısı | CLAUDE.md'nin Hangfire/Quartz yasağı nedeniyle (Faz 3'teki G/Hangfire kararıyla aynı gerekçe) mevcut `BackgroundService + PeriodicTimer` deseni (`NotificationDispatcher`/`OverdueReceivableSweeper` ile aynı) - OS cron'a bağımlı olmadan "bugün henüz çalışmadıysa ve saati geçtiyse çalıştır" mantığıyla günlük tetiklenir (`Backup__DailyRunTimeLocal`, varsayılan 03:00). |
| G6 | E-posta sağlayıcısı | Kullanıcı "en kolay hangisiyse o olsun" dedi - **Gmail SMTP + Uygulama Şifresi** önerildi (çoğu kullanıcının zaten Gmail hesabı var, yeni bir servise kaydolmaya gerek yok, domain doğrulama istemiyor). Kod herhangi bir SMTP sağlayıcısına bağımlı değil (`Email__Smtp__Host` vb. konfigüre edilir). Gönderim için .NET'in yerleşik `System.Net.Mail.SmtpClient`'ı kullanıldı - düşük hacimli alarm e-postası için MailKit gibi ek bir bağımlılık gerekmedi. |
| G7 | Sağlık durumu hesaplanması | Tek bir periyodik `SystemHealthMonitor` (BackgroundService, varsayılan 10 dk) hem DB bağlantısını (`HealthCheckService`, zaten var olan `/health` altyapısı) hem son başarılı yedeklemenin yaşını kontrol eder - `Healthy`/`Degraded` (`Ops__BackupStaleAfterHours`, varsayılan 30 saat)/`Unhealthy` (`Ops__BackupUnhealthyAfterHours`, varsayılan 48 saat, veya DB down, veya son yedekleme hiç başarılı olmadan başarısız oldu). Sonuç tek satırlık `SystemHealthStatus`'a yazılır (`NotificationAutomationSettings` ile aynı singleton desen), dashboard bunu okur. |
| G8 | Alarm sıklığı | Sorun devam ettiği sürece e-posta her kontrol turunda tekrar gitmesin diye `Ops__AlertCooldownMinutes` (varsayılan 60 dk) soğuma süresi var. Durum tekrar `Healthy`'ye dönünce ayrıca bir "düzeldi" e-postası gider (soğuma süresine tabi değil - tek seferlik iyi haber). |
| G9 | Geri yükleme | Uygulama içinde bir "restore" düğmesi **bilinçli olarak eklenmedi** - bir yedeği geri yüklemek mevcut veriyi yok edebilecek, tek yönlü bir işlem; bu, arayüzden kazayla tetiklenebilecek bir risk taşır. Bunun yerine `BackupEncryption.DecryptFileAsync` (manuel kurtarma için) yazıldı ve docs/15-product-phases.md'nin "örnek geri yükleme provası" kabul kriteri elle, `docker compose` ile bir doğrulama turu olarak yapılacak - kalıcı bir uygulama özelliği değil. |

## H — Aidat modelinin yeniden tasarımı (2026-09-16)

Kullanıcının tespiti: "Aidat sistemini baştan tasarla, bana nedense hâlâ çok karmaşık geliyor.
Müzik okulunda öğrencilerin aidatlarını gireceğiz, bu kadar basit. Tek bir farklı nokta var:
yıl başlarında toplu aidat kampanyası."

Teşhis: tek bir aidat satırı görebilmek için dört kavramdan geçmek gerekiyordu — `price_lists`
(tarihli kap) → `price_list_items` (enstrüman × ders süresi × aylık/paket × paket ders sayısı)
→ `fee_plans` (kurs kaydı başına, kalemden snapshot + vade günü) → `receivables`. Okulun gerçek
fiyat tablosu ise **iki satır**: Birebir 4 ders 6.000 TL, Grup (Resim) 4 ders 4.500 TL — ve
fiyat ne enstrümana ne de ders süresine göre değişiyor. Model, önemsiz eksenlerde fazla
ayrıntılı; gerçekten gereken eksenleri (%5 çoklu kurs/kardeş indirimi, toplu ödeme indirimi)
ise **hiç ifade edemiyordu**. Kanıt: canlı veritabanında 0 fiyat listesi, 0 ücret planı,
0 aidat vardı — ekran boş değildi, sistem üç adım öncesinde takılıydı.

| # | Konu | Karar |
|---|------|-------|
| H1 | Fiyat ekseni | `price_lists` + `price_list_items` + `fee_plans` ve **Pricing modülünün tamamı kaldırıldı**. Yerine tek tablo: `tuition_rates (course_kind, lessons_per_month, monthly_amount, effective_from, effective_until)`. Fiyatın tek belirleyicisi `Enrollment.CourseKind` (Birebir/Grup) — enstrüman ve ders süresi tutarı etkilemiyor. Migration sırasında veri kaybı riski yoktu (ilgili tabloların tamamı boştu). |
| H2 | Grup dersi | Kullanıcıya iki seçenek sunuldu: kurs kaydına `Birebir/Grup` alanı eklemek, ya da fiyatı doğrudan kursa (Resim → 4.500) bağlamak. **Alan eklendi** — ileride "grup piyano" veya "birebir resim" açılırsa model kırılmıyor. `Resim` enstrümanı migration ile seed edildi. |
| H3 | Zam | Ayrı bir "toplu güncelleme" işlemi (eski `Pricing/BulkUpdate.cs`) kaldırıldı. Zam = yeni `effective_from` ile yeni satır; öncekisi bir gün öncesinden otomatik kapanır. Geçmiş aidatlar tutarını kendi satırında taşıdığı için değişmez (A1 korunuyor, kapsamı genişledi — artık taban tutar, indirim yüzdesi ve gerekçesi de donuyor). |
| H4 | İndirim birleşimi | Kullanıcıya üç seçenek sunuldu (en yüksek / toplama / ardışık). **En yüksek olan uygulanır** seçildi: %5 + %5 = %5. Gerekçe — veliye açıklaması en kolay olan ve sürpriz indirim üretmeyen kural. |
| H5 | İndirimin yeri | Öğrenci başına saklanan bir veri değil, kurum geneli bir politika: `billing_settings` (çoklu kurs %, kardeş %, vade günü) + `prepay_discount_tiers`. Kurs kaydına özel istisna için `enrollments.manual_discount_percent/reason` — doluysa otomatik kuralların **yerine** geçer (admin bilinçli karar vermiştir). |
| H6 | Kardeşlik | Ayrı bir "aile" entity'si açılmadı — `student_guardians` zaten çoktan-çoğa. Ortak velisi olan ve **aktif kaydı bulunan** öğrenciler kardeş sayılır; velinin ders almayan ikinci çocuğu indirim doğurmaz. |
| H7 | Toplu ödeme kampanyası | Kullanıcıya üç seçenek sunuldu (kademeli / tek oran / her seferinde elle). **Ay sayısına göre kademeli** seçildi; başlangıç kademeleri 4 ay %5, 10 ay %10 olarak seed edildi, ekrandan değiştirilebilir. Peşin indirimi öğrenci indiriminin **üstüne**, bileşik uygulanır (%5 ve %10 birlikte %15 değil %14,5 eder). |
| H8 | Peşin ödemede tutarı kim hesaplar | **Sunucu.** Eski `BulkPayments.cs` istemcinin gönderdiği tutarın seçilen ayların *indirimsiz* toplamına birebir eşit olmasını şart koşuyordu ("Ödeme tutarı bu toplamla aynı olmalı") — kampanya indirimi uygulanmış bir ödeme bu kontrolden asla geçemezdi, yani **kampanya sisteme hiç girilemiyordu**. Artık istemci yalnızca gördüğü toplamı (`expectedTotal`) teyit eder; ekranla sunucu ayrışmışsa işlem 409 ile durur. |
| H9 | Açılmış ama ödenmemiş ay | Kampanya, normal tarifeyle açılmış ödenmemiş bir ayı iptal edip yenisini açmaz — aynı satırı kampanya oranıyla **yeniden fiyatlar** (`Receivable.Reprice`). Üzerinde ödeme bulunan bir aya dokunamaz: tahsil edilmiş parayla tutarsız bir tutar yazmak eksik/fazla bakiyeyi sessizce gizlerdi. |
| H10 | Vade günü | Ücret planı başına sorulan `due_day` kaldırıldı, okul geneli tek ayara taşındı (`billing_settings.due_day_of_month`, 1–28). Plan başına sormak gereksiz bir soruydu. |
| H11 | Adlandırma | `payments.bulk_payment_id/months` → `prepay_plan_id/months`. "Toplu ödeme" hem "ay başında toplu aidat üretimi" hem "peşin ödeme" için kullanılıyordu; iki farklı iş aynı adı taşıyordu. |
| H12 | Ekran mimarisi (2026-09-22) | Kullanıcı geri bildirimi: "aidat kısmında karışıklık var; bir normal aidat var, bir de toplu aidat yatırma var" + "dönem aidatı oluştur ne demek anlamadım". Model doğruydu, **ekran** karışıktı: toplu ödeme öğrenci künyesinin içinde açılır bir "Peşin ödeme al" bölümünün altında, kurs kaydı başına ayrı form olarak gizliydi; "Dönem aidatlarını oluştur" düğmesi de borç AÇMA ile PARA ALMA'yı aynı cümlede anlatıyordu. Karar: `Aidat yönetimi` üç sekmeye ayrıldı — **Aylık aidatlar** (ayın borç satırlarını aç + tek tek tahsil et), **Toplu ödeme** (birkaç ayı tek seferde tahsil et), **Fiyat politikası** (tarife + indirim kuralları). Toplu ödeme tek bir ekran ve üç adım: öğrenciyi ara → kaç ay → kaydet; kademeler politikadan çekilip düğme olarak gösterilir. Öğrenci künyesindeki ikinci giriş noktası kaldırıldı. Uç noktalar ve hesap AYNEN korundu (H7/H8/H9) — bu bir arayüz kararıdır, fiyat kararı değil. Tek sözleşme değişikliği: `/api/students/search` artık satır başına `enrollmentId` + `courseKind` de döner, böylece ekran seçimden sonra ikinci istek atmaz. |
| H13 | Kardeş indirimi artık çıkarım değil (2026-09-22) | Kullanıcı sorusu: "kardeş indirimini neye göre seçiyorsun, onu biz checkbox ile belirtelim." H6'daki "ortak veliye bağlı 2+ aktif öğrenci = kardeş" çıkarımı **kaldırıldı**; yerine `students.sibling_discount` (boolean) geldi ve öğrenci künyesindeki kutuyu **yalnızca Admin** işaretler. Çıkarım iki yönde de yanılıyordu: aynı veli iki kez kaydedildiyse (farklı telefon/yazım) gerçek kardeşler indirim ALAMIYOR, bir veli akraba/komşu çocuğuna da bağlıysa kardeş olmayan ALIYORDU; ayrıca kardeşlerden biri kursa ara verince diğerinin indirimi sessizce düşüyordu. Migration `ExplicitSiblingDiscount` kutuyu **eski kuralın o an kimi kardeş saydığıyla doldurur** - deploy anında kimsenin tutarı değişmez. **Çoklu kurs indirimi çıkarım olarak kaldı** (bilinçli): aktif kurs kaydı sayısı sistemin kendi verisi, tahmin değil. Öğretmenin künye düzenlemesi alana dokunamaz (`UpdateRequest.SiblingDiscount` `bool?` - gönderilmezse değişmez, Admin değilse yok sayılır). |
| H14 | Aylık aidat üretimi artık elle onaylanmıyor (2026-09-22) | Kullanıcı geri bildirimi: "her öğrenci kayıt olduğunda en az 1 sene her ay haftada 4 ders olacak şekilde gelmeli. aidat öde dendiğinde ödeyecek o kadar buna gerek var mı" - H12'nin "Aylık aidatlar" sekmesindeki önizle+onayla akışını gereksiz buldu, çünkü her kayıt zaten sabit haftalık bir taahhüt: hangi aidatın açılacağı elle karar verilecek bir şey değil, `MonthlyDueRun.BuildPlanAsync` zaten deterministik. Tek çekince fiyat snapshot'ının kilitlendiği an kontrolsüz kalması idi; kullanıcı bunu da netleştirdi: "fiyat zaten belli, güncelleme olduğunda o andan sonraki aidatlar güncel fiyattan alınacak demektir" - yani `tuition_rates`'in `effective_from/until` zinciri zaten bunu deterministik çözüyor, ekstra bir onay adımına gerek yok. Karar: `Modules/Billing/Infrastructure/MonthlyReceivableGenerator.cs` (yeni `BackgroundService`, `OverdueReceivableSweeper` ile aynı desen) her gün okul-yerel tarihe göre o ayın aidatlarını `MonthlyDueRun.RunAsync(..., actorId: null, throwIfEmpty: false)` ile otomatik açar - `AuditLog.ActorUserId` sistem-kaynaklı olay kuralıyla `null`. `POST /api/receivables/monthly-run` **kaldırılmadı**, AdminOnly kaçış kapısı olarak duruyor (servis uzun süre çalışmazsa veya geçmiş bir dönem elle telafi edilecekse); yalnızca rutin arayüz yüzeyi ("Aylık aidatları oluştur" butonu + önizleme modalı, `dues-list-section.tsx`) kaldırıldı. Ücret tarifesi tanımlanmamış bir ders türü (nadir bir kurulum eksikliği) artık admin'i bloklamaz, yalnızca loglanır. |
| H15 | "Aylık aidatlar" listesi dönem-önce yerine öğrenci-önce (2026-09-22) | Kullanıcı geri bildirimi: "tüm öğrencileri listele, bir öğrenciye tıkladığımda altında ayların olduğu takvim açılsın, geçmiş de dahil ödemeleri göstersin, aya tıklayarak da tahsil et denebilsin" + "default olarak tüm öğrenciler listelensin, harf ikonu kaldır, bekleyen/ödenen ayrımı olmasın, Geçmiş yerine Detay". H12'nin "dönem defteri" listesi (önce ay seçilir, o ayın borç satırları gösterilir) o ayda borcu olmayan veya seçili durum sekmesine girmeyen bir öğrenciyi listeden TAMAMEN düşürüyordu - "tüm öğrenciler" sorusuna cevap vermiyordu. Karar: `dues-list-section.tsx`'teki liste artık `/api/students/overview`'dan tüm öğrencileri (varsayılan filtresiz) satır satır listeler; her satırın "Detay"ı (eski adıyla "Geçmiş") zaten var olan 12 aylık takvimi (`PaymentHistoryCollapse`) açar - bu takvim artık salt-okunur değil, seçili aydaki her kurs satırına kendi "Tahsilat" formu eklendi (`ReceivablePeriodCard`). Öğrenci satırındaki harf-baş avatarı ve Bekleyen/Ödenen/Tümü durum sekmeleri kaldırıldı; öğretmen filtresi ve öğrenci arama korundu. "Kim borçlu" triyajını kaybetmemek için iki şey bilinçli olarak tutuldu: (1) sayfa üstündeki özet kartları (page.tsx, tüm okulun açık/gecikmiş/tahsil edilen toplamı) dönem/durum filtresinden bağımsız her zaman görünür kalmaya devam ediyor, (2) her satırda o ayki (bu ay) durumu gösteren tek bir rozet var - filtrelemiyor, yalnızca hangi öğrenciyi aramak gerektiğini gösteriyor. Uç noktalar ve hesap AYNEN korundu - bu da H12 gibi saf bir arayüz kararı. |
| H16 | Kurs kaydı açılınca o ayın aidatı hemen açılır (2026-09-23) | Ürün sahibi kuralı: "Bir öğrenci kayıtlıysa her ay ödeme yapabiliyor olmalı." H14'teki `MonthlyReceivableGenerator` yalnızca açılışta + 24 saatte bir çalıştığı için ay içinde açılan bir kurs kaydı o ayın aidatını en fazla 24 saat göremiyordu; Aylık aidatlar ekranı aktif kayıtlı öğrenciye "Bu ay kayıt yok" diyor, tahsilat yolu sunmuyordu. Karar: `Modules/Billing/Infrastructure/EnrollmentReceivableOpener.OpenCurrentPeriodAsync` — `POST /api/students/{id}/enrollments` ve `POST /api/teachers/{id}/students` kurs kaydını kaydettikten HEMEN SONRA (çoklu kurs indirimi yeni kaydı da saysın diye) bu açık Billing servisini çağırır. Fiyatlama `MonthlyDueRun` ile aynı (`TuitionPricer` + `Receivable.Create`), audit aksiyonu `receivable.enrollment_opened`. İdempotent: (kayıt, dönem) için satır varsa hiçbir şey yapmaz, `UNIQUE (enrollment_id, period)` yarışında da sessizce geçer. Başlangıcı ileri bir ayda olan kayıt bu ayı açmaz (o ayı zamanı gelince aylık üretim açar). Tarife yoksa aidat açılmaz ama kurs kaydı başarısız olmaz. Durdurulmuş kaydı yeniden aktifleştiren bir akış bugün yok (`Enrollment.SetStatus` çağrılmıyor); eklenirse aynı servisi çağırmalı. Arayüz: Aylık aidatlar satırındaki rozet "Aidat açılmadı" oldu; Detay → Aidat takvimi, bu ayın satırı olmayan aktif kayıt için "Bu ayın aidatını aç" (mevcut `POST /api/receivables`) sunar ve 409'da sunucunun sebebini gösterir — ayrı bir giriş noktası değil, H12'nin Aylık aidatlar işi. **Açık karar:** `TuitionPricer.RateFor` tarifeyi dönemin İLK gününe göre seçer; ay ortasında yürürlüğe giren bir tarife o ayı kapsamaz. Bu kural değiştirilmedi (ürün kararı bekliyor), yalnızca sebep ekranda görünür kılındı. |

Kaldırılan uçlar: `/api/price-lists*`, `/api/enrollments/{id}/fee-plan`, `/api/receivables/bulk*`,
`/api/enrollments/{id}/bulk-payments`. Yerine: `/api/tuition-rates`, `/api/billing-policy`,
`/api/receivables/monthly-run`, `/api/enrollments/{id}/prepay-preview` + `/prepay-plans`,
`PATCH /api/students/{id}/enrollments/{id}`.

Hesabın tamamı tek bir saf sınıfta: `Modules/Billing/Domain/TuitionCalculator.cs` (birim testleri
`TuitionCalculatorTests.cs`). Uçtan uca akış `TuitionAndDuesFlowTests.cs` ile gerçek Postgres'e
karşı doğrulanır.

## I — Yıl sonu gösterisi ve kalıcı kişi silme (2026-09-16)

| # | Konu | Karar |
|---|------|-------|
| I1 | Kalıcı silme | Kullanıcının açık talebi: "öğretmen gitti diyelim neden pasife alıyorsun? tamamen silme opsiyonu olmalı." Master prompt ve CLAUDE.md "mali/devamsızlık geçmişi olan kayıt silinmez" diyordu ve arayüzde hiçbir kaldırma yolu yoktu (API'de yalnızca `PATCH ... Status=Inactive` vardı, o da ekrana bağlanmamıştı). **Gerçek `DELETE` eklendi**, üç korumayla: (1) `/deletion-impact` işlemden önce ne silineceğini sayar, (2) kaydedilmiş ödeme varsa 409 döner ve devam etmek için açık `force` gerekir, (3) öğretmende `reassignTo` ile öğrenciler başka öğretmene devredilebilir — öğrencilerin ders/aidat geçmişine hiç dokunmadan. Silme işlemi `audit_log`'a yazılır, audit hiçbir zaman temizlenmez. |
| I2 | Silmenin uygulanışı | Tek transaction içinde ham SQL (`PersonEraser.cs`). EF cascade veya DB cascade tek başına yetmiyordu: referans veren 15 tablonun çoğunda FK yok (modüller arası bağlar açık id sorgularıyla kurulur). Eksiksizlik `PersonDeletionFlowTests`'teki yetim-satır testiyle korunuyor. |
| I3 | Veli silinmez | Öğrenci silinince velisi otomatik silinmez; yalnızca "N veli artık hiçbir öğrenciye bağlı değil" olarak raporlanır. Kişisel veriyi kullanıcının haberi olmadan silmek yerine kararı ona bırakmak tercih edildi. |
| I4 | Banka işlemi silinmez | Silinen bir aidata bağlı `bank_incoming_transaction` harici bir kayıttır: silinmez, yalnızca eşleşmesi kopar ve `NeedsReview`'a döner (E1'deki "belirsizlikte insan onayı" ilkesiyle tutarlı). |
| I5 | Yıl sonu gösterisi — kapsam | Kullanıcı isteği: "öğrenciler belli bir grupta ve sırayla enstrümanlarını çalacağı bir organizasyon, öğrencinin bir fotosu, çaldığı eserler, ve şu an çalacağı eser büyük punto ile." Benzer araçlar araştırıldı (resital yazılımları: Pembee/RecitalDash/CompuDance; run-of-show araçları: StageManager.tech, Rundown Studio, VI-Stage) ve iki standart yüzey alındı: **Program** (sıralı akış, eser + besteci, öğretmen, süre, basılabilir seyirci programı) ve **Sahne** (ŞU AN / SIRADAKİ / ONDAN SONRAKİ, tek tuşla ilerletme, geçen süre ile planlanan sürenin karşılaştırılması). |
| I6 | Gösteri veri modeli | Ayrı bir "bölüm" tablosu **açılmadı**: grup adı satırın kendisinde (`show_items.group_name`) taşınır. Bir okul resitalinde bölüm sıralamayı değiştiren bir yapı değil yalnızca bir başlık; ayrı tablo sürükle-bırak sıralamayı iki boyutlu hâle getirip karşılığı olmayan bir karmaşıklık eklerdi. Bir öğrencinin her eseri ayrı satırdır — sahne işaretçisi eser bazında ilerlemek zorunda. |
| I7 | Sahne işaretçisi nerede durur | Veritabanında (`show_events.current_item_id`), tarayıcıda değil. Sahne ekranı, kulis tableti ve yöneticinin ekranı aynı anı görmek zorunda. Websocket eklenmedi (CLAUDE.md'nin "bu ölçekte gerçekten gerekli mi" kuralı); ekran birkaç saniyede bir yeniler. Satır `xmin` ile optimistic concurrency taşır — iki ekrandan aynı anda ilerletme sırayı atlatmasın. |
| I8 | Öğrenci fotoğrafı | Dosya sistemine değil **veritabanına** (`student_photos`, ayrı tablo). Gerekçe: projenin günlük şifreli yedeklemesi (G-kararları) veritabanını kapsıyor, bir volume'u kapsamıyordu — fotoğraflar böylece ek iş yapılmadan yedekleniyor. `students` satırını şişirmemek için ayrı tabloda; program yanıtı yalnızca sürüm anahtarını taşır, baytları değil (ETag ile önbelleklenir). En fazla 2 MB, JPEG/PNG/WebP. |
| I9 | Gösteri izinleri | Program ve sahne ekranı **öğretmene de açık** (kulisteki öğretmen kendi öğrencisinin kaçıncı sırada olduğunu görmek zorunda); düzenleme ve sahne kontrolü Admin. Aidattan farklı olarak program operasyonel bir belge, mali veri değil. |

## J — Öğretmen kendi öğrencisini yönetir (2026-09-16)

Kullanıcı isteği: "öğretmenler kendi öğrencilerini ekleyebilirler kendi portallarında. buna
izin verelim. silme için yönetici onayı gereksin sadece. düzenleme de yapılabilir."

| # | Konu | Karar |
|---|------|-------|
| J1 | Ekleme/düzenleme | `docs/04-permissions.md`'deki "Öğrenci/veli oluşturma, düzenleme: TEACHER ❌" satırı bilinçli olarak değiştirildi. Öğretmen kendi adına öğrenci + ilk kursunu tek çağrıda açar (`POST /api/teachers/{id}/students`), kendi öğrencisini düzenler, ona yeni kurs ekler ve **velisini girebilir**. Kurs KALDIRMA öğretmende değil — o da bir silmedir. |
| J2 | Silme | Doğrudan silme öğretmene kapalı; gerekçeli bir talep açar (`student_deletion_requests`), yöneticiye ekran içi bildirim düşer, yönetici onaylar ya da reddeder. Scheduling'deki `LessonChangeRequest` deseninin aynısı. Onay, silmeyi `PersonEraser`'a devreder — yöneticinin doğrudan sildiği durumla birebir aynı yol, ikinci bir silme mantığı yok. |
| J3 | Veli verisi (KVKK) | Kullanıcıya soruldu: veli telefonu şu ana kadar öğretmene kapalıydı. **Açıldı** — gerekçe pratik: veli kaydı olmadan o öğrenciye hiçbir WhatsApp bildirimi gitmiyor, öğrenciyi ekleyen kişinin velisini de girebilmesi gerekiyor. Sınır dar tutuldu: öğretmen yalnızca KENDİ öğrencisine bağlı veliyi görür/düzenler; okul geneli veli listesi (`GET /api/guardians`) hâlâ yalnızca Admin. |
| J4 | Onay kaydının kalıcılığı | Talep satırı öğrenciyle birlikte cascade ile silinir (FK). Kararın kalıcı izi `audit_log`'dadır (`student.deletion_request_approved`, talep id'si + gerekçe + etki dökümü ile) — audit hiçbir zaman temizlenmez. |
| J5 | Yetki sızıntısı koruması | Her uç için hem izin verilen hem REDDEDİLEN yol ayrı test edilir (`TeacherPortalFlowTests`). Bu testler yazılırken gerçek bir açık bulundu: öğretmen kendini herhangi bir öğrencinin öğretmeni yazarak verisine erişebiliyordu — bkz. `docs/04-permissions.md` J bölümü. |

## K — Öğretmen kendi ders programını kurar (2026-09-17)

Kullanıcı isteği: "öğretmenler kendi programları yapsınlar, ders programlarını girebilsinler."

| # | Konu | Karar |
|---|------|-------|
| K1 | Uygunluk | `TeacherAvailabilities.cs`'teki "uygunluk tanımlamak Admin işi, öğretmen yalnızca görebilir" kuralı kaldırıldı. Öğretmen kendi müsait günlerini açıp kapatır; başkasınınkine `403`. Öğretmenler ekranı Admin'e özel kaldığı için öğretmenin yüzeyi **Ayarlar > Uygun günlerim** (aynı bileşen, `components/teacher-availability-days.tsx`). |
| K2 | Ders serisi | `POST /api/lesson-series` + `PATCH` (sonlandırma) + `/generate` öğretmene açıldı, kapsam serinin `Enrollment.TeacherId`'si üzerinden. Sonlandırma da öğretmende: bu bir silme değil durum değişikliği (`Status=Ended`) ve yanlış girilen programı düzeltmenin tek yolu — yönetici beklenirse özellik kullanılamaz hale gelir. J2'deki "silme yöneticide" kuralı kişi silmeye özeldir, buraya taşınmadı. |
| K3 | Ders taşıma/iptal | Öğretmen kendi dersini takvimde sürükleyip bıraktığında **yalnız bu dersi** veya **bu ders ve tüm programı** seçer. Tek ders `PATCH /api/lessons/{id}`, tüm program mevcut seri reschedule akışıyla güncellenir. Öğretmen kendi dersini okul kaynaklı iptal edip telafi hakkı da tanımlayabilir; başka öğretmenin dersine erişim `403` döner. Telafi hakkını yeni bir derse yerleştirmek yalnızca Admin'dedir. |
| K4 | Audit | Ders serisi oluşturma/sonlandırma `audit_log`'a yazar (`lesson_series.created` / `lesson_series.ended`). Daha önce hiç yazmıyordu — aktör her zaman admin olduğu için eksiklik göze batmıyordu; artık "kim" sorusunun yanıtı gerekiyor. JSON `JsonSerializer.Serialize` ile kurulur (CLAUDE.md). |
| K5 | Çakışma koruması | Yeni bir kural eklenmedi: `EnsureWithinAvailabilityAsync` / `EnsureNoConflictAsync` / `EnsureStudentWeeklyLimitAsync` zaten öğretmen ve öğrenci çakışmasını okul genelinde kontrol ediyor. Öğretmen kendi uygunluğunu kendisi tanımladığı için bu kontrol artık kendi kendini kısıtlıyor — kasıtlı: pencereyi genişletmek de açık bir eylem ve Ayarlar'dan görünür. |
| K7 | Program öğrenci künyesinde | Kullanıcı isteği: "öğrenci altında program görünebilir olmalı, buradan ders saatini güncelleyebilmeliyim. her hafta pazartesi 18:00 piyano mesela." Kurs satırının altında `Her hafta Pazartesi 18:00 · 45 dk` özeti ve tek tıkla düzenleme var. Yeni uçlar: `GET /api/students/{id}/lesson-series` (öğretmende kendi kayıtlarıyla sınırlı) ve `POST /api/lesson-series/{id}/reschedule`. |
| K8 | Taşıma = kapat + aç | Seri YERİNDE güncellenmez: eskisi bir gün öncesinden kapanır, yenisi seçilen tarihten açılır. Tablo zaten `EffectiveFrom`/`EffectiveUntil` ile bunun için tasarlandı; yerinde güncelleme geçmişe dönük olarak "bu ders hep Pazartesi'ydi" derdi. Taşınan tarihten sonraki üretilmiş `Normal` dersler ve onlara kurulmuş bekleyen hatırlatma job'ları silinir (CLAUDE.md "ders değişince eski job iptali"). Aynı boşluk `PATCH /api/lesson-series/{id}` (seri sonlandırma) yolunda da vardı, orada da kapatıldı. |
| K9 | Aynı enstrüman için tek program | Kullanıcı kuralı: "bir öğrenci aynı enstrüman için birden fazla ders alamasın, farklı saatler de olsa." `EnsureSingleSeriesPerInstrumentAsync` — tarih aralığı çakışan ikinci bir aktif seri `409`. Kayıt (enrollment) seviyesindeki eski kısıt yalnızca aynı ÖĞRETMEN için mükerrer kaydı engelliyordu; aynı enstrümanı ikinci bir öğretmenden almak ve tek kayıt üzerine ikinci program açmak hâlâ mümkündü. Haftalık 4 ders sınırı korundu ama artık ancak 4 FARKLI branşla dolabilir. **Kural yalnızca yeni kayıtlara uygulanır — mevcut mükerrer programlar temizlenmedi, veri düzeltmesi ayrı bir karardır.** |
| K6 | Yetki sızıntısı koruması | J5'teki desen sürdürüldü: her uç için hem izin verilen hem REDDEDİLEN yol ayrı test edilir (`TeacherPortalFlowTests`: `Teacher_sets_its_own_availability_but_not_another_teachers`, `Teacher_enters_and_ends_its_own_lesson_schedule`, `Teacher_cannot_touch_another_teachers_lesson_series`). |

## L — "Genel gelişim" AI yorumu (2026-09-24)

Kullanıcı isteği: "Veli yorumunu yapıcı metne çeviren yapay zekâ kısmını kaldır. Sadece öğretmen yorumlarına göre, gelişim girdikçe yapay zekâdan destek alan bir özet yorum alalım; gpt-4o-mini yeterli."

| # | Konu | Karar |
|---|---|---|
| L1 | Yapıcı metne dönüştürme | Kaldırıldı: `POST /api/lesson-notes/{id}/parent-comment/suggest`, `IConstructiveTextRewriter` ve `/api/auth/me`'deki `aiRewriteAvailable`. Veli yorumu artık yalnızca öğretmenin elle yazdığı metin. |
| L2 | Özet üreteci | `IProgressSummaryGenerator` (`OpenAi` / `Disabled`), aynı `Ai__*` yapılandırmasıyla. Girdi yalnızca öğretmen ders notları (son 30, eskiden yeniye) ve öğrencinin adı; veli yorumu, soyad, veli/iletişim bilgisi gönderilmez. |
| L3 | Ne zaman üretilir | Tembel: `GET /api/students/{id}/progress-summary` önbellekteki yorumu kaynak notlarla karşılaştırır (not sayısı + en yeni notun zamanı; notlar değişmez). Yeni not varsa bir kez üretir ve `progress_summaries`'e yazar. Not kaydetme akışı sağlayıcıya hiç bağlı değil. Sağlayıcı hata verirse son yorum `isStale=true` ile döner. |
| L4 | Kapsam | Öğretmen gelişim ekranında yalnızca kendi notlarını görür; yorum da (öğrenci, öğretmen) başına üretilir ve önbelleğe alınır (`teacher_id NULL` = yönetici, tüm notlar). Aksi hâlde başka öğretmenin notları özet üzerinden sızardı. `UNIQUE (student_id, teacher_id) NULLS NOT DISTINCT`. |
| L5 | Görünürlük | Yalnızca okul ekibi. Ham öğretmen notundan türediği için veli portalına gitmez. Para/takvim/rıza değiştirmediği için `audit_log`'a yazılmaz. |
| L6 | Kural tabanlı eski özet | Gelişim ekranındaki kural tabanlı başlık/özet/odak metni (`buildProgressAnalysis`) kaldırıldı. "Yapay zekâ" etiketi yalnızca gerçekten modelden gelen metnin üstünde durur. Eser zorluğu önerisi kural tabanlı olarak kaldı ("öneri" etiketiyle). |

## M — Saha geri bildirimi: veli silme, tahsilat tutarı, tarife boşluğu, ders bildirimi (2026-09-24)

| # | Konu | Karar |
|---|---|---|
| M1 | Öğrenci silinince veli | Kullanıcı isteği: "öğrenci silinince veli de silinmeli, telefon numaraları çakışıyor." I1'deki "veli kalır, yalnızca sayısı bildirilir" kararı **tersine çevrildi**: başka öğrencisi olmayan veli (`_gua`) giriş kodları, WhatsApp geçmişi, sanal IBAN'ı ve telefonuna bekleyen bildirimlerle birlikte silinir. İki istisna: kardeşi kayıtlı veli (yalnızca bağ kopar) ve sanal IBAN'ına banka havalesi düşmüş veli (banka işlemi harici finansal kayıt, silinmez). Eski akıştan kalmış sahipsiz veliler için `POST /api/guardians` aynı numaralı **öğrencisiz** veliyi yeniden kullanır; numara bir kardeşin velisiyse 409 kalır ve yönetici `GET /api/guardians/by-phone` ile "Mevcut veliyi bağla" seçeneğini görür. |
| M2 | Tahsilatta elle tutar | Kullanıcı isteği: "ödenecek rakamı o anda editleyebileyim, küsüratlar değişebiliyor." `PrepayPlans.CreateRequest.AgreedTotal`: hesap yine sunucuda; istemci önce `ExpectedTotal` ile hesaplananı teyit eder, sonra toplamı değiştirir. Fark `TuitionCalculator.AdjustToAgreedTotal` ile aylara net tutarları oranında dağıtılır (kuruş artığı son aya), her ay yine tam snapshot (taban + yeniden hesaplanan yüzde + "… + Tahsilatta elle düzeltme"). Sınır: 0 < tutar ≤ tarife toplamı (aidat tarifenin üstüne çıkamaz). Audit'e `computedTotal` ve `agreedTotal` birlikte yazılır. |
| M3 | Tarife boşluğu | "2026-08: Birebir dersi için geçerli ücret tarifesi yok" - tohum tarifesi 1 Eylül 2026'da başlıyor ve daha erken başlayan tarife açılamıyordu. En eski tarifeden **önce** başlayan tarife artık kabul edilir ve en eski tarifenin bir gün öncesinde kapanır (açık uçlu tarifeye dokunulmaz, hiçbir gün iki tarifeye düşmez). Mevcut tarifelerin arasına giren tarih hâlâ reddedilir. Hata metni tarifenin nerede başladığını ve ne yapılacağını söyler (`TuitionPricer.MissingRateMessage`). |
| M4 | Ders değişikliği bildirimi | Kullanıcı isteği: "ders telafi / saat değişimi / iptal durumlarında bildirim gelsin, mail gelsin." Alıcı dersin öğretmeni + aktif yöneticiler, **işlemi yapan hariç**. Ekran içi `staff_notifications` (`LessonMoved`, `LessonCancelled`, `MakeupScheduled`) aynı transaction'da; e-posta mevcut Ops `IEmailSender`'ı (G) ile SaveChanges'ten **sonra**, en iyi çaba (hata isteği düşürmez, loglanır). Serverless yayında arka plan servisi güvenilir olmadığı için kuyruk/outbox tablosu açılmadı. `Email__Provider=Smtp` + `Email__Smtp__*` girilene kadar e-postalar yalnızca loglanır (Fake). Veliye iptal için ayrı bir WhatsApp şablonu **yok** - ayrı karar. Arayüzde üç kanal: zil üzerinde sayılı rozet (artık yönetici de görüyor - eskiden zil yalnızca öğretmendeydi), yeni bildirim düşünce köşede açılan kart (30 sn'lik yoklama, `localStorage`'daki "en son gösterilen" damgası ilk açılışta eski bildirimleri kart olarak basmaz) ve sekme başlığında "(n) Abdera". |
| M5 | Safari | `select.field` iki motorda da `appearance: none` + çizili ok (Safari yerel menüde dolguyu yok sayıp ikonu metnin üstüne bindiriyordu). Safari masaüstü `input type="month"`'u desteklemediği için dönem seçimleri `MonthInput` (ay + yıl menüsü) ile yapılır. Takvim araç çubuğu dar içerik alanında ikinci satıra sarılır. |
| M6 | Türkçe karakterler | Font dosyaları yalnızca `latin` alt kümesiyle tutuluyordu; **ğ Ğ ş Ş İ** o kümede yok, uygulamanın her yerinde bu harfler yedek fonttan (Arial/Times) çiziliyordu. Figtree ve Lora `latin` + `latin-ext` ile yeniden üretildi (google/fonts değişken TTF → `pyftsubset`). Sayfa başlıkları eğik (italik) Lora'dan düz Lora'ya alındı; logo yazısı eğik kaldı. |
| M7 | Yapay zekâ yorumu sıklığı | L3 değişti. Kullanıcı isteği: "ilk kez 4 yorum girildikten sonra takip eden her ay yapay zekâ yorumu alınsın, varsa kayıttan gösterilsin." `ProgressSummary.Decide`: kayıt yokken 4 nottan az → üretilmez (`NotEnoughNotes`); kayıtlı yorum aynı notları kapsıyorsa ya da bu ay (okulun yerel takvimi) üretildiyse gösterilir; ay değişmiş ve yeni not varsa bir kez üretilir. Yanıt `nextRefreshOn` ve toplam not sayısını da taşır. |
| M8 | Kayıtta veli çakışması | Yeni öğrenci kaydı önce öğrenciyi açıp sonra veliyi deniyordu; veli 409 verince öğrenci velisiz kalıyor, her denemede bir kopya daha oluşuyordu. Sıra ters çevrildi: veli önce çözülür. Numara bir kardeşin velisineyse telefon alanının altında "Bu veliyi kullan" çıkar; seçilirse yeni veli açılmaz ve mevcut velinin şifresi **sıfırlanmaz**. |

## Master prompt'un "Required First Response" listesiyle eşleme

| Master prompt maddesi | Karşılığı |
|---|---|
| 1. varsayımlar ve açık sorular | Bu dosya |
| 2. modül sınırları | `docs/02-modules.md` |
| 3. ERD (Mermaid) | `docs/03-erd.md` |
| 4. veritabanı tabloları ve kısıtlar | `docs/03-erd.md` |
| 5. rol/izin matrisi | `docs/04-permissions.md` |
| 6. lesson/RSVP/attendance durum modeli | `docs/05-state-models.md` |
| 7. WhatsApp hatırlatma + webhook sequence diagram | `docs/06-whatsapp.md` |
| 8. ilk REST uç nokta listesi | `docs/07-api.md` |
| 9. ilk migration sırası | `docs/08-migrations.md` |
| 10. test stratejisi | `docs/09-testing.md` |
| 11. uygulama fazları | `README.md` (durum tablosu) + `docs/00-master-prompt.md` (Implementation Order) |
| 12. riskler ve onay gerektiren kararlar | Bu dosya (A/B/D bölümleri) |
