# Denetim Düzeltme Görevi — Abdera

Bu, `docs/11-progress-log.md`'deki Faz 6 sonrası yapılan kod/mimari denetiminin ([Abdera Denetim Defteri](https://claude.ai/code/artifact/4eb5fcd0-2dd5-42d9-9384-39140398cbf1)) çıktısıdır. Aşağıdaki 15 bulguyu, belirtilen sırayla düzelt. Her adım için: önce `CLAUDE.md`'yi oku (mimari kurallar, JSON/kültür kuralları, test stratejisi), sonra düzelt, sonra ilgili testleri (birim + varsa Testcontainers) yaz/güncelle, sonra `dotnet test` ile tüm suite'i (şu an 141 test) yeşil tut, en az bir kez `docker compose up` ile canlı doğrula, sonra commit et. Push için her seferinde kullanıcıdan onay iste (repo public).

Proje kökü: `/Users/sevimm/Documents/Projects/abdera-web`. Backend: `backend/src/Abdera.Api`. Frontend: `frontend/src`.

---

## 1. (SEC-1, Kritik) Webhook imza doğrulaması boş secret'ta fail-open

**Dosya:** `Modules/Messaging/Domain/WebhookSignatureVerifier.cs`

`IsValid(rawBody, signatureHeader, appSecret)` metodu `appSecret` boş/null olduğunda ayrı bir kontrol yapmıyor — boş anahtarla HMAC hesaplayıp karşılaştırıyor, bu da deterministik ve tahmin edilebilir bir sonuç üretir. `WhatsApp__AppSecret` canlı ortamda tanımsız kalırsa imza doğrulaması sessizce herkese açık hale gelir; saldırgan sahte RSVP veya opt-out isteği gönderebilir.

**Referans doğru desen — zaten var:** `Modules/Banking/Features/Webhooks.cs` içindeki `VerifySharedSecret` metodu şunu yapıyor:
```csharp
if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;
```

**Yapılacak:**
1. `WebhookSignatureVerifier.IsValid`'e aynı guard'ı ekle: `appSecret` boşsa doğrudan `false` dön.
2. `Modules/Messaging/Tests` altındaki (veya nerede olursa) ilgili webhook testinin şu an **boş bir secret ile imzalayıp geçtiğini** kontrol et — muhtemelen `MessagingFlowTests.cs` içinde `Webhook_does_not_reprocess_a_duplicate_provider_event_id` ve `Webhook_rejects_request_with_invalid_signature` testleri. Bu testleri gerçek bir test secret'ı kullanacak şekilde güncelle (test factory config'ine `WhatsApp:AppSecret` ekle) ve testin `AppSecret` boşken de reddettiğini doğrulayan yeni bir test ekle.
3. `Program.cs`'e (veya `DatabaseMigrator`/başlangıç kontrolü neredeyse oraya) `Production` ortamında `WhatsApp:AppSecret` ve `WhatsApp:PayloadSigningKey` boşsa uygulamanın başlamayı reddetmesini sağlayan bir kontrol ekle (`Development`'ta zorunlu olmasın, `Fake` provider zaten bunları kullanmıyor).

## 2. (SEC-2, Yüksek) RSVP buton imzası aynı boş-anahtar zafiyetini taşıyor

**Dosya:** `Modules/Messaging/Domain/RsvpButtonPayload.cs`

