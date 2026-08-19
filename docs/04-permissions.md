# Rol ve İzin Matrisi

İki rol: `ADMIN`, `TEACHER`. Veli hesabı yok — WhatsApp üzerinden telefon numarası + öğrenci-veli ilişkisiyle çözümlenir, bu yüzden matriste yer almıyor.

Kural: her izin **sunucu tarafında** zorlanır (endpoint/handler seviyesinde). Frontend'deki gizleme sadece UX'tir, güvenlik sınırı değildir.

| Kaynak / işlem | ADMIN | TEACHER |
|---|---|---|
| Öğrenci/veli oluşturma, düzenleme | ✅ | ❌ |
| Öğrenci/veli listesi ve detayı | ✅ tümü | ✅ yalnızca kendi atanmış öğrencileri |
| Öğretmen oluşturma, düzenleme | ✅ | ❌ |
| Öğretmen listesi | ✅ | ✅ (isim/enstrüman görünür, kişisel veri yok) |
| Enstrüman/kayıt (enrollment) yönetimi | ✅ | ❌ |
| Fiyat listesi görüntüleme/düzenleme | ✅ | ❌ |
| Ders serisi oluşturma/düzenleme | ✅ | ❌ (yalnızca talep açabilir) |
| Haftalık takvim — tüm okul | ✅ | ❌ |
| Kendi programı ("Bugünkü Derslerim") | ✅ (herkesinkini görebilir) | ✅ yalnızca kendisi |
| Ders değişikliği talebi açma | ✅ | ✅ kendi dersi için |
| Ders değişikliği onay/red | ✅ | ❌ |
| Yoklama işaretleme | ❌ (gerekirse override edebilir, audit'e düşer) | ✅ yalnızca kendi dersi |
| Ders notu / ödev / yetenek puanı girme | ❌ (salt okuma) | ✅ yalnızca kendi öğrencisi |
| RSVP durumu görüntüleme | ✅ tümü | ✅ yalnızca kendi dersleri |
| Aidat / tahsilat / ödeme kaydı | ✅ | ❌ |
| Okul geneli mali özet | ✅ | ❌ |
| WhatsApp bildirim durumu / yeniden deneme | ✅ | ❌ |
| Dashboard (bugün / dikkat / yaklaşan) | ✅ okul geneli | ✅ yalnızca kendi dersleri özeti |
| Kullanıcı/rol yönetimi, geçici şifre atama | ✅ | ❌ (yalnızca kendi şifresini değiştirir) |
| Denetim kaydı (audit log) görüntüleme | ✅ | ❌ |

## Sunucu tarafı zorlama noktaları

- Her `TEACHER` isteğinde `teacherId` (JWT/cookie'den değil, oturumdan) ile hedef kaynağın `teacher_id`'si karşılaştırılır — URL'deki id'ye güvenilmez.
- Bir öğretmen başka öğretmenin dersine yoklama/not girmeye çalışırsa `403`, `audit_log`'a "yetkisiz erişim denemesi" düşülmez (audit sadece başarılı hassas işlemler içindir) ama uygulama logunda görünür.
- Mali uç noktalar (`/api/receivables`, `/api/payments`, fiyat listesi) rol kontrolünü middleware/policy seviyesinde yapar, controller içinde `if (role == ...)` tekrarlanmaz.
- `GET /api/students/{id}` gibi tekil kaynak uç noktaları, `TEACHER` için önce "bu öğrenci bana atanmış mı" kontrolü yapar — yalnızca liste uç noktasını filtrelemek yetmez.
