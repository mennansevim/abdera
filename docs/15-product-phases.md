# Abdera ürün geliştirme fazları

Bu belge, yönetim panelindeki veli, takvim, mesaj, aidat, maliyet ve veri güvenliği kapsamını
üretimde güvenle ilerletebilmek için fazlara ayırır. Para hareketleri ve otomatik mesajlar,
arayüz hazır görünse bile ilgili backend akışı ve audit kaydı olmadan tamamlanmış sayılmaz.

## Faz 1 — Günlük operasyon deneyimi ✅

- Veli ekleme alanları sürekli görünmez; `Veli ekle` ile modal açılır.
- Ayarlar sayfası ve kalıcı şifre değiştirme akışı eklendi.
- Takvimde Piyano, Gitar, Keman, Bateri ve Hepsi filtresi var.
- Ders kartına tıklayınca tarih, saat, süre, öğretmen, ders türü, durum ve RSVP detayları açılır.
- Aynı saate üç veya daha fazla ders geldiğinde öğrenci adları baş harfleriyle kısalır; renkler ders türüne göre korunur.
- Mesaj Merkezi altında gönderim kayıtları, veli/öğrenci/ders türü görünürlüğü, mesaj şablonu düzenleme, placeholder sürükle-bırak ve WhatsApp önizlemesi var.

## Faz 2 — Aidat ve maliyetin güvenilir kaydı ✅

- Aidat kayıtları ödeme tarihi, yöntem, tutar ve ödeme geçmişiyle gösterilir.
- Toplu tahsilat seçilen başlangıç ayından itibaren 1–24 aya dağıtılır; eksik aidat kayıtları aynı transaction içinde oluşturulur.
- Öğrencinin telafi hakkı ve son kullanım tarihi Aidatlar ekranında görünür.
- Maliyet Takibi parola doğrulamasıyla açılır.
- Bekleyen ödeme, toplanan aidat, gelir ve gider özetleri eklenir.
- Maaş, elektrik/su, kira ve diğer giderler kalıcı `expenses` tablosuna yazılır ve audit kaydı oluşturulur.

## Faz 3 — Mesaj otomasyonu ve operasyon politikaları ✅

- Hatırlatma ayarı (`15/30/45/60` dakika), aktif/pasif durum ve üçüncü RSVP seçeneğinin açık/kapalı olması kalıcı kurum ayarına taşındı (`NotificationAutomationSettings`, `GET/PUT /api/notification-automation-settings`). Mesaj Merkezi'ndeki "Otomatik gönderim ayarları" paneli artık gerçek bu uca bağlı.
- Mevcut `INotificationScheduler`/`NotificationDispatcher` (`BackgroundService`) mimarisi **korundu** — CLAUDE.md'nin "Yapılmayacaklar" listesi Hangfire/Quartz gibi ek zamanlayıcı kütüphanesi eklenmesini `docs/10-decisions.md` üzerinden açık onay olmadan yasaklıyor; bu onay verilmedi. Hatırlatma süresi artık DB'den okunuyor (`LessonSeriesFeatures`, `ChangeRequests.ApproveAsync`), altyapı değişmedi. Her ders için idempotency anahtarı korunuyor (`UNIQUE(type, reference_type, reference_id)`).
- Üçüncü RSVP seçeneği ("Evet ama biraz geç kalacağım") eklendi: `RsvpResponse.AttendingLate`, WhatsApp quick-reply butonu (`rsvp_attending_late`, imzalı payload, Meta Cloud API'de per-ders `components` override'ıyla gönderiliyor), takvim detay popup'ında "Geç kalacak" olarak gösteriliyor, veli web portalında üçüncü buton olarak da mevcut.
- Otomasyon ayarları değiştiğinde bekleyen (henüz gönderilmemiş) `LessonReminder` job'ları dersin gerçek saatine göre yeniden hesaplanıyor; otomasyon kapatılırsa bekleyen job'lar iptal ediliyor (geçmişe dönük toparlama yok); gönderilmiş mesajlar hiçbir durumda değiştirilmiyor.
- Mesaj gönderiminden önce veli rızası, sessiz saatler, WhatsApp template onayı ve tekrar deneme politikaları zaten mevcut `NotificationScheduler`/`NotificationDispatcher` içinde uygulanıyordu, bu fazda değişmedi.

## Faz 4 — Sağlık, yedekleme ve geri dönüş ✅ (SFTP hariç, aşağıya bak)

- PostgreSQL health check (`/health`) + son başarılı yedeklemenin yaşı `SystemHealthMonitor` tarafından periyodik hesaplanıp ana ekranda yeşil/sarı/kırmızı bir şeride bağlandı (`Healthy` iken sessiz kalır, `Degraded`/`Unhealthy`'de görünür). `/dashboard/backups` sayfası ayrıntılı geçmişi + manuel "şimdi yedek al" düğmesini gösterir.
- Günlük şifreli (AES-256-GCM) yedekleme (`BackupService`), tutulma süresi (`Backup__RetentionDays`, varsayılan 30 gün, parametrik), her denemenin (başarılı/başarısız) `backup_runs` tablosunda kalıcı kaydı.
- Yedek alınamazsa: `backup_runs`'a `Failed` + hata mesajı, `audit_log`'a `backup.failed` olayı, `Ops__AlertRecipients`'a e-posta - hepsi uygulandı ve canlı doğrulandı.
- Geri yükleme sonrası tutarlılık kontrolü **uygulama içi otomatik bir özellik olarak değil**, elle çalıştırılan bir runbook olarak uygulandı (`docs/16-backup-restore.md`, `docs/10-decisions.md` G9) - bir "restore" düğmesinin arayüzden kazayla tetiklenebilecek geri dönüşsüz bir işlem olması bilinçli olarak bu kararı doğurdu.
- Boş veritabanı migration testi zaten CI'da her push'ta çalışıyor (`MigrationTests.cs`). Örnek geri yükleme provası `docs/16-backup-restore.md`'deki adımlarla canlıya çıkmadan önce elle yapılmalı - bu bir kod teslimatı değil, operasyonel bir adım.

**Kapsam dışı bırakılan tek parça — gerçek SFTP sunucusuna karşı canlı doğrulama.** Kullanıcı yedekleme hedefi olarak kendi sunucusuna SFTP/SSH'i seçti (`docs/10-decisions.md` G1), ama bu turda sunucu bağlantı bilgileri (host/kullanıcı/anahtar) paylaşılmadı. `SftpBackupStorage` (SSH.NET) yazıldı ve derlendi, ama `FakeBackupStorage` ile (pg_dump→şifreleme→retention→health zincirinin tamamı gerçek) doğrulandı - yalnızca gerçek ağ transferi test edilmedi. Kullanıcı sunucu bilgilerini girip `Backup__Provider=Sftp` yaptığında bu adım ayrıca canlı doğrulanmalı (bkz. `docs/09-testing.md` Faz 4 notları).

## Faz geçiş kuralı

Bir faz; arayüz, API, veritabanı migration'ı, yetki kontrolü, audit kaydı ve testleri birlikte
geçmeden tamamlanmış kabul edilmez. Faz 3 için WhatsApp sağlayıcı bilgileri (gerçek şablon onayı),
Faz 4 için gerçek SFTP sunucu bilgileri ve (istenirse) gerçek bir SMTP sağlayıcısı ayrıca yapılandırılmalıdır.
