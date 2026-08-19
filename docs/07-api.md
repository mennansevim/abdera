# İlk REST API Yüzeyi

Master prompt'un önerdiği yüzeye ek olarak Pricing, MakeupCredit ve TeacherTimeOff uç noktaları var (A1, A2, A3). `✅` işaretli satırlar Phase 1–2'de gerçekten uygulandı; işaretsiz olanlar henüz yok (Phase 3+).

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

GET    /api/calendar                            ✅ ?from=&to=&teacherId=&instrumentId=
GET    /api/lessons                             ✅ /api/calendar ile aynı handler
POST   /api/lesson-series                       ✅ oluşturur + ilk rolling window'u üretir
PATCH  /api/lesson-series/{seriesId}            ✅ seriyi sonlandırır (EffectiveUntil)
POST   /api/lesson-series/{seriesId}/generate   ✅ eklendi - üretim penceresini elle uzatır
POST   /api/lessons/{lessonId}/change-requests  -- Phase 3
POST   /api/change-requests/{requestId}/approve -- Phase 3
POST   /api/change-requests/{requestId}/reject  -- Phase 3

GET    /api/school-calendar-days                ✅ A3: tatiller ve okul etkinlikleri
POST   /api/school-calendar-days                ✅

POST   /api/lessons/{lessonId}/attendance
POST   /api/lessons/{lessonId}/notes
POST   /api/students/{studentId}/skill-assessments
GET    /api/students/{studentId}/progress

GET    /api/price-lists                         -- A1
POST   /api/price-lists
POST   /api/price-lists/{priceListId}/preview-bulk-update   -- A1: uygulamadan önce önizleme
POST   /api/price-lists/{priceListId}/apply

GET    /api/receivables
POST   /api/receivables
POST   /api/receivables/{receivableId}/payments
GET    /api/students/{studentId}/billing
POST   /api/receivables/{receivableId}/send-reminder

GET    /api/students/{studentId}/makeup-credits  -- A2
POST   /api/makeup-credits/{creditId}/use

GET    /api/notifications
POST   /api/notifications/{notificationId}/retry
GET    /api/webhooks/whatsapp
POST   /api/webhooks/whatsapp
POST   /api/dev/whatsapp/simulate-webhook        -- yalnızca Development ortamı

GET    /api/dashboard/today
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
