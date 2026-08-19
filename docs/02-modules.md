# Modül Haritası

9 modül, master prompt'un 8'ine ek olarak **Pricing** (bkz. `docs/10-decisions.md` A1). Her modül `Domain/ Features/ Persistence/` dikey dilimiyle organize edilir — `CLAUDE.md`'deki katman kuralına bak.

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
└── Dashboard/       salt-okunur sorgu modeli (kendi tablosu yok)
```

## Bağımlılık yönü

```
Dashboard  → (okur) People, Scheduling, Attendance, Billing, Messaging
Messaging  → (tetikler) Scheduling, Billing, Attendance olaylarını; kendi verisine sahip
Billing    → People (kim borçlu), Pricing (tutar), Scheduling (hangi ders paketten düşer)
Attendance → Scheduling (hangi Lesson), People (hangi Guardian/Teacher)
Scheduling → People (hangi Student/Teacher/Instrument)
Pricing    → People'a bağımlı değil, bağımsız referans veri
Progress   → People, Scheduling (hangi Lesson'a not düşülüyor)
Auth       → hiçbir modüle bağımlı değil; herkes Auth'a bağımlı (kimlik/izin)
```

Kural: bir modül başka modülün **iç** entity'sine EF navigation property ile join atmaz. İhtiyaç varsa o modülün `Features/` altında sunduğu bir sorgu/servis üzerinden okunur. Örnek: `Billing`, hangi `Student`'ın adı olduğunu `People` modülünün `IPeopleLookup` benzeri küçük bir arayüzünden alır — `Student` entity'sini kendi DbSet'i gibi sorgulamaz.

İstisna: `Dashboard` salt-okunur olduğu için doğrudan SQL/LINQ projeksiyonu ile birden fazla modülün tablosunu okuyabilir (kendi yazma yetkisi yoktur, sadece toplulaştırır).

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
dashboard  : (tablosu yok — diğer modüllerin projeksiyonu)
```

Ayrıntılı kolon/kısıt listesi: `docs/03-erd.md`.
