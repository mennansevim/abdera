# Terim Sözlüğü

Kod, tablo ve tip adları İngilizce; kullanıcı arayüzü ve WhatsApp metinleri Türkçe. Bu tablo ikisi arasındaki eşleşmeyi sabitler — yeni kod yazarken burada olmayan bir terim icat etmeden önce buraya ekle.

| Türkçe | İngilizce (kod) | Not |
|---|---|---|
| Öğrenci | `Student` | |
| Veli / ebeveyn | `Guardian` | Hesabı yok, WhatsApp üzerinden çözümlenir |
| Öğretmen | `Teacher` | |
| Enstrüman | `Instrument` | Piyano, gitar, keman, bateri |
| Kayıt (öğrenci-enstrüman-öğretmen) | `Enrollment` | |
| Ders serisi (tekrarlayan program) | `LessonSeries` | "Her Salı 18:00" |
| Ders (somut oturum) | `Lesson` | `LessonSeries`'ten üretilen tek olay |
| Ders değişikliği talebi | `LessonChangeRequest` | |
| Telafi dersi | `MakeupLesson` | `Lesson.status = MAKEUP` |
| Telafi hakkı / kredisi | `MakeupCredit` | Kullanılmamış hak; ders değil |
| Öğretmen izni | `TeacherTimeOff` | Hastalık, tatil |
| Okul takvim günü | `SchoolCalendarDay` | Resmi tatil, okul etkinliği |
| Katılım niyeti (RSVP) | `LessonRsvp` | Velinin "geliyorum/gelemiyorum" cevabı |
| Gerçek yoklama | `LessonAttendance` | Öğretmenin işaretlediği fiili durum |
| Geldi | `PRESENT` | |
| Gelmedi (habersiz) | `ABSENT` | |
| Mazeretli | `EXCUSED` | |
| Ücret tarifesi | `TuitionRate` | Ders türü (Birebir/Grup) → aylık tutar, yürürlük tarihiyle. `PriceList`/`PriceListItem`/`FeePlan` üçlüsünün yerini aldı (H1) |
| Ders türü | `CourseKind` | `Individual` (Birebir) / `Group` (Grup) — aidat tutarının tek ekseni |
| İndirim politikası | `BillingSettings` | Çoklu kurs %, kardeş %, vade günü — kurum geneli tek satır |
| Peşin ödeme kademesi | `PrepayDiscountTier` | "N ay ve üzeri peşin ödeyene %X" |
| Peşin ödeme planı | `PrepayPlan` | Yıl başı toplu ödeme kampanyasının bir öğrenci için uygulanmış hâli |
| Aidat / tahakkuk | `Receivable` | Bir döneme ait borç kaydı |
| Tahsilat / ödeme | `Payment` | `Receivable`'a karşı yapılan ödeme |
| Ödenmedi | `UNPAID` | |
| Kısmi ödendi | `PARTIAL` | |
| Ödendi | `PAID` | |
| Vadesi geçmiş | `OVERDUE` | |
| Ders notu | `LessonNote` | |
| Ödev / sonraki hedef | `PracticeAssignment` | |
| Yetenek tanımı | `SkillDefinition` | Ritim, tempo, deşifre, teknik... |
| Yetenek değerlendirmesi | `SkillAssessment` | 1–5 ölçek |
| Bildirim işi | `NotificationJob` | Postgres tabanlı kalıcı kuyruk |
| Ders hatırlatması | `LESSON_REMINDER` | Dersten 1 saat önce |
| Aidat hatırlatması | `PAYMENT_REMINDER` | |
| Doğum günü mesajı | `BIRTHDAY` | |
| Paket bitiyor bildirimi | `PACKAGE_ENDING` | |
| Gelen webhook olayı | `WhatsAppWebhookEvent` | |
| Mesaj şablonu | `MessageTemplate` | Meta onaylı template |
| Rıza (bildirim izni) | `NotificationConsent` | KVKK — geri alınabilir |
| Sessiz saat | `QuietHours` | Zamanlanmış bildirimlerin gönderilmediği aralık |
| Konuşma penceresi | `ConversationWindow` | WhatsApp 24 saatlik serbest metin penceresi |
| Denetim kaydı | `AuditLog` | Kim, ne zaman, ne değiştirdi |
