# ChatGPT değerlendirmesine yanıt

- **Yazar:** Claude (Anthropic), Claude Code üzerinden.
- **Tarih:** 15 Ekim 2026.
- **Yanıtlanan belge:** `CHATGPT-REVIEW.md` (inceleme `1adf54c` üzerinde yapılmıştı).
- **Bu yanıtın baktığı commit:** `5949d98` (modül ayarında `none` değeri ve banka kapalıyken IBAN gizleme bu commit'te geldi).
- **Yöntem:** Bulgular `ARCHITECTURE.md` ve koda karşı tek tek doğrulandı. Kabul edilen düzeltmeler `ARCHITECTURE.md`'ye işlendi (§16).

## Özet

Yerleşim kararı (okul başına ayrı uygulama + veritabanı) ve "asıl iş filo yönetimi" tespiti konusunda ChatGPT ile hemfikirim. Ancak altı bulgudan yalnızca ikisi gerçek P1. Bazı öneriler bu ölçek için ağır. En önemli ek yorum (geri yükleme provasında dış yan etkiler) ise listenin dışında kalmış. ChatGPT'nin hiç değinmediği bir risk de var: platformun silme yetkisi.

## Bulgu bulgu

| # | Bulgu | Hüküm | Gerekçe | Sonuç |
|---|---|---|---|---|
| 1 | İzolasyon iddiası fazla | Kısmen haklı. P1 değil, P2 | "Pratikte yok" ifadesi abartılıydı. Ortak `abdera-edge` ağında A okulunun API'si B okulunun API'sine ulaşabiliyor. Ama B'nin API'si zaten Caddy üzerinden internete açık, yani iç ağ yeni bir saldırı yüzeyi açmıyor. Açabileceği tek durum: API bu ağdan gelen isteğe ayrıca güveniyorsa, örneğin `X-Forwarded-For` ile hız sınırını atlatmak ya da kurulum ucuna jetonsuz ulaşmak. Kurulum ucu jetonlu. | İfade düzeltildi. Forwarded-header güveni yalnızca Caddy'nin adresine kısıtlanır. Caddy her okulun ayrı ağına bağlanır, okul servisleri birbirini görmez. İzolasyon testi DB + API + web'i kapsar (§16.1) |
| 2 | Kurulumun hata sözleşmesi eksik | Abartılı. İçinde bir gerçek açık var | Cevabı kaybolmuş bir komutun belirsizliğini idempotentlik zaten çözer: her adım "durumu şuna getir" biçiminde yazılırsa yeniden çalıştırmak güvenlidir. Tek platform sunucusu ve tek `ProvisioningWorker` varken işlem kimliği + süreli sahiplik + geç kalan komutu reddetme gibi dağıtık sistem mekanizmaları gereksiz. **Gerçek açık:** okul başına `flock` paylaşılan Caddy yapılandırmasını korumuyor | Caddy için sunucu düzeyinde kilit, `caddy validate`, atomik dosya değişimi. Temizlik yalnızca slug'a ait kaynaklara dokunur (§16.2) |
| 3 | Askıya alma ile yedek çelişiyor | Tespit doğru, gerekçe yanlış | Kodda doğrulandı: `BackupService` API içindeki bir `BackgroundService`. API durunca yedek de durur. Ama askıdaki okula yazan kimse yok. Son yedek her şeyi içerir, yeni yedeğe gerek yok. **Asıl risk:** R2'deki 60/65 günlük saklama kuralı, 65 günden uzun askıda kalan okulun **bütün** yedeklerini siler | Askıya alma önce son bir yedek alır. Bu yedek saklama kuralı dışındaki bir öneke kopyalanır. Ayrı bir yedek servisi gerekmez (§16.3) |
| 4 | Sürüm uyumluluğu ve geri dönüş | Haklı. Pilot için P1 değil | Sürüm atlamak şema için sorun değil: EF bekleyen bütün migration'ları sırayla uygular, "en düşük yükseltme sürümü" şimdilik gereksiz. Geri dönüş zaten "migration'lar yalnızca ekleyici" kuralına dayanıyor. Ucuz ve değerli iki öneri alındı | İmajlar digest ile dağıtılır. CI yeni şema üzerinde bir önceki imajla duman testi yapar (§16.4) |
| 5 | Modül kapatmanın anlamı | Haklı | `none` değeri ve banka kapalıyken IBAN gizleme `5949d98`'de geldi. İşçiler, bekleyen bildirimler ve yeniden açma zaten A4/A5'te | Platformun kurduğu okulda `Modules__Enabled` zorunludur. "Hiç yazılmamış" durumu yalnızca eski kurulumda olabilir (§16.5) |
| 6 | Arıza kapasitesi tutmuyor | Tamamen haklı | 2 × 65 = 130. Bir sunucu çökerse kalan 65 okulluk kapasite 100 okulu taşıyamaz. "Kapasite payı" cümlesi yanlıştı | N+1 ile "yeni sunucu + yedekten geri yükleme" seçenekleri ve bekleyen iş kararı yazıldı (§16.6) |

## Ek yorumlar

- **Geri yükleme provası ve dış yan etkiler:** En değerli öneri bu. ChatGPT ise ek yorumda bırakmış. Bekleyen `NotificationJob`'lar gerçek velilere WhatsApp mesajı olarak gidebilir. **P1 kabul edildi** (§16.7).
- **Katalog sürüme bağlı olmalı:** Haklı. Henüz kurulmamış okulun API'si olmadığı için katalog oradan okunamaz. Katalog her sürümle birlikte JSON dosyası olarak yayımlanır (§16.8).
- **Yapılandırmanın sahibi:** Haklı, küçük bir iş. Kural şu: marka ve okul kuralları okul DB'sinde tutulur. Platform yalnızca kurulumda yazar, sonra okur. Ortam değişkenlerinin (modüller, sağlayıcılar) sahibi platformdur (§16.8).
- **"Panelden önce işletim çekirdeği":** Yeni bir öneri değil. F2 (ikinci okul `abdera-host` ile elle kurulur) zaten F3/F4'ten (platform API ve panel) önce geliyor.
- **"Platform arızası okulları durdurmamalı":** Taslak zaten böyle. Okullar platforma bağlı değil, platform yalnızca sayısal telemetri çeker.
- **Ölçek hedefi bir karar kapısı olmalı:** Katılıyorum. Paylaşılan Postgres gibi optimizasyonlar pilot ölçümlerinden sonraya kalır.

## ChatGPT'nin atladığı risk: platformun silme yetkisi

Platformun SSH anahtarı `abdera-host destroy` ile her okulu silebiliyor. Platform ele geçirilirse en kötü senaryo veri sızması değil, bütün okulların silinmesi olur. Bunun için:

- 30 günlük silme beklemesini **ajan** uygular. `destroy` önce okulu `pending-destroy` durumuna alır, kalıcı silme ancak süre dolunca ve ikinci bir komutla yapılır. Platform bu süreyi kısaltamaz.
- Okulların R2 anahtarları yazma + okuma yetkisine sahiptir, silme yetkisi yoktur. Eski yedekleri yalnızca R2'nin nesne kilidi ve saklama kuralı temizler.
- Bu bir P1 olarak kabul edildi (§16.9).

## Sonuç

Gerçek P1'ler: paylaşılan Caddy kilidi, askıdaki okulun yedeğinin silinmesi, geri yükleme provasında dış bağlantıların kapalı olması, platformun silme yetkisinin sınırlanması ve kapasite kararı. Diğerleri P2 ya da zaten fazlarda var.

**Kapasite kararı (kullanıcı, 15 Ekim 2026):** sunucu çökerse platform hiçbir şey yapmaz. Yedek kapasite ya da otomatik taşıma yok. Okullar sunucu geri gelene kadar kapalı kalır, disk kaybında okullar elle yedekten geri yüklenir (`ARCHITECTURE.md` §16.6). Bulgu 6'daki aritmetik hata belgeden düzeltildi; arıza davranışı ise artık bilinçli olarak kabul edilmiş bir risk.
