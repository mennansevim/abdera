# Rol ve İzin Matrisi

Üç rol: `ADMIN`, `TEACHER`, `GUARDIAN`. İlk ikisi `users` tablosunda e-posta/şifre ile;
`GUARDIAN` ise `docs/10-decisions.md` Karar F reversal ile eklendi - telefon numarası + WhatsApp
OTP ile giriş yapar, `users` tablosunda hiçbir zaman bir satırı olmaz (bkz.
`Modules/People/Features/GuardianAuth.cs`). Veli için WhatsApp hâlâ birincil kanal; web erişimi
yalnızca kendi verisine *bakabilmek* için ek bir yüzey, aşağıdaki GUARDIAN satırının dışındaki
hiçbir işlemi (aidat, bildirim geçmişi, başka bir öğrencinin verisi) kapsamaz.

Kural: her izin **sunucu tarafında** zorlanır (endpoint/handler seviyesinde). Frontend'deki gizleme sadece UX'tir, güvenlik sınırı değildir.

| Kaynak / işlem | ADMIN | TEACHER | GUARDIAN |
|---|---|---|---|
| Öğrenci/veli oluşturma, düzenleme | ✅ tümü | ✅ yalnızca kendi öğrencisi ve onun velisi (J1) | ❌ |
| Öğrenci silme | ✅ doğrudan | ⏳ gerekçeli talep açar, yönetici karara bağlar (J2) | ❌ |
| Kurs kaydı açma | ✅ | ✅ yalnızca kendi adına ve zaten kendi öğrencisine | ❌ |
| Kurs kaydı kaldırma | ✅ | ❌ (bir silmedir) | ❌ |
| Okul geneli veli listesi | ✅ | ❌ (yalnızca kendi öğrencisinin velisi) | ❌ |
| Öğrenci/veli listesi ve detayı | ✅ tümü | ✅ yalnızca kendi atanmış öğrencileri | ✅ yalnızca kendi öğrencisi (`GET /api/guardian/me/students`) |
| Öğretmen oluşturma, düzenleme | ✅ | ❌ | ❌ |
| Öğretmen listesi | ✅ | ✅ (isim/enstrüman görünür, kişisel veri yok) | ❌ (yalnızca kendi öğrencisinin öğretmen adı, students yanıtı içinde) |
| Uygunluk (müsait gün) tanımlama | ✅ tümü | ✅ yalnızca kendisi (K1) | ❌ |
| Enstrüman/kayıt (enrollment) yönetimi | ✅ | ❌ | ❌ |
| Fiyat listesi görüntüleme/düzenleme | ✅ | ❌ | ❌ |
| Ders serisi oluşturma/sonlandırma | ✅ tümü | ✅ yalnızca kendi kurs kaydı (enrollment) üzerinden (K2) | ❌ |
| Haftalık takvim — tüm okul | ✅ | ❌ | ❌ |
| Kendi programı ("Bugünkü Derslerim") | ✅ (herkesinkini görebilir) | ✅ yalnızca kendisi | ✅ yalnızca kendi öğrencisinin dersleri (`GET /api/guardian/me/students/{id}/calendar`) |
| Ders değişikliği talebi açma | ✅ | ✅ kendi dersi için | ❌ |
| Ders değişikliği onay/red | ✅ | ❌ | ❌ |
| Yoklama işaretleme | ❌ (gerekirse override edebilir, audit'e düşer) | ✅ yalnızca kendi dersi | ❌ |
| Ders notu / ödev / yetenek puanı girme | ❌ (salt okuma) | ✅ yalnızca kendi öğrencisi | ❌ |
| RSVP durumu görüntüleme | ✅ tümü | ✅ yalnızca kendi dersleri | ✅ yalnızca kendi cevabı |
| RSVP ayarlama (Geliyorum/Gelemiyorum) | ✅ (herhangi bir veli adına, WhatsApp'ın yerini tutan geçici kanal) | ❌ | ✅ yalnızca kendi adına, kendi öğrencisinin dersi için (`POST /api/guardian/me/lessons/{id}/rsvp`) |
| Aidat / tahsilat / ödeme kaydı | ✅ | ❌ | ❌ (kapsam dışı - hâlâ WhatsApp/mock) |
| Okul geneli mali özet | ✅ | ❌ | ❌ |
| WhatsApp bildirim durumu / yeniden deneme | ✅ | ❌ | ❌ |
| Dashboard (bugün / dikkat / yaklaşan) | ✅ okul geneli | ✅ yalnızca kendi dersleri özeti | ❌ (ayrı, basit bir veli özeti var - dashboard değil) |
| Kullanıcı/rol yönetimi, geçici şifre atama | ✅ | ❌ (yalnızca kendi şifresini değiştirir) | ❌ (şifre kavramı yok, OTP her seferinde yeniden istenir) |
| Denetim kaydı (audit log) görüntüleme | ✅ | ❌ | ❌ |

## Sunucu tarafı zorlama noktaları

- Her `TEACHER` isteğinde `teacherId` (JWT/cookie'den değil, oturumdan) ile hedef kaynağın `teacher_id`'si karşılaştırılır — URL'deki id'ye güvenilmez.
- Bir öğretmen başka öğretmenin dersine yoklama/not girmeye çalışırsa `403`, `audit_log`'a "yetkisiz erişim denemesi" düşülmez (audit sadece başarılı hassas işlemler içindir) ama uygulama logunda görünür.
- Mali uç noktalar (`/api/receivables`, `/api/payments`, fiyat listesi) rol kontrolünü middleware/policy seviyesinde yapar, controller içinde `if (role == ...)` tekrarlanmaz.
- `GET /api/students/{id}` gibi tekil kaynak uç noktaları, `TEACHER` için önce "bu öğrenci bana atanmış mı" kontrolü yapar — yalnızca liste uç noktasını filtrelemek yetmez.
- Aynı ilke `GUARDIAN` için de geçerli: `/api/guardian/me/*` altındaki her uç nokta, URL'deki `studentId`/`lessonId`'ye güvenmeden önce `StudentGuardians` üzerinden "bu öğrenci/ders gerçekten bu veliye mi bağlı" kontrolü yapar (`GuardianPortal.cs::EnsureOwnsStudentAsync`) — aksi halde bir veli başka bir öğrencinin id'sini tahmin ederek verisine erişebilirdi.

## J — Öğretmen portalı (2026-09-16)

Öğretmen artık kendi öğrencisini ekleyebiliyor, düzenleyebiliyor ve velisini girebiliyor.
"Kendi öğrencisi" kelimesinin tek tanımı `Modules/People/Features/PeopleAuthorization.cs`
içindedir — öğretmenin AKTİF bir kurs kaydı üzerinden bağlı olduğu öğrenci.

İki ayrı kontrolün birlikte uygulanması şart, testle yakalanan gerçek bir açık vardı:
`POST /api/students/{id}/enrollments` yalnızca "istekteki teacherId kendisi mi" diye
kontrol edilseydi, bir öğretmen kendini HERHANGİ bir öğrencinin öğretmeni yazarak o
öğrencinin verisine erişebilirdi. Bu yüzden ikinci kontrol de var: öğrenci zaten o
öğretmenin olmalı (`TeacherPortalFlowTests.Teacher_cannot_act_on_behalf_of_another_teacher`).

## K — Öğretmen kendi ders programını kurar (2026-09-17)

Kullanıcı isteği: "öğretmenler kendi programları yapsınlar, ders programlarını girebilsinler."

İki uç grubu Admin'den `TeacherOrAdmin`'e açıldı; "kendi" kelimesinin bu modüldeki tek tanımı
`Modules/Scheduling/Features/SchedulingAuthorization.cs` içindedir (People'daki
`PeopleAuthorization` ile aynı desen, modül sınırı gereği kopyası duruyor):

- `POST`/`DELETE /api/teachers/{teacherId}/availability` — hedef `teacherId` oturumdan çözülen
  id ile aynı olmalı, aksi halde `403`.
- `POST /api/lesson-series`, `PATCH /api/lesson-series/{id}`, `POST /api/lesson-series/{id}/generate`,
  `POST /api/lesson-series/{id}/reschedule` — kapsam kontrolü **istekteki id üzerinden değil**,
  serinin `Enrollment.TeacherId`'si üzerinden yapılır. Bir öğretmen başka bir öğretmenin kurs
  kaydına seri açamaz, var olan serisini yeniden üretemez, taşıyamaz ve sonlandıramaz.
- `GET /api/students/{id}/lesson-series` — öğretmene yalnızca KENDİ kurs kaydından doğan
  seriler döner; aynı öğrencinin başka bir öğretmenle olan programı görünmez (boş liste,
  404 değil - öğrencinin varlığı da sızdırılmaz).

Ders serisi takvimi değiştirdiği için oluşturma ve sonlandırma artık `audit_log`'a yazıyor
(`lesson_series.created`, `lesson_series.ended`) — aktörün admin olduğu artık garanti değil,
"kim yaptı" sorusunun yanıtı gerekiyor.

Bu iki grubun dışında kalan ders işlemleri **değişmedi**: var olan bir dersi taşımak/iptal
etmek (`PATCH /api/lessons/{id}`, `POST /api/lessons/{id}/cancel`) ve telafi yerleştirmek hâlâ
yalnızca Admin'de — öğretmen bunlar için `LessonChangeRequest` açar.
