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

## Faz 3 — Mesaj otomasyonu ve operasyon politikaları

- Hatırlatma ayarı (`15/30/45/60` dakika), aktif/pasif durum ve RSVP seçenekleri kalıcı kurum ayarına taşınacak.
- Mevcut `INotificationScheduler`/`NotificationDispatcher` (`BackgroundService`) mimarisi **korunacak** — CLAUDE.md'nin "Yapılmayacaklar" listesi Hangfire/Quartz gibi ek zamanlayıcı kütüphanesi eklenmesini `docs/10-decisions.md` üzerinden açık onay olmadan yasaklıyor; bu onay verilmedi. Yeni gereksinim (admin panelden değiştirilebilir hatırlatma süresi) mevcut porta DB-destekli bir ayar okuma noktası eklenerek karşılanacak, altyapı değişmeyecek. Her ders için idempotency anahtarı zaten korunuyor (`UNIQUE(type, reference_type, reference_id)`).
- Mesaj gönderiminden önce veli rızası, sessiz saatler, WhatsApp template onayı ve tekrar deneme politikaları zorunlu kontrol olacak (bunların çoğu zaten mevcut `NotificationScheduler`/`NotificationDispatcher` içinde uygulanıyor).
- Otomasyon ayarları değiştiğinde eski bekleyen job'lar yeniden hesaplanacak; gönderilmiş mesajlar değiştirilmeyecek.

## Faz 4 — Sağlık, yedekleme ve geri dönüş

- PostgreSQL health check ana ekranda yeşil/sarı/kırmızı durum kartına bağlanacak.
- Günlük şifreli yedekleme, tutulma süresi, son başarılı yedekleme zamanı ve geri yükleme denemesi kaydedilecek.
- Yedek alınamazsa kırmızı alarm, audit olayı ve yapılandırılmış e-posta bildirimi üretilecek.
- Aidat, ödeme, gider, audit ve mesaj job verileri için geri yükleme sonrası tutarlılık kontrolü çalışacak.
- Canlıya çıkmadan önce boş veritabanı migration testi ve örnek geri yükleme provası zorunlu kabul kriteri olacak.

## Faz geçiş kuralı

Bir faz; arayüz, API, veritabanı migration'ı, yetki kontrolü, audit kaydı ve testleri birlikte
geçmeden tamamlanmış kabul edilmez. Faz 3 için WhatsApp sağlayıcı bilgileri (gerçek şablon onayı),
Faz 4 için yedekleme hedefi ve e-posta sağlayıcısı ayrıca yapılandırılmalıdır.
