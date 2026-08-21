# İlk REST API Yüzeyi

Master prompt'un önerdiği yüzeye ek olarak Pricing, MakeupCredit, TeacherTimeOff ve Banking uç noktaları var (A1, A2, A3, E1 — Banking master prompt'ta hiç yoktu, sonradan onaylanan bir kapsam genişlemesi). `✅` işaretli satırlar gerçekten uygulandı (Phase 1–6, Dashboard denetim sonrası E2); işaretsiz olanlar henüz yok (Progress modülünün kalanı).

```
POST   /api/auth/login                          ✅
POST   /api/auth/logout                         ✅
GET    /api/auth/me                             ✅
POST   /api/auth/change-password                ✅ B4: ilk girişte geçici şifre değişimi
POST   /api/users/{userId}/reset-password       ✅ Admin, öğretmen şifresini sıfırlar (B4)

GET    /api/students                            ✅ Teacher yalnızca kendi öğrencilerini görür
POST   /api/students                            ✅
GET    /api/students/{studentId}                ✅
PATCH  /api/students/{studentId}                ✅
GET    /api/students/{studentId}/timeline       -- Progress modülü (Phase 6)
GET    /api/students/{studentId}/guardians       ✅ eklendi - docs'ta yoktu, People'ın temel ilişkisi
POST   /api/students/{studentId}/guardians       ✅ eklendi
GET    /api/students/{studentId}/enrollments     ✅ eklendi - Enrollment her zaman bir öğrenciye bağlı
POST   /api/students/{studentId}/enrollments     ✅ eklendi

GET    /api/guardians                           ✅
POST   /api/guardians                           ✅
PATCH  /api/guardians/{guardianId}              ✅

GET    /api/teachers                            ✅
POST   /api/teachers                            ✅ email verilirse giriş hesabı da açılır (B4)
PATCH  /api/teachers/{teacherId}                ✅
GET    /api/teachers/{teacherId}/availability   ✅
POST   /api/teachers/{teacherId}/availability   ✅ eklendi
GET    /api/teachers/{teacherId}/time-off       ✅ A3
POST   /api/teachers/{teacherId}/time-off       ✅ A3

GET    /api/instruments                         ✅
POST   /api/instruments                         ✅

GET    /api/calendar                            ✅ ?from=&to=&teacherId=&instrumentId= - aralık en fazla 3 ay (ARC-3), aşarsa 400
GET    /api/lessons                             ✅ /api/calendar ile aynı handler
POST   /api/lesson-series                       ✅ oluşturur + ilk rolling window'u üretir
PATCH  /api/lesson-series/{seriesId}            ✅ seriyi sonlandırır (EffectiveUntil)
POST   /api/lesson-series/{seriesId}/generate   ✅ eklendi - üretim penceresini elle uzatır
POST   /api/lessons/{lessonId}/change-requests  ✅ Teacher(kendi dersi)/Admin açar
GET    /api/change-requests                     ✅ eklendi - Admin onay kuyruğu (?status=)
POST   /api/change-requests/{requestId}/approve ✅ reschedule: eski ders RESCHEDULED, yeni NORMAL
POST   /api/change-requests/{requestId}/reject  ✅
POST   /api/lessons/{lessonId}/cancel           ✅ eklendi - doğrudan iptal, A2 kredi mantığı burada

GET    /api/school-calendar-days                ✅ A3: tatiller ve okul etkinlikleri
POST   /api/school-calendar-days                ✅

GET    /api/lessons/{lessonId}/rsvp             ✅ eklendi - source=ADMIN (Phase 3) veya source=WHATSAPP (Phase 5, buton yanıtı)
POST   /api/lessons/{lessonId}/rsvp             ✅ eklendi
GET    /api/lessons/{lessonId}/attendance       ✅
POST   /api/lessons/{lessonId}/attendance       ✅ Teacher(kendi)/Admin(override, audit'e düşer)
GET    /api/lessons/{lessonId}/notes            ✅ Admin salt okuma, Teacher kendi dersi
POST   /api/lessons/{lessonId}/notes            ✅ yalnızca Teacher
POST   /api/students/{studentId}/skill-assessments   -- Progress'in kalanı, Phase 6
GET    /api/students/{studentId}/progress            -- Phase 6

GET    /api/price-lists                         ✅ A1
POST   /api/price-lists                         ✅ liste + tüm kalemleri tek seferde
POST   /api/price-lists/{priceListId}/preview-bulk-update   ✅ A1: uygulamadan önce önizleme
POST   /api/price-lists/{priceListId}/apply     ✅ yalnızca bu listenin kalemleri, geçmiş Receivable etkilenmez

POST   /api/enrollments/{enrollmentId}/fee-plan ✅ eklendi - docs'ta yoktu, Receivable'ın ön koşulu
GET    /api/enrollments/{enrollmentId}/fee-plan ✅ eklendi

GET    /api/receivables                         ✅ ?status= filtresiyle
POST   /api/receivables                         ✅ aktif FeePlan'dan snapshot alır
POST   /api/receivables/{receivableId}/cancel   ✅ eklendi - PAID iptal edilemez
POST   /api/receivables/{receivableId}/payments ✅ CASH/TRANSFER/CARD/OTHER, durumu yeniden hesaplar
GET    /api/students/{studentId}/billing        ✅ tüm kayıtların aidat/ödeme geçmişi tek ekranda
POST   /api/receivables/{receivableId}/send-reminder   ✅ Phase 5 - elle PAYMENT_REMINDER job'ı kurar

GET    /api/students/{studentId}/makeup-credits  ✅ A2
POST   /api/makeup-credits/{creditId}/use        ✅ yeni bir MAKEUP dersi açar

GET    /api/notifications                        ✅ ?status=&page=&pageSize= (varsayılan 50, en fazla 200) - yanıt {items,totalCount,page,pageSize} zarfında (ARC-3)
POST   /api/notifications/{notificationId}/retry ✅ yalnızca FAILED durumundan
GET    /api/webhooks/whatsapp                    ✅ Meta abonelik doğrulama handshake'i
POST   /api/webhooks/whatsapp                    ✅ imza doğrulama + idempotency + RSVP/opt-out/intent yönlendirme
POST   /api/dev/whatsapp/simulate-text           ✅ yalnızca Development - serbest metin/opt-out testi
POST   /api/dev/whatsapp/simulate-rsvp           ✅ yalnızca Development - imzalı RSVP butonu testi

POST   /api/guardians/{guardianId}/virtual-iban  ✅ Phase 6 (E1) - veliye sanal IBAN atar, aktifken tekrar atanamaz
GET    /api/guardians/{guardianId}/virtual-iban  ✅ atanmışsa döner, yoksa 404
GET    /api/bank-transactions                    ✅ ?status=&page=&pageSize= (varsayılan 50, en fazla 200) - yanıt {items,totalCount,page,pageSize} zarfında (ARC-3)
POST   /api/bank-transactions/{transactionId}/resolve   ✅ NeedsReview'ı elle bir Receivable'a bağlar (veya "hiçbirine sayma")
POST   /api/webhooks/bank                        ✅ paylaşılan-sır başlığı ile doğrulama + idempotency + eşleştirme (gerçek sağlayıcı seçilince imza şeması değişecek)
POST   /api/dev/bank/simulate-transaction        ✅ yalnızca Development - eşleştirme mantığını gerçek sağlayıcı olmadan test eder

POST   /api/guardian/otp/request                 ✅ yalnızca kayıtlı veli telefonu için WhatsApp OTP
POST   /api/guardian/otp/verify                  ✅ GuardianOnly cookie oturumu açar
GET    /api/guardian/me                          ✅ oturumdaki veliyi döner
GET    /api/guardian/me/students                 ✅ yalnızca bağlı öğrenciler
GET    /api/guardian/me/students/{studentId}/calendar ✅ yalnızca bağlı öğrencinin takvimi
POST   /api/guardian/me/lessons/{lessonId}/rsvp  ✅ yalnızca bağlı ders için veli RSVP'si
GET    /api/guardian/me/billing                  ✅ yalnızca bağlı öğrencilerin salt-okunur aidat/telafi/IBAN görünümü
GET    /api/guardian/me/messages                 ✅ yalnızca velinin giden WhatsApp bildirim geçmişi (son 50)

GET    /api/dashboard/today                     ✅ rol bazlı kapsam (docs/04-permissions.md) - denetim E2/ARC-6
```

## `GET /api/dashboard/today` örnek yanıt

```json
{
  "todayLessons": 22,
  "attending": 15,
  "notAttending": 2,
  "noResponse": 5,
  "pendingChangeRequests": 3,
  "overduePayments": 8,
  "upcomingBirthdays": 2,
  "upcomingSchoolEvents": 1
}
```

Rol bazlı davranış: `TEACHER` bu uç noktayı çağırdığında sayılar okul geneli değil, yalnızca kendi dersleri üzerinden hesaplanır (bkz. `docs/04-permissions.md`).
