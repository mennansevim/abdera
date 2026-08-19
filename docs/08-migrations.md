# Migration Sırası

EF Core migrations (`dotnet ef migrations add ...`), master prompt'un istediği Flyway'in yerine (`CLAUDE.md` — stack kararı). Her migration tek modülün tablolarını getirir, önceki migration'ların üzerine FK kurar; sıra bağımlılık grafiğini takip eder (`docs/02-modules.md`).

```
001_auth                  users, audit_log
002_people                instruments, teachers, teacher_instruments,
                           students, guardians, student_guardians, enrollments
003_scheduling            lesson_series, lessons, lesson_change_requests,
                           teacher_availability, teacher_time_off,
                           school_calendar_days
004_attendance            lesson_rsvps, lesson_attendances
005_pricing               price_lists, price_list_items
006_billing               fee_plans, receivables, payments, makeup_credits
007_progress              skill_definitions, lesson_notes,
                           skill_assessments, practice_assignments
008_messaging             notification_jobs, whatsapp_messages,
                           whatsapp_webhook_events, message_templates
009_seed_reference_data   instruments (piyano/gitar/keman/bateri),
                           skill_definitions (ortak + enstrümana özel),
                           message_templates (lesson_reminder_rsvp)
```

## Seed verisi (009)

```
instruments: PIANO, GUITAR, VIOLIN, DRUMS

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
- İlk canlıya almadan önce `dotnet ef database update` boş bir veritabanında baştan sona çalıştırılıp doğrulanır (Phase 0 doğrulama listesindeki madde 1).
