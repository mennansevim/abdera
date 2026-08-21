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
| — | İptal ile erteleme (reschedule) ayrımı | `LessonChangeRequest.proposed_start_at/end_at` ERD'de NOT NULL — yani bu talep her zaman **yeni bir saat önerir**, düz iptal değildir. Düz iptal ayrı, basit bir uç nokta: `POST /api/lessons/{id}/cancel` (yalnızca Admin). Telafi kredisi (A2) yalnızca **iptalde** doğar, ertelemede doğmaz — ders zaten farklı bir saatte gerçekleşecek, telafiye gerek yok. |
| — | Okul kaynaklı iptal her zaman kredi doğurur | A2'nin 24 saat kuralı yalnızca veli kaynaklı iptale uygulanır. Okul kaynaklı iptalde (öğretmen hastalığı, tatil) bildirim süresine bakılmaksızın her zaman `MakeupCredit` doğar — velinin hatası olmayan bir durumda ücreti kaybetmemesi gerekir. `Modules/Scheduling/Features/CancelLesson.cs`. |
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
| C3 | `ProgressSummaryGenerator` AI arayüzü Phase 0'da açılması | Sıfır implementasyonlu arayüz spekülatif soyutlama — Phase 6'ya ertelendi |
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
