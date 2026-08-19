# Test Stratejisi

`CLAUDE.md`'deki kural: gerçek Postgres yalnızca gerçekten gerektiğinde. Testcontainers testi 3–5 saniyeden başlar; her testte kullanılırsa paket dakikalar sürer ve kimse çalıştırmaz olur (`docs/10-decisions.md` C4).

## Birim testleri (xUnit, veritabanı yok)

- Ders üretimi: rolling window (8–12 hafta), idempotency — iki kez çalıştırınca mükerrer satır olmamalı
- Öğretmen uygunluğu / çakışma doğrulaması (öğrenci çakışması, öğretmen çakışması, geçerli süre/aralık)
- `LessonRsvp` durum geçişleri (UNKNOWN → ATTENDING → NOT_ATTENDING)
- `LessonAttendance` kuralları (bir kez girilir, düzeltme audit'e düşer)
- `Receivable` durum hesaplama (`docs/05-state-models.md`)
- Fiyat snapshot kuralı: `PriceList` güncellenince açık `Receivable`'ların **değişmediği**
- `LessonChangeRequest` onay/red kuralları, `PARENT_REJECTED` → sessizce tekrar değiştirmeme
- `MakeupCredit` doğuşu: ≥24 saat önce iptal → kredi; <24 saat / no-show → kredi yok
- WhatsApp buton payload çözümleme ve imza doğrulama (geçerli/geçersiz imza)
- Sessiz saat öteleme mantığı (A6)
- Konuşma penceresi hesaplama (A7)

## Entegrasyon testleri (Testcontainers.PostgreSql — yalnızca bunlar)

1. Migration'lar boş bir veritabanında baştan sona çalışıyor
2. `notification_jobs` üzerindeki `FOR UPDATE SKIP LOCKED` — iki eşzamanlı worker aynı job'ı iki kez işlemiyor
3. Unique kısıt ihlalleri gerçekten reddediyor (`lesson_series_id + start_at`, `type+reference_type+reference_id`, `provider_event_id`, `enrollment_id+period`)
4. Webhook idempotency — aynı `provider_event_id` iki kez POST edilince ikinci seferde iş etkisi tekrarlanmıyor
5. Rol bazlı yetkilendirme — `TEACHER` başka öğretmenin dersine yazamıyor (`403`)

## Uçtan uca testler (master prompt'un asgari listesi)

1. Yönetici öğrenci, veli, öğretmen ve ders serisi oluşturur
2. Sistem bir `Lesson` ve bir hatırlatma `NotificationJob` üretir
3. WhatsApp RSVP'si, sağlayıcı olayı tekrar gönderse bile **bir kez** kaydedilir
4. Öğretmen yoklama işaretler ve not ekler
5. Yönetici bir ödeme kaydeder ve `Receivable` durumu değişir
6. Bir ders-değişikliği talebi onaylanır ve bildirim oluşturulur

Bu 6 senaryo Phase 1–5 tamamlandığında CI'da (`.github/workflows/ci.yml`) çalışır; Phase 0'da yalnızca senaryo listesi olarak var.
