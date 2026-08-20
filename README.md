# Abdera — Müzik Okulu Yönetim Sistemi

Küçük bir müzik okulunun bugün WhatsApp + Excel + kağıt ile yürüttüğü işi tek yerde toplayan web uygulaması.

**Kapsam:** ders takvimi ve öğretmen programları · veli RSVP'si · devamsızlık ve telafi dersleri · aidat/tahsilat takibi · birim fiyat yönetimi · ders notları ve öğrenci gelişimi · WhatsApp üzerinden otomatik ve etkileşimli bilgilendirme.

**Kullanıcılar ve arayüzleri**

| Kim | Nereden |
|---|---|
| Yönetici | Web uygulaması (masaüstü + mobil) |
| Öğretmen | Mobil öncelikli web / PWA |
| Veli | WhatsApp — ayrı uygulama indirmesi yok |

Enstrümanlar: **piyano · gitar · keman · bateri**

---

## Durum

**Phase 5 — WhatsApp.** Ders serisi/erteleme/telafi/aidat akışları artık `INotificationScheduler` portu üzerinden otomatik bildirim job'ı açıyor (`notification_jobs`, `UNIQUE(type,reference_type,reference_id)` ile idempotent). `NotificationDispatcher` (`BackgroundService` + `FOR UPDATE SKIP LOCKED`) her dakika bekleyen job'ları alıp `IWhatsAppClient` üzerinden gönderiyor — sessiz saat dışına düşen cron kaynaklı bildirimler (aidat/doğum günü/paket bitişi) bir sonraki pencereye ötelenir, ders hatırlatması bu kurala tabi değil. Gelen webhook imzası (`X-Hub-Signature-256`) doğrulanıyor, RSVP butonları HMAC ile imzalı opak referans taşıyor, deterministik intent'ler (`ders`/`aidat`/`telafi`/`okula yaz`) ve opt-out (`dur`/`iptal`/`stop`) çalışıyor. Meta hesabı olmadan uçtan uca test için dev-only `POST /api/dev/whatsapp/simulate-text` ve `simulate-rsvp` var (`WhatsApp__Provider=Fake` varsayılan - D2). Hepsi `docker compose up` ile uçtan uca doğrulandı; ayrıntılı ilerleme günlüğü `docs/11-progress-log.md`.

| Faz | Kapsam | Durum |
|---|---|---|
| 0 | Tasarım paketi, alan modeli, API yüzeyi, kararlar | ✅ |
| 1 | İskelet: Compose + Postgres + API + web + auth | ✅ |
| 2 | Kişiler ve takvim | ✅ |
| 3 | Devam, RSVP, ders değişikliği | ✅ |
| 4 | Fiyatlandırma ve aidat | ✅ |
| 5 | WhatsApp | ✅ Bu commit |
| 6 | Gelişim takibi ve hatırlatmalar | ⬜ |
| 7 | Sağlamlaştırma ve devreye alma | ⬜ |

---

## Teknoloji

