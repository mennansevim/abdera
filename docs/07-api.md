# İlk REST API Yüzeyi

Master prompt'un önerdiği yüzeye ek olarak Pricing, MakeupCredit ve TeacherTimeOff uç noktaları var (A1, A2, A3). İsimler Faz 1–2'de netleşecek; bu liste iskelet için başlangıç noktasıdır.

```
POST   /api/auth/login
POST   /api/auth/logout
GET    /api/auth/me
POST   /api/auth/change-password              -- B4: ilk girişte geçici şifre değişimi

GET    /api/students
POST   /api/students
GET    /api/students/{studentId}
PATCH  /api/students/{studentId}
GET    /api/students/{studentId}/timeline

GET    /api/guardians
POST   /api/guardians
PATCH  /api/guardians/{guardianId}

GET    /api/teachers
POST   /api/teachers
PATCH  /api/teachers/{teacherId}
GET    /api/teachers/{teacherId}/availability
POST   /api/teachers/{teacherId}/time-off       -- A3
GET    /api/teachers/{teacherId}/time-off

GET    /api/instruments
POST   /api/instruments

GET    /api/calendar
GET    /api/lessons
POST   /api/lesson-series
PATCH  /api/lesson-series/{seriesId}
POST   /api/lessons/{lessonId}/change-requests
POST   /api/change-requests/{requestId}/approve
POST   /api/change-requests/{requestId}/reject

GET    /api/school-calendar-days                -- A3: tatiller ve okul etkinlikleri
POST   /api/school-calendar-days

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
