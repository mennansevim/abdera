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
| Fiyat listesi | `PriceList` | Yürürlük tarihi aralığıyla |
| Fiyat listesi kalemi | `PriceListItem` | Enstrüman × ders süresi → birim fiyat |
| Ücret planı | `FeePlan` | Bir kayıt için aylık/paket seçimi |
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
