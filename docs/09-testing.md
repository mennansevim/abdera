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

## Phase 5 notları (uygulandıktan sonra eklendi)

- Yukarıdaki listenin tamamı `Unit/MessagingDomainTests.cs` (27 test) ve `Integration/MessagingFlowTests.cs` (12 test, Testcontainers) ile karşılanıyor — `docs/11-progress-log.md`'de ayrıntılı liste.
- **Bilinçli boşluk — `FOR UPDATE SKIP LOCKED`'ın gerçek eşzamanlı iki-worker senaryosu testlenmedi.** Bu ölçekte (`CLAUDE.md` — 6–8 öğretmen, tek `NotificationDispatcher` instance'ı, `docs/10-decisions.md`'nin "mikroservis/Kubernetes yok" kararı) birden fazla worker instance'ı hiç çalışmıyor; `SKIP LOCKED` yalnızca aynı instance içindeki teorik bir yarışa karşı savunma. Sorgunun kendisi (`SELECT ... FOR UPDATE SKIP LOCKED`) `MigrationTests.cs`'in de doğruladığı gibi gerçek Postgres'e karşı çalışıyor (`Dispatcher_sends_a_due_job_through_fake_client_and_marks_it_sent` testi bunu dolaylı doğruluyor - sorgu sözdizimi hatalıysa test de patlardı).
- **Bilinçli boşluk — sessiz saat (A6) dispatch-anı davranışı yalnızca birim testli.** `IClock` gerçek `SystemClock` olduğu için entegrasyon testinde "şu an sessiz saat içinde" durumunu deterministik kuramıyoruz - saf fonksiyonlar (`QuietHours.IsWithinQuietHours`/`ResolveSendTime`) ayrı ayrı birim testli, dispatcher'daki çağrı tek satırlık düz bir if.
- **Yeni öğrenilen kural — globalizasyon/Alpine bug'ları yerel testle yakalanamaz.** `docker compose up` ile Alpine container'ında canlı doğrulama, bu sınıf bug için testin yerini tutmuyor, tamamlıyor. Ayrıntı: `CLAUDE.md` "Kullanıcıya gösterilecek metinde `new CultureInfo(...)` kullanıyorsan Dockerfile'ı kontrol et".
