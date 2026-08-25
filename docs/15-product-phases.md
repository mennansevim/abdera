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

## Faz 6 — Ölçülebilir öğrenci gelişimi ✅

- Ortak ve enstrümana özel yetenek tanımları migration ile seed edilir.
- Öğretmen yalnızca atanmış öğrencisine, 1–5 arası puan ve kısa notla yetenek değerlendirmesi ekleyebilir; isterse değerlendirmeyi kendi dersine bağlar. Admin salt okuyabilir.
- Öğrenci gelişim yanıtı ders notlarının yanında yetenek değerlendirmelerini de en yeniden eskiye döndürür.
- Ders bazlı çalışma ödevleri oluşturulabilir ve tek yönlü olarak tamamlandı durumuna geçirilebilir; başka öğretmenin öğrencisi/dersi sunucu tarafında `403` alır.
- Domain kuralları unit testlerle, rol/kapsam/migration/HTTP akışları gerçek PostgreSQL integration testleriyle korunur.

## Faz 7 — Takvim ve ders planlama ✅

- Boş hücre çift tıklama, başlangıç günü/saati, müsaitlik ve ortak boş slot önerileri tamamlandı.
- Öğretmen + enstrüman filtreleri birlikte çalışır; sürükle-bırak hedef zamanı görünür ve
  taşıma change-request/audit zinciri üzerinden kalıcıdır.
- Yönetici ders detayından öğrenci, öğretmen, tarih, saat, 15–180 dakika süre ve durumu
  güncelleyebilir. Tarihsel ders silinmez; sürümlenir, bildirimler yeniden planlanır.

## Faz 8 — Aidat dönem görünümü ve düzeltme defteri ✅

- Aidat ekranı finansal dönem alanlarına indirildi; autocomplete isim, veli telefonu,
  öğretmen ve enstrümanla arar; filtre ve özet aynı sorgu kümesini kullanır.
- Tekil/toplu/kısmi ödeme kalıcıdır. Ödemeler silinmez; düzeltmeler ayrı, değiştirilemez
  `payment_corrections` satırları ve audit kaydıyla etkin toplamı yeniden hesaplar.
- Demo seed açık/gecikmiş/kısmi/ödenmiş sekmelerini dolduran 20–30 kayıt üretir.

## Faz 9 — Kurs kayıtları ✅

- Öğretmen satırı öğrenci sayısını ve açılır öğrenci listesini gösterir; öğrenci ekleme ve
  başka kurs bağlama akışları ortak enrollment API'sini kullanır.
- Kurs kaldırma öğrenci/öğretmeni silmeden enrollment'ı sonlandırır ve audit'e yazar.
- Aynı öğrenci–öğretmen–enstrüman üçlüsünde yalnızca bir aktif enrollment filtreli unique
  index ile güvence altındadır.

## Faz 10 — Eser, gelişim ve veli yorumu ✅

- Eser adı/besteci/enstrüman/seviye/durum/hedef tarih/kaynak alanları ve kronolojik filtreli
  gelişim görünümü tamamlandı.
- Ham öğretmen notu veliye dönmez. Veli yorumu ayrı taslak, açık öğretmen onayı ve geri
  çekme yaşam döngüsüne sahiptir; yalnızca onaylı yorum ve görünür işaretli kaynak veliye açılır.
- Harici AI sağlayıcısı yapılandırılmadığında manuel düzenleme eksiksiz kalır; UI sağlayıcının
  kullanılamadığını açıkça söyler ve sahte AI sonucu üretmez.

## Faz 11 — Hafif bağlılık ✅

- Pratik günlüğü tarih/süre/hedef/not, veli onayı ve basit rozetlerle veli portalına eklendi.
- Enstrüman bakım periyodu/aktiflik/WhatsApp tercihi yönetilebilir; yalnızca rızalı veliler
  mevcut job sistemi ve sessiz saat kurallarıyla bildirim alır.
- Yönetici panosunda son devamsızlık eşiğine dayalı, gerekçesi gösterilen “ilgi gerektiren
  öğrenci” sinyali vardır; otomatik ayrılma veya yaptırım üretmez.

## Faz 12 — Production kalite kapısı ⚠️ dış sağlayıcı doğrulamaları blokeli

- Production, Fake WhatsApp/banka/yedek sağlayıcısı, varsayılan admin şifresi veya placeholder
  secret ile fail-fast olur; Development akışı korunur.
- `/health` ve `/api/system/health` veritabanı/yedek/sağlayıcı durumunu sır döndürmeden raporlar.
- Banka webhook'u paylaşılan sır, alan doğrulama ve idempotency ile sertleştirildi.
- `284` backend testi, lint/build, üç rol Playwright E2E, Compose smoke, temiz migration ve
  ayrı veritabanına restore provası 25 Ağustos 2026'da geçti (`docs/09-testing.md`,
  `docs/16-backup-restore.md`).
- Gerçek SFTP aktarımı, Meta template/token doğrulaması ve banka sandbox doğrulaması için
  kullanıcıya ait sağlayıcı seçimleri/kimlik bilgileri hâlâ gereklidir; kod bunları taklit ederek
  tamamlandı iddiasında bulunmaz.

## Faz geçiş kuralı

Bir faz; arayüz, API, veritabanı migration'ı, yetki kontrolü, audit kaydı ve testleri birlikte
geçmeden tamamlanmış kabul edilmez. Gerçek WhatsApp şablon onayı, SFTP sunucu bilgileri,
banka sağlayıcısı ve (istenirse) gerçek SMTP sağlayıcısı ayrıca yapılandırılmalıdır.
