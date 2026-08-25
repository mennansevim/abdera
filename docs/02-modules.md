# Modül Haritası

10 modül, master prompt'un 8'ine ek olarak **Pricing** (`docs/10-decisions.md` A1) ve **Banking** (`docs/10-decisions.md` E1 — master prompt'un başlangıçta hariç tuttuğu, sonradan onaylanan bir kapsam). Her modül `Domain/ Features/ Persistence/` dikey dilimiyle organize edilir — `CLAUDE.md`'deki katman kuralına bak.

```
Modules/
├── Auth/          kullanıcı, rol, oturum, denetim kaydı
├── People/        öğrenci, veli, öğretmen, enstrüman, kayıt
├── Scheduling/     ders serisi, ders, öğretmen izni/uygunluğu, okul takvimi
├── Attendance/     RSVP, gerçek yoklama
├── Pricing/       fiyat listesi ve kalemleri            ← yeni (A1)
├── Billing/        ücret planı, aidat, ödeme, telafi kredisi
├── Progress/       ders notu, yetenek tanımı/değerlendirme, ödev
├── Messaging/       bildirim işi, WhatsApp mesajı/webhook, şablon
├── Banking/         sanal IBAN, gelen havale eşleştirme         ← yeni (E1, Phase 6)
└── Dashboard/       salt-okunur sorgu modeli (kendi tablosu yok)
```

## Bağımlılık yönü

```
Dashboard  → (okur) People, Scheduling, Attendance, Billing, Messaging
Scheduling/Billing → (tetikler) Messaging'i `INotificationScheduler` portu üzerinden (Phase 5,
             uygulandı) - Messaging'in kendi entity'lerine doğrudan bağımlı olmadan job açar
Messaging  → (yazar) Attendance'a - WhatsApp RSVP butonu `LessonRsvp` oluşturur/günceller
             (Phase 5); kendi verisine (notification_jobs, whatsapp_messages, ...) sahip
Banking    → (yazar) Billing'e - eşleşen bir banka işlemi `Payment` oluşturur ve
             `Receivable.RecordPaymentEffect` çağırır (Phase 6, E1); (okur) People'ı -
             velinin bağlı öğrenci/kayıtlarını bulmak için `StudentGuardian`/`Enrollment`
             doğrudan sorgular (Messaging'in Lesson/Student/Teacher'ı doğrudan sorgulamasıyla
             aynı, kurulu pratik - bkz. `NotificationMessageBuilder.cs`); kendi verisine
             (virtual_ibans, bank_incoming_transactions) sahip
Billing    → People (kim borçlu), Pricing (tutar), Scheduling (hangi ders paketten düşer)
Attendance → Scheduling (hangi Lesson), People (hangi Guardian/Teacher)
Scheduling → People (hangi Student/Teacher/Instrument)
Pricing    → People'a bağımlı değil, bağımsız referans veri
Progress   → People, Scheduling (hangi Lesson'a not düşülüyor)
Auth       → hiçbir modüle bağımlı değil; herkes Auth'a bağımlı (kimlik/izin)
```

Kural: bir modül başka modülün **iç** entity'sine EF navigation property ile join atmaz. İhtiyaç varsa o modülün `Features/` altında sunduğu bir sorgu/servis üzerinden okunur. Örnek: `Billing`, hangi `Student`'ın adı olduğunu `People` modülünün `IPeopleLookup` benzeri küçük bir arayüzünden alır — `Student` entity'sini kendi DbSet'i gibi sorgulamaz.

İstisna 1: `Dashboard` salt-okunur olduğu için doğrudan SQL/LINQ projeksiyonu ile birden fazla modülün tablosunu okuyabilir (kendi yazma yetkisi yoktur, sadece toplulaştırır).

İstisna 2 — **yazma tarafı**: bir modülün Feature'ı, başka bir modülün Domain entity'sini kendi genel (public) factory metoduyla oluşturup tek `AbderaDbContext` üzerinden ekleyebilir — bu okuma tarafındaki navigation-property yasağının kapsamı dışında. İki örnek: `People/Features/Teachers.cs` bir `Auth.Domain.User` oluşturur (öğretmen giriş hesabı); `Scheduling/Features/CancelLesson.cs` bir `Billing.Domain.MakeupCredit` oluşturur (telafi kredisi). Bu, tek `DbContext`'li modüler monolitin doğal bir sonucu — mikroservis gibi API çağrısı simüle etmek burada anlamsız olurdu.

## Fazlara bölünerek açılan modüller

`Billing` Phase 4 ile tamamlandı (`makeup_credits` Phase 3'te, `fee_plans`/`receivables`/`payments` Phase 4'te). `Progress` de önce ders notu, sonra ölçülebilir gelişim kayıtları şeklinde tamamlandı:

| Modül | Açılan | Kalan |
|---|---|---|
| Progress | `lesson_notes` (Phase 3); `skill_definitions`, `skill_assessments`, `practice_assignments` (Phase 6) | — |

Bu, "modülü fazın sırasına göre bütün olarak aç" kuralının bilinçli bir istisnasıydı — `MakeupCredit` ve `LessonNote` doğrudan Phase 3'ün kendi kapsamında (master prompt: "make-up lessons", "lesson notes") gerekliydi; Progress'in kalan tabloları ölçümlü gelişim akışı açıldığında eklendi.

## Modül başına tablolar

```
auth       : users, audit_log
people     : students, guardians, student_guardians, teachers,
             instruments, teacher_instruments, enrollments
scheduling : lesson_series, lessons, lesson_change_requests,
             teacher_availability, teacher_time_off, school_calendar_days
attendance : lesson_rsvps, lesson_attendances
pricing    : price_lists, price_list_items
billing    : fee_plans, receivables, payments, makeup_credits
progress   : lesson_notes, skill_definitions, skill_assessments,
             practice_assignments
messaging  : notification_jobs, whatsapp_messages,
             whatsapp_webhook_events, message_templates
banking    : virtual_ibans, bank_incoming_transactions
dashboard  : (tablosu yok — diğer modüllerin projeksiyonu)
```

Ayrıntılı kolon/kısıt listesi: `docs/03-erd.md`.
