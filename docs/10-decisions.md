# Kararlar ve Açık Sorular

Master prompt (`docs/00-master-prompt.md`) gözden geçirilirken bulunan boşluklar, kesilen fazlalıklar ve onaylanan kararlar. Yeni bir kural eklerken önce burayı kontrol et — sessizce ikinci bir karar üretme.

## Onaylanmış kararlar

| # | Konu | Karar |
|---|---|---|
| — | Repo yapısı | Monorepo — backend, frontend, infra, docs tek repoda |
| — | Stack | .NET 10 (LTS) + ASP.NET Core, Java/Spring Boot yerine (gerekçe: bellek/soğuk başlangıç, aşağıda) |
| — | 4. enstrüman | Keman (`VIOLIN`) eklendi, kendi yetenek tanımlarıyla (`INTONATION`, `BOW_CONTROL`, `LEFT_HAND_POSITION`) |
| A2 | Telafi hakkı doğuşu | Dersten **≥24 saat önce** iptal → 1 `MakeupCredit`. Habersiz gelmeme (no-show) kredi doğurmaz, ücret yine tahakkuk eder. |

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
| B1 | `PACKAGE` tipi paket hangi ders durumunda tükeniyor? | `COMPLETED` + habersiz `ABSENT` düşer; `CANCELLED`/okul kaynaklı iptal düşmez |
| B2 | Devamsızlık aidatı etkiliyor mu? | `MONTHLY`'de hayır; telafi hakkı A2 kuralına göre ayrı doğar |
| D4 | Deploy hedefi: Cloudflare + managed Postgres mi, yoksa Raspberry Pi mi (diğer projelerinle tutarlı)? | Phase 7'den önce netleşmeli — yedekleme ve kaynak planlamasını etkiliyor |

**Not:** B3 ve B4 aşağıda kendi öneri notlarıyla birlikte kabul edilmiş kararlar olarak işlendi (net alternatifleri yoktu, tersi mantıksız olurdu); B1/B2/D4 hâlâ açık — Phase 2/4/7'den önce onay gerekiyor.

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
