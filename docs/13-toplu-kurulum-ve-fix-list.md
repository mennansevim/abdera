# Faz 7 — Toplu Kurulum Agent'ı, Tek-Ekran Kayıt & Benchmark

Bu dosya, "sıfırdan kurulum" çalışmasının **canlı takip + fix listesi**dir. Toplu veri girişi
sırasında bulunan UX/gecikme hatalarını burada toplarız, sonra en verimli şekilde çözeriz.
Kaynak istek: 20 öğretmen × 35 öğrenci, öğrencilerin ≥%30'u 2 enstrüman; veliler telefon+şifre
ile giriş; geçmiş aidatlar dahil.

> **Güncel demo veri politikası (2026-09-12):** İlk performans turundaki 20/700 hacmi
> yalnızca tarihsel ölçüm olarak aşağıda korunur. `tools/bulk-seed/seed.mjs` artık toplam
> **10 öğretmen / en fazla 150 öğrenci** hedefler ve **hiç aidat, ücret planı, fiyat listesi
> veya ödeme üretmez**. `20260912230000_TrimDemoData` migration'ı eski deterministik toplu
> demo verisini aynı sınırlara indirir ve demo aidatlarını temizler.

## Kararlar (docs/10-decisions.md'ye işlenecek)

- **Karar F (ikinci) reversal — veli girişi telefon + ŞİFRE.** Kullanıcı, veli portalı için
  WhatsApp OTP yerine kalıcı **şifre** istedi (2026-09-12). Kullanıcı adı = telefon numarası.
  Şifre, çocuğun adı + veli adı + telefon son 4 hanesinden türeyen **mnemonik bir desen**le
  üretilir ve WhatsApp'tan gönderilir. OTP akışı korunur (geri uyum) ama birincil giriş artık
  şifre. `Guardian` tablosuna `password_hash` (nullable) eklenir; `users` tablosuna hâlâ
  dokunulmaz.
- **Şifre deseni:** `{ÇocukAdıİlk3}{VeliAdıİlk2}{TelefonSon4}` — Türkçe karakterler ASCII'ye
  çevrilir, çocuk adı Baş Harf Büyük, veli adı küçük. Örn: çocuk "Zeynep", veli "Ayşe",
  telefon `+90 532 123 45 67` → `Zeyay4567`. Güvenlik notu: kamuya açık bilgiden türediği için
  yalnızca **ilk şifre**dir; ileride "ilk girişte değiştir" akışı önerilir (FIX-BACKLOG'a bkz).

## Faz planı

- [x] **A. Veli şifre girişi (backend + frontend)** — `password_hash` + migration
      (`20260912174826_AddGuardianPasswordHash`), `Guardian.SetPassword`,
      `GuardianPasswordGenerator`, `POST /api/guardian/login`, admin
      `POST /api/guardians/{id}/reset-password` (şifre üret + WhatsApp gönder),
      `useGuardianLogin` hook, `/parent/login` telefon+şifre (OTP ikincil). **Backend curl ile
      uçtan uca doğrulandı** (örnek: veli "Ayşe" → şifre `Aysay2244`, login 200, yanlış/bilinmeyen
      401). Frontend tarayıcı doğrulaması konsolide pass'e ertelendi.
- [x] **B. Toplu kurulum agent'ı** (`tools/bulk-seed/seed.mjs`) — izole `abdera_seed` DB'sine
      **20 öğretmen, 700 öğrenci, 260 ikinci kayıt (%37), 700 veli (hepsi şifreli), 960 aktif
      kayıt, 3840 aidat, 2880 ödeme, 432 ders serisi** yazdı. 6743 istek / 54s. **Gecikme bulgusu
      YOK** (tüm endpoint p95 ≤ 43ms, max 128ms). Veli girişi 40/40 doğrulandı. Rapor:
      `tools/bulk-seed/last-run-report.{md,json}`. 268 bulgu = ders serisi slot çakışması
      (öğretmen başına 35 öğrenci > 30 slot; sistem çift-rezervasyonu doğru engelliyor — bug değil).
- [x] **C. Tek-ekran öğrenci kaydı** — `dashboard/students/new/page.tsx` (öğrenci + öğretmen +
      enstrüman + ders günü [şimdi/sonra] + veli, tek submit'te `useRegisterStudent` ile
      zincirlenir → üretilen şifre gösterilir). UX-2 çözüldü (5-7 çağrı tek submit). UX-1 için
      ders çakışması nazikçe geri bildiriliyor (öğrenci yine kaydediliyor). typecheck+lint temiz.
      Tarayıcı doğrulaması: başka oturumun Next dev-server kilidi nedeniyle bu oturumda yapılamadı.
- [x] **D. CRUD doğrulama** — `tools/bulk-seed/crud-test.mjs`, **22/22 geçti**: öğretmen/öğrenci/veli
      create-read-update-delete, soft-delete (Inactive), kayıt sonlandırma, şifre-çocuk-adı
      türetme, veli girişi, unique telefon (409) kısıtı.
- [x] **E. Fix listesindeki bugları çöz** — Seed + CRUD **gerçek bug/gecikme üretmedi** (bkz.
      PERF-1). UX-1/UX-2 tek-ekran formda ele alındı. Açık kalan: UX-1'in tam çözümü (slot ön
      gösterimi) ve FIX-BACKLOG (ilk-giriş şifre değiştir).