| Katman | Seçim |
|---|---|
| Backend | .NET 10 (LTS) · ASP.NET Core Minimal API |
| Veri | PostgreSQL 16 · EF Core 10 + Npgsql |
| Doğrulama | FluentValidation |
| Kimlik | `PasswordHasher<T>` + httpOnly cookie oturumu (tam ASP.NET Core Identity değil — kendi minimal `users` tablomuz var) |
| Zamanlayıcı | `BackgroundService` + `PeriodicTimer` (Postgres `FOR UPDATE SKIP LOCKED`) — `NotificationDispatcher`, Phase 5 |
| Log / sağlık | Serilog · `HealthChecks` |
| Frontend | Next.js 16 (App Router) · TypeScript · Tailwind · TanStack Query (shadcn/ui Phase 2'de UI büyüyünce eklenecek) |
| Test | xUnit · `WebApplicationFactory` · Testcontainers (yalnızca gerçek Postgres gerekenlerde) |
| Mesajlaşma | Meta WhatsApp Business Cloud API |
| Çalıştırma | Docker + Docker Compose |

Neden .NET (master prompt Java 21 öneriyor): bu ölçekte iki yığın da fazlasıyla yeterli; ucuz bir sunucuda fark yaratan bellek (~80 MB / ~300 MB) ve soğuk başlangıç (<1 sn / 5–10 sn) tarafında .NET önde. Gerekçe: [`docs/10-decisions.md`](docs/10-decisions.md).

**Kasıtlı olarak kullanılmayanlar:** mikroservis, Kafka/RabbitMQ, Redis, Kubernetes, Hangfire/Quartz, Keycloak, workflow motoru. Gerekçe: bu ölçekte hiçbirinin çözdüğü somut bir problem yok.

---

## Dokümantasyon

| Dosya | İçerik |
|---|---|
| [`docs/00-master-prompt.md`](docs/00-master-prompt.md) | Kaynak ürün/mimari brief'i (değiştirilmez referans) |
| [`docs/01-glossary.md`](docs/01-glossary.md) | Türkçe ↔ İngilizce terim sözlüğü |
| [`docs/02-modules.md`](docs/02-modules.md) | Modül haritası ve bağımlılık kuralları |
| [`docs/03-erd.md`](docs/03-erd.md) | Varlık ilişki diyagramı ve tablo kısıtları |
| [`docs/04-permissions.md`](docs/04-permissions.md) | Rol/izin matrisi |
| [`docs/05-state-models.md`](docs/05-state-models.md) | Durum makineleri |
| [`docs/06-whatsapp.md`](docs/06-whatsapp.md) | Hatırlatma, webhook, template, idempotency |
| [`docs/07-api.md`](docs/07-api.md) | İlk REST yüzeyi |
| [`docs/08-migrations.md`](docs/08-migrations.md) | Migration sırası |
| [`docs/09-testing.md`](docs/09-testing.md) | Test stratejisi |
| [`docs/10-decisions.md`](docs/10-decisions.md) | Alınan kararlar ve onay bekleyen sorular |
| [`CLAUDE.md`](CLAUDE.md) | Kod yazarken uyulacak kurallar |

---

## Geliştirme (Phase 1'den itibaren)

Ön koşullar: Docker + Docker Compose, .NET 10 SDK, Node 22.

```bash
cp .env.example .env     # değerleri doldur — .env commit'lenmez
docker compose up
```

| Servis | Adres |
|---|---|
| Web | http://localhost:3000 |
| API | http://localhost:8080 |
| OpenAPI | http://localhost:8080/openapi |
| Sağlık | http://localhost:8080/health |

İlk giriş: `.env`'deki `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` ile — yalnızca `users` tablosu boşken bir kere çalışır, ilk girişte kalıcı şifre belirlemen istenir.

WhatsApp geliştirmesi Meta hesabı olmadan yapılabilir: `WhatsApp__Provider=Fake` (varsayılan) giden mesajları loglar, gerçek bir API çağrısı yapmaz. Gelen webhook'u taklit eden dev-only uç noktalar (`POST /api/dev/whatsapp/simulate-text`, `simulate-rsvp` — yalnızca Development ortamı) RSVP/opt-out/deterministik intent akışlarını Meta hesabı olmadan uçtan uca test etmeyi sağlıyor. Ayrıntı: [`docs/06-whatsapp.md`](docs/06-whatsapp.md).

### Testler

```bash
cd backend && dotnet test    # birim + entegrasyon (Testcontainers gerçek bir Postgres başlatır - Docker gerekir)
cd frontend && npm run lint && npm run build
```

---

## Güvenlik ve gizlilik

Bu repo **public**. Erişim jetonu, veritabanı parolası, webhook doğrulama jetonu hiçbir koşulda commit'lenmez — tümü ortam değişkeninden okunur, `.env.example` yalnızca şablondur.

Sistem çocuklara ait kişisel veri (ad, doğum tarihi, devamsızlık) ve veli telefon numarası işler. KVKK yükümlülükleri — zaman damgalı açık rıza, aydınlatma metni, saklama süresi, veri silme — [`docs/10-decisions.md`](docs/10-decisions.md) altında izlenir.

---

## Lisans

Özel kullanım. Henüz lisans belirlenmedi.
