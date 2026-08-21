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
| Öğrenci/veli oluşturma, düzenleme | ✅ | ❌ | ❌ |
| Öğrenci/veli listesi ve detayı | ✅ tümü | ✅ yalnızca kendi atanmış öğrencileri | ✅ yalnızca kendi öğrencisi (`GET /api/guardian/me/students`) |
| Öğretmen oluşturma, düzenleme | ✅ | ❌ | ❌ |
| Öğretmen listesi | ✅ | ✅ (isim/enstrüman görünür, kişisel veri yok) | ❌ (yalnızca kendi öğrencisinin öğretmen adı, students yanıtı içinde) |
| Enstrüman/kayıt (enrollment) yönetimi | ✅ | ❌ | ❌ |
| Fiyat listesi görüntüleme/düzenleme | ✅ | ❌ | ❌ |
| Ders serisi oluşturma/düzenleme | ✅ | ❌ (yalnızca talep açabilir) | ❌ |
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
