# Migration Sırası

EF Core migrations (`dotnet ef migrations add ...`), master prompt'un istediği Flyway'in yerine (`CLAUDE.md` — stack kararı). Her migration bağımlılık grafiğini takip eder (`docs/02-modules.md`); tek DbContext olduğu için (CLAUDE.md) bir migration birden fazla modülün tablosunu içerebilir — bu durumda master prompt'un phase gruplamasına uyulur (örn. "Phase 2 — People and scheduling" tek migration'dır).

**Dosya konumu:** yalnızca tek modülü ilgilendiren migration o modülün `Persistence/Migrations/` altında kalır (örn. `InitialAuth`). Birden fazla modülü ilgilendiren migration'lar `src/Abdera.Api/Persistence/Migrations/` altındaki paylaşılan klasöre gider — belirli bir modülün klasörüne gömülü kalması yanıltıcı olurdu.

**Not:** aşağıdaki `001_auth`, `002_people`... etiketleri mantıksal/kavramsal sıralamayı gösterir. EF Core migration dosyalarını kendi `<timestamp>_<Ad>` biçimiyle üretir — dosya adına elle numara eklenmez. Uygulama sırası, dosya adındaki numaraya değil, migration'ların **oluşturulma sırasına** (timestamp) göre belirlenir; bu yüzden migration'lar burada listelenen modül sırasıyla oluşturulmalı.

```
001_auth (InitialAuth)                    users, audit_log
002_people_and_scheduling                 instruments, teachers, teacher_instruments,
(PeopleAndScheduling)                     students, guardians, student_guardians,
                                           enrollments, lesson_series, lessons,
                                           teacher_availability, teacher_time_off,
                                           school_calendar_days
003_seed_instruments (SeedInstruments)    instruments seed verisi (aşağıda)
004_attendance_changes_and_progress       lesson_attendances, lesson_change_requests,
(AttendanceChangesAndProgress)            lesson_notes, lesson_rsvps, makeup_credits     -- Phase 3
005_pricing_and_billing                   fee_plans, payments, price_list_items,
(PricingAndBilling)                       price_lists, receivables                       -- Phase 4
006_messaging (Messaging)                 notification_jobs, whatsapp_messages,
                                           whatsapp_webhook_events, message_templates     -- Phase 5, uygulandı
007_seed_message_templates                message_templates seed verisi (aşağıda)        -- Phase 5, uygulandı
(SeedMessageTemplates)
008_banking (Banking)                     virtual_ibans, bank_incoming_transactions +
                                           payments.created_by nullable'a çevrildi     -- Phase 6 (E1), uygulandı
009_progress                              skill_definitions, skill_assessments,
                                           practice_assignments                          -- Phase 7
                                           (lesson_notes zaten 004'te - Phase 3'te gerekliydi)
010_seed_skill_definitions                skill_definitions seed verisi (ortak + enstrümana özel)
```

Not: Messaging ve Banking, Progress'ten önce geldi çünkü kullanıcı bu ikisini Progress'ten önce istedi (Phase 5 ve Phase 6) - bu dosyanın önceki bölümü plan aşamasında yazılmıştı, gerçek uygulama sırası planlanan numaralandırmayı bozdu (numaralar zaten yalnızca kavramsal, bkz. yukarıdaki "Not"). Banking master prompt'ta hiç yoktu (`docs/10-decisions.md` E1) - Progress artık Phase 7'ye kaydı.

`lesson_series`/`lessons` başlangıçta People ile aynı migration'da geldi çünkü Phase 2 ("People and scheduling") master prompt'ta tek faz — ayrı migration'lara bölmek yapay bir ayrım olurdu. Aynı şekilde `lesson_change_requests` (Scheduling), `lesson_rsvps`/`lesson_attendances` (Attendance), `lesson_notes` (Progress'in bir dilimi) ve `makeup_credits` (Billing'in bir dilimi) Phase 3'ün tek migration'ında birlikte geldi; Pricing ve Billing'in kalanı (`price_lists`, `price_list_items`, `fee_plans`, `receivables`, `payments`) Phase 4'te tek migration'da birlikte geldi — `docs/02-modules.md`'deki "Kısmi açılan modüller" notuna bak.

## Seed verisi

**003_seed_instruments** (uygulandı):
```
instruments: PIANO (Piyano), GUITAR (Gitar), VIOLIN (Keman), DRUMS (Bateri)
```
`INSERT ... ON CONFLICT (code) DO NOTHING` ile yazılır — tekrar çalıştırılabilir (abdera-migration skill kuralı).

**007_seed_message_templates** (uygulandı, Phase 5):
```
message_templates:
  lesson_reminder_rsvp  -- docs/06-whatsapp.md'deki tam metin, Meta onayı bekliyor (D2)
  lesson_rescheduled    -- kendi taslağımız, Meta onayı bekliyor
  makeup_approved       -- kendi taslağımız, Meta onayı bekliyor
  payment_reminder      -- kendi taslağımız, Meta onayı bekliyor
```
`INSERT ... ON CONFLICT (name) DO NOTHING` ile yazılır. Meta onayı gelene kadar `WhatsApp__Provider=Fake` ile geliştirme paralel ilerler (D2) - şablon adı/parametre isimleri değişirse bu migration'a yeni bir migration ile `UPDATE` eklenir, var olan satır elle düzenlenmez.

**010_seed_skill_definitions** (Phase 6'da eklenecek):
```
skill_definitions (ortak, instrument_id=null):
  RHYTHM, TEMPO_CONTROL, SIGHT_READING, MUSICAL_EXPRESSION,
  TECHNIQUE, PRACTICE_DISCIPLINE

skill_definitions (enstrümana özel):
  PIANO  -> HAND_COORDINATION, PEDAL_USE
  GUITAR -> CHORD_TRANSITION, PICKING, FINGER_POSITION
  VIOLIN -> INTONATION, BOW_CONTROL, LEFT_HAND_POSITION
  DRUMS  -> TIMING, LIMB_INDEPENDENCE, GROOVE_CONSISTENCY
```

## Kurallar

- Her migration `Up`/`Down` çifti içerir; `Down` gerçekten geri alınabilir olmalı (üretimde hiç çalıştırılmasa da, yerelde test edilir).
- Yıkıcı işlem (kolon/tablo silme) ayrı bir migration'da, en az bir sürüm sonra yapılır — "expand/contract" yaklaşımı.
- Her migration, gerçek bir Postgres'e karşı hem ileri (`database update`) hem idempotency (iki kez çalıştırma) açısından doğrulanır — bkz. `docs/09-testing.md` madde 1 ve `MigrationTests.cs`.
- Uygulama başlangıçta bekleyen migration'ları otomatik uygular (`Database__AutoMigrate=true`, `Shared/DatabaseMigrator.cs`) — `docker compose up` tek başına çalışan bir kurulum verir.
