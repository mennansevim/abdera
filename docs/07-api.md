# İlk REST API Yüzeyi

Master prompt'un önerdiği yüzeye ek olarak ücret tarifesi/indirim politikası, MakeupCredit, TeacherTimeOff ve Banking uç noktaları var (A1→H1, A2, A3, E1 — Banking master prompt'ta hiç yoktu, sonradan onaylanan bir kapsam genişlemesi). `✅` işaretli satırlar gerçekten uygulandı (Phase 1–6, Dashboard denetim sonrası E2).

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
DELETE /api/students/{studentId}/enrollments/{enrollmentId} ✅ kursu silmeden enrollment'ı sonlandırır
GET    /api/students/attention-needed            ✅ Admin/Teacher scope; açıklanabilir son-devamsızlık sinyali

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
PATCH  /api/lessons/{lessonId}                  ✅ Admin; öğrenci/öğretmen/tarih/süre/durum, sürümleme + audit

GET    /api/school-calendar-days                ✅ A3: tatiller ve okul etkinlikleri
POST   /api/school-calendar-days                ✅

GET    /api/lessons/{lessonId}/rsvp             ✅ eklendi - source=ADMIN (Phase 3) veya source=WHATSAPP (Phase 5, buton yanıtı)
POST   /api/lessons/{lessonId}/rsvp             ✅ eklendi
GET    /api/lessons/{lessonId}/attendance       ✅
POST   /api/lessons/{lessonId}/attendance       ✅ Teacher(kendi)/Admin(override, audit'e düşer)
GET    /api/lessons/{lessonId}/notes            ✅ Admin salt okuma, Teacher kendi dersi
POST   /api/lessons/{lessonId}/notes            ✅ yalnızca Teacher
PUT    /api/lesson-notes/{noteId}/parent-comment ✅ taslak kaydeder; Approve=true ise veliye açar
POST   /api/lesson-notes/{noteId}/parent-comment/revoke ✅ onayı geri çeker, audit'e yazar
GET    /api/skill-definitions                        ✅ ortak + enstrümana özel yetenek tanımları (?instrumentId=)
GET    /api/students/{studentId}/skill-assessments   ✅ Teacher kendi öğrencisi, Admin salt okuma
POST   /api/students/{studentId}/skill-assessments   ✅ yalnızca Teacher; puan 1–5, ders bağı isteğe bağlı
GET    /api/students/{studentId}/progress            ✅ ders notu + yetenek değerlendirmeleri, rol kapsamlı
GET    /api/lessons/{lessonId}/practice-assignments  ✅ Admin salt okuma, Teacher kendi dersi
POST   /api/lessons/{lessonId}/practice-assignments  ✅ yalnızca Teacher
PATCH  /api/practice-assignments/{id}/complete       ✅ yalnızca dersi atanan Teacher; tek yönlü tamamlama
GET    /api/students/{studentId}/practice-journal    ✅ Teacher kendi öğrencisi, Admin okul geneli
POST   /api/students/{studentId}/practice-journal   ✅ staff girişi; süre 1–600 dk
GET    /api/guardian/me/students/{studentId}/practice-journal ✅ yalnızca bağlı öğrenci
POST   /api/guardian/me/students/{studentId}/practice-journal ✅ veli girişi
POST   /api/guardian/me/practice-journal/{entryId}/approve    ✅ veli onayı

GET    /api/tuition-rates                       ✅ H1 - yürürlükteki + geçmiş tarifeler
POST   /api/tuition-rates                       ✅ zam = yeni satır; öncekisi bir gün öncesinden otomatik kapanır
GET    /api/billing-policy                      ✅ H5 - çoklu kurs/kardeş %, vade günü, peşin kademeleri
PUT    /api/billing-policy                      ✅ kademeler tam değişimle yazılır

PATCH  /api/students/{studentId}/enrollments/{enrollmentId}  ✅ H1/H5 - ders türü + kursa özel elle indirim

GET    /api/receivables                         ✅ ?status= filtresiyle
POST   /api/receivables                         ✅ tarife + indirimlerden hesaplanır, satıra donar
POST   /api/receivables/{receivableId}/cancel   ✅ eklendi - PAID iptal edilemez
POST   /api/receivables/{receivableId}/payments ✅ CASH/TRANSFER/CARD/OTHER, durumu yeniden hesaplar
GET    /api/receivables/monthly-run             ✅ ?period=yyyy-MM - açılacak/zaten var/tarifesiz dökümü + indirim toplamı
POST   /api/receivables/monthly-run             ✅ dönemin aidatını tüm aktif kayıtlar için tek çağrıda açar; yeni kayıt yoksa 409
POST   /api/payments/{paymentId}/corrections    ✅ değiştirilemez düzeltme satırı; fazla ödeme reddi + audit
GET    /api/students/{studentId}/billing        ✅ tüm kayıtların aidat/ödeme geçmişi tek ekranda
POST   /api/receivables/{receivableId}/send-reminder   ✅ Phase 5 - elle PAYMENT_REMINDER job'ı kurar

GET    /api/students/{studentId}/makeup-credits  ✅ A2
POST   /api/makeup-credits/{creditId}/use        ✅ yeni bir MAKEUP dersi açar

GET    /api/me/notifications                     ✅ oturumdaki personelin ekran içi bildirimleri {items,unreadCount}
POST   /api/me/notifications/{id}/read            ✅ yalnızca kendi bildirimi - başkasınınki 404
POST   /api/me/notifications/read-all             ✅ zili sıfırlar

GET    /api/notifications                        ✅ ?status=&page=&pageSize= (varsayılan 50, en fazla 200) - yanıt {items,totalCount,page,pageSize} zarfında (ARC-3)
POST   /api/notifications/{notificationId}/retry ✅ yalnızca FAILED durumundan
GET    /api/webhooks/whatsapp                    ✅ Meta abonelik doğrulama handshake'i
POST   /api/webhooks/whatsapp                    ✅ imza doğrulama + idempotency + RSVP/opt-out/intent yönlendirme
POST   /api/dev/whatsapp/simulate-text           ✅ yalnızca Development - serbest metin/opt-out testi
POST   /api/dev/whatsapp/simulate-rsvp           ✅ yalnızca Development - imzalı RSVP butonu testi ("Faz 3'ten itibaren rsvp_attending_late de kabul eder)

GET    /api/message-templates                    ✅ Faz 1 - Mesaj Merkezi şablon editörü (redesign/sicak-atolye)
POST   /api/message-templates                    ✅ yeni şablon ekler
PATCH  /api/message-templates/{templateId}       ✅ gövde/dil/aktiflik günceller - şablon anahtarı (Name) sabit kalır

GET    /api/notification-automation-settings     ✅ Faz 3 - hatırlatma süresi/aktiflik/3. RSVP seçeneği (tek satırlık kurum ayarı)
PUT    /api/notification-automation-settings     ✅ günceller; bekleyen LessonReminder job'larını yeniden hesaplar veya (kapatılırsa) iptal eder, audit_log'a yazar

GET    /api/enrollments/{enrollmentId}/prepay-preview ✅ H7/H8 - ?startPeriod=&months= - taban/indirim/net toplam, sunucu hesaplar
POST   /api/enrollments/{enrollmentId}/prepay-plans   ✅ H8 - 1-24 ay peşin tahsilat; expectedTotal ekranla sunucu ayrışmışsa 409

GET    /api/instrument-maintenance-settings      ✅ Admin; bakım periyodu/aktiflik/kanal ve rızalı veli sayısı
PUT    /api/instruments/{instrumentId}/maintenance-setting ✅ Admin upsert + audit
POST   /api/instrument-maintenance-settings/run-due ✅ vadesi gelen rızalı veli job'larını idempotent planlar

GET    /api/expenses                             ✅ Faz 2 - Maliyet Takibi gider defteri, ?from=&to= filtresiyle
POST   /api/expenses                             ✅ maaş/elektrik-su/kira/diğer - kayıtlar silinmez

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

GET    /api/system/health                       ✅ DB + yedek tazeliği + sır içermeyen sağlayıcı durumları
GET    /api/backup-runs                         ✅ Faz 4 - ?page=&pageSize= (varsayılan 20) - yanıt {items,totalCount,page,pageSize} zarfında
POST   /api/backup-runs/trigger                 ✅ Faz 4 - manuel yedeklemeyi arka planda başlatır, hemen 202 döner
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
  "upcomingBirthdays": [
    { "studentId": "...", "studentName": "Ela Kaya", "birthDate": "2017-09-16", "nextOccurrence": "2026-09-16", "daysUntil": 5, "turningAge": 9 }
  ],
  "upcomingSchoolEvents": 1
}
```

Rol bazlı davranış: `TEACHER` bu uç noktayı çağırdığında sayılar okul geneli değil, yalnızca kendi dersleri üzerinden hesaplanır (bkz. `docs/04-permissions.md`).

`upcomingBirthdays` önceden yalnızca bir sayıydı (hiçbir ekranda gösterilmiyordu) - kullanıcı isteğiyle 30 günlük pencere içindeki öğrencilerin gerçek listesine (`daysUntil`'e göre artan sırayla) çevrildi, `/dashboard` ana ekranında "Yaklaşan Doğum Günleri" bölümü olarak gösterilir.

## Show — yıl sonu gösterisi (I5–I9)

```
GET    /api/shows                                    ✅ özet liste (sıra/öğrenci/süre)      Teacher+Admin
GET    /api/shows/{showId}                           ✅ tam program                          Teacher+Admin
POST   /api/shows                                    ✅                                      Admin
PATCH  /api/shows/{showId}                           ✅                                      Admin
DELETE /api/shows/{showId}                           ✅ programı da siler (cascade)          Admin
POST   /api/shows/{showId}/items                     ✅ sona ekler                           Admin
PATCH  /api/shows/{showId}/items/{itemId}            ✅                                      Admin
DELETE /api/shows/{showId}/items/{itemId}            ✅ kalan sıraları yeniden numaralar      Admin
POST   /api/shows/{showId}/items/reorder             ✅ tüm sıra tek çağrıda; eksik liste 400 Admin

GET    /api/shows/{showId}/stage                     ✅ ŞU AN / SIRADAKİ / ONDAN SONRAKİ      Teacher+Admin
POST   /api/shows/{showId}/start                     ✅ başlatır ve ilk sıraya geçer          Admin
POST   /api/shows/{showId}/advance                   ✅ program bittiyse 409                  Admin
POST   /api/shows/{showId}/back                      ✅ ilk sıradaysa 409                     Admin
POST   /api/shows/{showId}/goto/{itemId}             ✅ araya atlama                          Admin
POST   /api/shows/{showId}/finish                    ✅                                       Admin
POST   /api/shows/{showId}/reopen                    ✅ yanlışlıkla bitirilirse geri alır     Admin

GET    /api/students/{studentId}/photo                ✅ ETag'li, 304 döner                   Teacher+Admin
PUT    /api/students/{studentId}/photo                ✅ multipart, ≤2 MB, JPEG/PNG/WebP      Admin
DELETE /api/students/{studentId}/photo                ✅                                      Admin
```

## Kalıcı kişi silme (I1–I4)

```
GET    /api/students/{studentId}/deletion-impact      ✅ ne silineceğinin dökümü               Admin
DELETE /api/students/{studentId}[?force=true]         ✅ ödeme varsa force olmadan 409         Admin
GET    /api/teachers/{teacherId}/deletion-impact      ✅                                       Admin
DELETE /api/teachers/{teacherId}[?force=true][&reassignTo=]  ✅ devir varsa hiçbir veri silinmez  Admin
```

## Öğretmen portalı — öğrenci silme talepleri (J2)

```
POST   /api/students/{studentId}/deletion-requests       ✅ öğretmen kendi öğrencisi için; gerekçe zorunlu   Teacher+Admin
GET    /api/student-deletion-requests[?status=]          ✅ öğretmen kendi talepleri, admin tümü + etki dökümü Teacher+Admin
POST   /api/student-deletion-requests/{id}/approve       ✅ onaylar ve siler (PersonEraser)                   Admin
POST   /api/student-deletion-requests/{id}/reject        ✅                                                    Admin
```

İzni gevşetilen mevcut uçlar (J1) — hepsi "kendi öğrencisi" kapsamıyla:
`POST /api/teachers/{id}/students`, `PATCH /api/students/{id}`,
`POST /api/students/{id}/enrollments`, `POST|GET /api/students/{id}/guardians`,
`POST /api/guardians`, `PATCH /api/guardians/{id}`.
`GET /api/guardians` (okul geneli liste) Admin'de kaldı.