`Sign` ve `TryVerify` metodlarında `signingKey` için hiç `IsNullOrEmpty` kontrolü yok. `WhatsApp__PayloadSigningKey` boşsa buton payload imzası taklit edilebilir — bu imzanın var oluş amacı tam olarak bunu engellemekti (bir velinin başka bir dersin RSVP'sini imzasız/tahmin edilebilir id ile değiştirmesini önlemek).

**Yapılacak:** `TryVerify`'a `signingKey` boşsa `false` dönen bir guard ekle. Madde 1'deki başlangıç kontrolüne bu anahtarı da dahil et. `Unit/MessagingDomainTests.cs`'e boş anahtarla `TryVerify`'ın reddettiğini doğrulayan bir test ekle.

## 3. (SEC-3, Yüksek) Giriş ekranında rate limiting / kaba kuvvet koruması yok

**Dosya:** `Program.cs`, `Modules/Auth/Features/Login.cs`

69 endpoint'in hiçbirinde `AddRateLimiter` yok. `User` entity'sinde `AccessFailedCount`/`LockoutEnd` gibi bir alan da yok. `/api/auth/login` sınırsız denenebilir.

**Yapılacak:**
1. ASP.NET Core'un yerleşik `Microsoft.AspNetCore.RateLimiting` middleware'ini kullan (zaten geçişli paket olarak mevcut, ek NuGet paketi gerekmez — CLAUDE.md'nin "gereksiz bağımlılık ekleme" kuralına uygun).
2. `/api/auth/login` için IP başına sabit pencere politikası ekle (öneri: 5 istek / 15 dakika).
3. `/api/webhooks/whatsapp` ve `/api/webhooks/bank` için ayrı, daha gevşek bir politika ekle (gerçek sağlayıcı trafiğini engellememeli, ama sınırsız da bırakma — öneri: 60 istek/dakika).
4. Rate limit aşımının 429 döndüğünü doğrulayan bir entegrasyon testi ekle (`AuthFlowTests.cs`'e).

## 4. (SEC-4, Orta) Login.cs'teki yorum kodun yapmadığı bir korumayı iddia ediyor

**Dosya:** `Modules/Auth/Features/Login.cs`

```csharp
if (user is null)
{
    // Var olmayan e-posta ile yanlış şifre arasında zamanlama farkı bırakmamak için
    // aynı genel mesaj döner - kullanıcı numaralandırmasına karşı.
    return Results.Problem(statusCode: 401, ...);
}
var verifyResult = passwordHasher.VerifyHashedPassword(...);
```

Kullanıcı bulunamadığında hash doğrulama hiç çalışmıyor — yanıt mesajı aynı ama süre farklı (kayıtlı e-posta ~50-100ms, kayıtsız ~1ms). Bu, e-posta numaralandırmasına yeterli bir zamanlama kanalı.

**Yapılacak:** Kullanıcı `null` ise sabit, önceden hesaplanmış bir "dummy" hash'e karşı `passwordHasher.VerifyHashedPassword` çağır (sonucu kullanma, sadece süreyi eşitle). Ardından yorumu koddaki gerçek davranışla eşleştir.

## 5. (UX-1, Yüksek) Mobil desteği fiilen yok

**Dosyalar:** `frontend/src/app/dashboard/app-header.tsx`, `frontend/src/app/dashboard/billing/*.tsx`, genel Tailwind kullanımı

Kanıtlar:
- Tüm frontend'de yalnızca 4 responsive breakpoint kullanımı var (`sm:` × 3, `lg:` × 1), hepsi `calendar/page.tsx` içinde.
- `app-header.tsx`'teki 8 linkli nav `flex-wrap` ile sarılıyor, hamburger/drawer yok → dar ekranda 3-4 satıra yayılıyor.
- 86 yerde `py-0.5`/`py-1` kullanımı (~24-28px yükseklik), 44px asgari dokunma hedefinin altında.
- `billing/price-lists-section.tsx` ve `billing/student-billing-section.tsx`'teki tablolar `overflow-x-auto` sarmalı değil (banking ve notifications sayfalarındaki tablolar sarmalı — tutarsız).

**Yapılacak:**
1. `app-header.tsx`'i mobilde (`< 768px`) hamburger + slide-in drawer'a çevir; masaüstünde mevcut yatay nav kalsın.
2. `billing/price-lists-section.tsx` ve `billing/student-billing-section.tsx`'teki `<table>` sarmalayıcılarını `overflow-x-auto rounded-lg border ...` ile sar (notifications/banking sayfalarındaki desenle birebir aynı).
3. Birincil aksiyon butonlarını (ödeme al, onayla/reddet, yeniden dene) `min-h-11` (44px) yap.
4. Bu iş için referans: bu oturumda üretilen 4 tasarım konseptindeki (Sıcak Stüdyo/Gece Resitali/Renkli Sınıf/Kağıt ve Mürekkep) Öğretmen ve Veli ekranları zaten doğru mobil düzeni (bottom tab nav, 44px+ dokunma hedefleri, agenda-list görünüm) gösteriyor — birebir kopyalama değil ama düzen mantığı oradan alınabilir.

## 6. (UX-2, Orta) Geist fontu indiriliyor ama uygulama Arial render ediyor

**Dosya:** `frontend/src/app/globals.css`

`layout.tsx` Geist Sans/Mono'yu Google Fonts'tan yükleyip `--font-geist-sans` CSS değişkeni tanımlıyor, ama `globals.css`:
```css
body { font-family: Arial, Helvetica, sans-serif; }
```
bu değişkeni hiç kullanmıyor. Tarayıcıda `getComputedStyle(document.body).fontFamily` → `"Arial, Helvetica, sans-serif"` olarak doğrulandı.

**Yapılacak:** `globals.css`'teki `body { font-family: ... }` satırını `var(--font-geist-sans)` (fallback zinciriyle) yap. Değişikliği `docker compose up` sonrası tarayıcıda `getComputedStyle` ile tekrar doğrula.

## 7. (UX-4, Düşük) Kullanılmayan karanlık tema CSS'i

**Dosya:** `frontend/src/app/globals.css`, `frontend/src/app/layout.tsx`

`globals.css`'teki `@media (prefers-color-scheme: dark)` bloğu, `layout.tsx`'in gövdeye verdiği `bg-neutral-50 text-neutral-900` Tailwind sınıfları tarafından eziliyor — karanlık tema ne çalışıyor ne tasarlandı, yanıltıcı ölü kod.

**Yapılacak:** Karanlık tema tasarlanmayacaksa bloğu tamamen sil. Tasarlanacaksa ayrı bir karar/görev olarak ele al, bu görevin kapsamında değil.

## 8. (ARC-1, Yüksek) Optimistic concurrency hiç uygulanmadı

**Dosyalar:** `Shared/AbderaDbContext.cs`, `Modules/Billing/Persistence/ReceivableConfiguration.cs`, `Modules/Banking/Persistence/BankIncomingTransactionConfiguration.cs`

CLAUDE.md: "Eşzamanlı düzenleme riski olan tablolarda optimistic concurrency (`xmin` veya `rowversion` kolonu)." Kodda hiç örneği yok (`IsRowVersion`/`UseXminAsConcurrencyToken`/`IsConcurrencyToken` — 0 sonuç). İki admin aynı `Receivable`'a aynı anda ödeme işlerse ikinci yazma birinciyi sessizce ezer.

**Yapılacak:**
1. En azından `Receivable` ve `BankIncomingTransaction` için Npgsql'in `UseXminAsConcurrencyToken()`'ını EF konfigürasyonlarına ekle (kolon eklemeden çalışır — Postgres'in sistem kolonu `xmin`'i kullanır, migration'ı ucuzdur ama yine de bir migration gerekir çünkü model snapshot değişir).
2. `GlobalExceptionHandler.cs`'e `DbUpdateConcurrencyException`'ı yakalayıp 409 ProblemDetails'e çeviren bir dal ekle.
3. Testcontainers ile: iki eşzamanlı `SaveChangesAsync` çağrısının ikincisinin `DbUpdateConcurrencyException` fırlattığını doğrulayan bir entegrasyon testi ekle (`PricingAndBillingFlowTests.cs` veya yeni bir dosya).

## 9. (ARC-2, Orta) Tanımlı ama üretilmeyen altı durum sessizce "FAILED"a düşüyor

**Dosya:** `Modules/Messaging/Features/NotificationMessageBuilder.cs`

`NotificationJobType.Birthday`, `PackageEnding` ve `LessonChangeRequestStatus`'ün dört veli-etkileşimi durumu (`AlternativeProposed`, `ParentConfirmationPending`, `ParentAccepted`, `ParentRejected`) hiçbir use-case tarafından üretilmiyor. `BuildAsync`'in `default` dalı `null` dönüyor, bu da dispatcher'ı "ilgili kayıt bulunamıyor" gibi yanıltıcı bir hatayla `FAILED`'a düşürüyor.

**Yapılacak:** `BuildAsync`'in `default` dalında, tip `Birthday`/`PackageEnding` ise `NotificationMessageBuilder`'dan özel bir exception fırlat (örn. `NotImplementedNotificationTypeException`) ve dispatcher'da bunu `LastError = "Bu bildirim tipi henüz uygulanmadı (Faz 7)."` gibi açık bir mesajla `FAILED`'a düşür — en azından panelde neden başarısız olduğu okunur olsun. `LessonChangeRequestStatus`'ün kullanılmayan dört durumu için ayrı bir kod değişikliği gerekmiyor (zaten hiçbir yerde üretilmiyorlar), yalnızca `docs/05-state-models.md`'deki "bilinçli eksik" notunun hâlâ doğru olduğunu teyit et.

## 10. (ARC-3, Orta) Sayfalama yok — iki listede sessiz 200 kayıt kesintisi

**Dosyalar:** `Modules/Messaging/Features/Notifications.cs:29`, `Modules/Banking/Features/BankTransactions.cs:35`

İkisi de `.Take(200)` ile kesiliyor, kullanıcıya "daha fazlası var" sinyali yok, toplam sayı dönmüyor.

**Yapılacak:**
1. Her iki endpoint'e `?page=1&pageSize=50` gibi parametreler ekle, yanıta `{ items, totalCount, page, pageSize }` şeklinde bir zarf ekle (mevcut response şekli bozulmasın diye frontend'i de güncellemen gerekir — `frontend/src/lib/messaging.ts` ve `frontend/src/lib/banking.ts`).
2. `/api/calendar` sorgusuna zorunlu bir üst sınır ekle (öneri: `to - from` en fazla 3 ay, aşarsa 400).

## 11. (ARC-5, Düşük) Döngü içinde veritabanı sorgusu (N+1)

**Dosyalar:** `Modules/Pricing/Features/BulkUpdate.cs:69`, `Modules/Pricing/Features/PriceLists.cs:44`

```csharp
// BulkUpdate.cs — kalem başına sorgu
foreach (...) { await db.FeePlans.CountAsync(f => f.PriceListItemId == x.Item.Id && f.ActiveUntil == null); }

// PriceLists.cs — kalem başına sorgu
foreach (...) { await db.Instruments.AnyAsync(i => i.Id == itemRequest.InstrumentId); }
```

**Yapılacak:** İlkini döngü öncesi tek bir `GroupBy` sorgusuyla topla (`Dictionary<Guid, int>`). İkincisini döngü öncesi tek `db.Instruments.Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToListAsync()` ile çöz, sonra `HashSet.Contains` kullan.

## 12. (ARC-4, Orta) FluentValidation bağımlılık olarak duruyor, hiç kullanılmıyor

**Dosya:** `Abdera.Api.csproj`

Paket referansı var, kodda `AbstractValidator` kullanan tek bir satır yok. Doğrulama her yerde elle `throw new ValidationFailedException(...)`.

**Yapılacak — bir karar ver ve uygula:**
- **Seçenek A (öneri, bu ölçek için):** Paketi `csproj`'dan kaldır, mevcut elle doğrulama desenini koru. `docs/10-decisions.md`'ye "FluentValidation MVP ölçeğinde gereksiz görülüp kaldırıldı" notu düş.
- **Seçenek B:** En azından `Login.Request`, `Payments.CreateRequest`, `PriceLists.CreateRequest` için gerçek `AbstractValidator<T>` sınıfları yaz ve `builder.Services.AddValidatorsFromAssembly(...)` ile DI'a bağla.

Hangisini seçersen seç, kod tabanında **tek bir tutarlı desen** kalmalı.

## 13. (ARC-6 / UX-3, Düşük+Orta) Dashboard modülü ve Veli paneli — kapsam kararı gerekiyor

Bunlar kod düzeltmesi değil, **karar** gerektiriyor:
- `docs/02-modules.md` ve `docs/07-api.md` bir "Dashboard" modülü ve `GET /api/dashboard/today` tanımlıyor, hiç yazılmadı — frontend ana sayfası hâlâ yer tutucu.
- Sistem veli verisini/RSVP'sini/aidat durumunu tutuyor ama velinin bakabileceği hiçbir web ekranı yok (yalnızca WhatsApp).

**Yapılacak:** Bu görevi yürüten oturum, düzeltmeye geçmeden önce kullanıcıya şunu sor: "(a) Dashboard modülünü şimdi mi yapalım yoksa Faz 7'ye mi bırakalım, (b) Veli web paneli ayrı bir faz olarak planlansın mı yoksa WhatsApp tek kanal olarak mı kalsın?" Cevaba göre `docs/10-decisions.md`'ye yeni bir karar maddesi ekle. Bu maddeler için kod yazmadan önce onay bekle.

## 14. (OPS-1, Yüksek) CI altı fazdır testleri çalıştırmıyor

**Dosya:** `.github/workflows/ci.yml`

Şu an yalnızca `.env` commit kontrolü çalışıyor; backend build+test bloğu Faz 0'dan beri yorum satırında.

**Yapılacak:**
1. Yorumdaki `backend-build-test` işini aç, `dotnet restore`/`build`/`test` adımlarını ekle.
2. Testcontainers CI'da Docker-in-Docker gerektirdiği için: GitHub Actions'ın `ubuntu-latest` runner'ı Docker'ı zaten destekliyor (ek konfigürasyon genelde gerekmez, ama `services: postgres` bloğunu Testcontainers ile çakışmayacak şekilde kaldırıp yalnızca Testcontainers'ın kendi container'ını başlatmasına izin ver — ya da tam tersi, birim testleri ayrı bir job'da, Testcontainers gerektiren entegrasyon testlerini ayrı bir job'da çalıştır).
3. `main`'e push/PR'da bu job'ın zorunlu (required check) olmasını `docs/09-testing.md`'ye not düş.

---

## Doğrulama (her madde için tekrarlanacak)

1. `dotnet build` hatasız.
2. `dotnet test` — tüm suite yeşil (şu an 141, yeni testler eklendikçe artacak).
3. `docker compose up -d --build` ile canlı doğrulama — özellikle SEC-1/SEC-2/SEC-3/UX-1/UX-2 için tarayıcıdan/curl'den gerçek davranışı kontrol et, yalnızca kod okuyarak "düzeldi" deme.
4. İlgili `docs/*.md` dosyalarını güncelle (CLAUDE.md'de yeni bir kural doğuyorsa oraya da yaz).
5. Türkçe commit mesajı (temp dosya + `git commit -F`), push öncesi kullanıcı onayı.