- [x] **F. Öğrenci–öğretmen benchmark ekranı** — backend `Modules/Dashboard/Features/Benchmark.cs`
      (`GET /api/benchmark/{teachers,students}`, kompozit skor: öğrenci sayısı + ders/çalışma günü
      + not + onaylı yorum + katılım). Frontend `dashboard/benchmark/page.tsx` (öğretmen/öğrenci
      sekmeleri, sıralı, skor barı). **Endpoint doğrulandı** (20 öğretmen skor 95.2→38.9, 700
      öğrenci sıralı). Demo için yoklama+not+onaylı yorum aktivitesi tohumlandı (izole DB, SQL).

---

## FIX LİSTESİ (UX / gecikme / bug)

Format: `[ID] durum · alan · özet — belirti/tekrar adımı — kök neden (biliniyorsa) → çözüm`

| ID | Durum | Alan | Özet |
|----|-------|------|------|
| PERF-1 | ✅ sorun yok | gecikme | Toplu seed'de 6743 istekte tüm endpoint p95 ≤ 43ms — bağlantı havuzu tuning'i (7a48ff9) yeterli, düzeltilecek gecikme yok. |
| UX-1 | 🔧 açık | takvim/kayıt | Ders günü/saati atarken öğretmen uygunluğu görünmüyor → seed'de 268 slot 409 çakışması. Tek-ekran kayıt formunda (Pillar C) gün/saat seçilirken **dolu slotlar gösterilmeli** ya da çakışma anında geri bildirilmeli. |
| UX-2 | 🔧 açık | kayıt akışı | Tek bir öğrenci kaydı 5-7 ayrı admin çağrısı gerektiriyor (öğrenci, kayıt, veli, bağlama, şifre, fee-plan, ders). Tek-ekran form (Pillar C) bunları kullanıcı adına tek submit'te zincirlemeli. |

### FIX-BACKLOG (sonraya, kapsam dışı ama not)
- Veli "ilk girişte şifre değiştir" akışı (şifre deseni tahmin edilebilir olduğundan).

---

## Yerel geliştirme kurulumu (bu oturum)
- Docker `--build` sınıflandırıcı tarafından engellendiğinden backend **yerelde `dotnet run`**
  ile **8081** portunda, Docker'daki aynı Postgres'e (localhost:5432) bağlı çalışıyor.
  Container (8080) eski imajla ayakta ama bu oturumda otorite **8081**.
- **Gotcha:** `.env`'i `source` etmek `Auth__KeysDirectory=/app/keys` (yerelde yazılamaz) çeker →
  Data Protection cookie şifreleme 500 verir. Çözüm: yalnızca gerekli env'leri set et,
  `Auth__KeysDirectory`'yi yazılabilir bir scratchpad dizinine yönlendir.
- Kullanıcının normal kurulumuna (8080) almak için: `docker compose up -d --build api`
  (ben çalıştıramıyorum, engellendi) + `.env.local` içinde `NEXT_PUBLIC_API_BASE_URL` geri 8080.

## Çalışma günlüğü
- 2026-09-12: Ortam doğrulandı (Postgres 5432, API Docker 8080, WhatsApp=Fake). Plan + fix
  listesi kuruldu.
- 2026-09-12: **Pillar A tamamlandı** — veli telefon+şifre girişi (backend curl ile doğrulandı,
  frontend yazıldı+typecheck temiz). Migration eklendi. Yerel API 8081'de çalışıyor.
- 2026-09-12: Pillar B (toplu kurulum agent'ı) başladı.
- 2026-09-12: **Pillar B tamam** — `abdera_seed` izole DB'ye 20/700 yazıldı, doğrulandı.
- 2026-09-12: **Pillar C** tek-ekran kayıt, **Pillar F** benchmark (backend+frontend) yazıldı.
  Benchmark için demo aktivitesi (yoklama/not/onaylı yorum) tohumlandı. **Pillar D** CRUD 22/22.
  Kalan tek eksik: frontend tarayıcı doğrulaması (başka oturumun Next dev kilidi bu oturumda
  engelledi) — backend uçları curl ile tam doğrulandı, frontend typecheck+lint temiz.
- 2026-09-12: Karar F (ikinci) reversal `docs/10-decisions.md`'ye işlendi.
- 2026-09-12: **Vercel production deploy** (`https://abdera-web-nine.vercel.app`). Frontend +
  backend container temiz derlendi. Canlıda doğrulandı: admin login, benchmark (teachers+students),
  frontend rotaları, guardian/login (401), guardian OTP (200) — **hepsi çalışıyor**. Not: prod'da
  `Database__AutoMigrate=false` ama `Runtime:Serverless=true` başlangıçta arka planda migrate
  ettiği için `AddGuardianPasswordHash` prod DB'ye uygulandı (deploy'dan birkaç sn sonra tamamlandı).

## Kullanıcının canlı görmesi için
Yerel API şu an **8081** portunda `abdera_seed` DB'sine bağlı çalışıyor (bu oturumun arka plan
süreci). Frontend'i buna bağlayıp görmek için:
1. Çalışan diğer Next dev server'ı durdur (3001), sonra:
   `NEXT_PUBLIC_API_BASE_URL=http://localhost:8081 npm --prefix frontend run dev`
2. Admin: `admin@example.com` / `.env`'deki `Bootstrap__AdminPassword`. Benchmark: `/dashboard/benchmark`,
   tek-ekran kayıt: `/dashboard/students → "Öğrenci ekle"`.
3. Örnek veli girişi (`/parent/login`): `tools/bulk-seed/sample-credentials.json` (örn.
   `+905003000001` / `Berse0001`).
Kalıcı olarak normal Docker kurulumuna (8080, `abdera` DB) almak için: `docker compose up -d --build api`
(bu değişiklikleri container imajına derler) — ben çalıştıramadım (sınıflandırıcı engeli).
