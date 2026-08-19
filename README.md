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

**Phase 3 — devam, RSVP, ders değişikliği.** Öğretmen "Bugünkü Derslerim" ekranından yoklama alıp ders notu girebiliyor (ders otomatik tamamlandı oluyor); ders değişikliği talebi açıp Admin onay kuyruğundan onaylanabiliyor (geçmiş korunarak yeni ders açılıyor); doğrudan iptalde ≥24 saat kuralına göre telafi kredisi doğuyor ve kredi yeni bir telafi dersi açmak için kullanılabiliyor. Hepsi `docker compose up` ile uçtan uca doğrulandı. Fiyatlandırma/aidat ve WhatsApp henüz yok — Phase 4'ten itibaren geliyor.

| Faz | Kapsam | Durum |
|---|---|---|
| 0 | Tasarım paketi, alan modeli, API yüzeyi, kararlar | ✅ |
| 1 | İskelet: Compose + Postgres + API + web + auth | ✅ |
| 2 | Kişiler ve takvim | ✅ |
| 3 | Devam, RSVP, ders değişikliği | ✅ Bu commit |
| 4 | Fiyatlandırma ve aidat | ⬜ |
| 5 | WhatsApp | ⬜ |
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
| Zamanlayıcı | `BackgroundService` + `PeriodicTimer` (Postgres `FOR UPDATE SKIP LOCKED`) — Phase 5'te geliyor |
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

WhatsApp geliştirmesi Meta hesabı olmadan yapılabilir: `WhatsApp__Provider=Fake` (varsayılan) giden mesajları loglar, gerçek bir API çağrısı yapmaz. Gelen webhook'u taklit eden dev-only uç nokta, `Messaging` modülüyle birlikte Phase 5'te geliyor. Ayrıntı: [`docs/06-whatsapp.md`](docs/06-whatsapp.md).

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
