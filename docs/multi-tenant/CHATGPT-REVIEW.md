# Multi-tenant mimarisi — ChatGPT değerlendirmesi

- **Yazar:** ChatGPT (OpenAI), Codex üzerinden.
- **Tarih:** 15 Ekim 2026.
- **İncelenen branch / commit:** `arch/multi-tenant` / `1adf54c`.
- **Kapsam:** `ARCHITECTURE.md` sürüm 2 ve branch'teki ilk uygulama değişiklikleri.
- **Yöntem:** Statik doküman ve kod incelemesi. Test, yük testi ve canlı ortam doğrulaması yapılmadı.

Bu belge benim mimari yorumlarımı ve önerilerimi içerir; kabul edilmiş ürün kararı veya uygulanmış düzeltme değildir. Taslakta geleceğe bırakılan işleri mevcut üretim hatası olarak değerlendirmiyorum. Ancak bunlar tamamlanmadan hangi güvencelerin verilemeyeceğini belirtiyorum.

## Genel görüşüm

Okul başına ayrı uygulama ve veritabanı seçimini destekliyorum. Kullanıcının bağımsız kurulum isteğine uyuyor, mevcut domain kodunu büyük bir kiracı dönüşümünden koruyor ve okul bazında taşıma ile sürüm yönetimini kolaylaştırıyor. Tek kod tabanı ve her okulda tam şema yaklaşımını da korurdum.

Asıl itirazım, operasyonel karmaşıklığın otomasyonla ortadan kalktığı varsayımı. Karmaşıklık platformun geliştirilmesine, hata durumlarının yönetilmesine ve platformun kendisinin kurtarılmasına taşınıyor. Bu proje aynı zamanda bir filo yönetim sistemi geliştirme işi. Taslağı pilot için makul bir başlangıç sayıyorum; 100 okul için kanıtlanmış bir işletim modeli saymıyorum.

## Bulgular ve öneriler

### 1. P1 — İzolasyon iddiası gerçek güven sınırlarını aşmaktadır

**Dayanak:** `ARCHITECTURE.md` §1, §6.2 ve §11.

Taslak veri sızıntısı riskini “pratikte yok” olarak nitelendiriyor. Oysa API ve web servisleri ortak `abdera-edge` ağına bağlı. DB ağlarının ayrı olması, bir okul uygulaması ele geçirildiğinde diğer okulların API/web servislerine erişimi engellemiyor. Aynı host ve ayrıcalıklı yönetim platformu da ortak güven sınırlarıdır; ayrı container fiziksel izolasyon değildir.

**Önerim:** Caddy'yi okul başına ayrı ağa bağlamak; okul servisleri arasında doğrudan erişimi engellemek; DB yanında API/web erişimini de izolasyon testine dahil etmek. Platform veya host ele geçirilmesini tüm ilgili okulları etkileyebilecek bir senaryo olarak açıkça belgelemek.

**Kabul kanıtı:** Okul A container'ından okul B'nin DB, API ve web servislerine doğrudan bağlantı kurulamaması; A oturumunun B'de geçersiz olması. Bu testlerin host ele geçirilmesine karşı güvence vermediği belirtilmeli.

### 2. P1 — İdempotent kurulumun hata sözleşmesi eksiktir

**Dayanak:** `ARCHITECTURE.md` §3.2, §6 ve §6.1.

Ajan kaynakları oluşturduktan sonra SSH bağlantısı kopabilir. Platform bu durumda komutun uygulanmadığını mı, uygulanıp cevabının kaybolduğunu mu bilemez. Adımları idempotent olarak adlandırmak bu belirsizliği çözmez. Okul başına kilit de farklı okulların ortak Caddy yapılandırmasını eşzamanlı değiştirmesini korumaz.

**Önerim:** Her işlem için kalıcı kimlik ve ajan tarafında sonuç kaydı; istenen durum ile gerçek durumu karşılaştıran uzlaştırma; çalışan iş için süreli sahiplik ve eski çalışanın geç kalan komutlarını reddetme mekanizması. Caddy değişiklikleri host düzeyinde kilitlenmeli, yapılandırma doğrulanıp atomik uygulanmalı. Temizleme yalnızca ilgili işlemin sahip olduğu kaynakları kaldırmalı.

**Kabul kanıtı:** Her dış yan etkinin hemen öncesinde ve sonrasında bağlantı/process kesintisi; yeniden denemede çift kaynak oluşmaması; eşzamanlı iki okul kurulumunda iki yönlendirmenin de korunması.

### 3. P1 — Askıya alma ile yedekleme tasarımı çelişmektedir

**Dayanak:** `ARCHITECTURE.md` §9; `backend/src/Abdera.Api/Modules/Ops/OpsModule.cs` içindeki `BackupService` kaydı.

Taslak API/web durduğunda yedeğin süreceğini söylüyor. Mevcut yedek üreticisi API içinde çalışan bir arka plan servisidir. API durunca bu servis de durur; `backup-gw`'nin ayakta kalması yeni yedek üretmez.

**Önerim:** Yedek üretimini uygulamadan bağımsız bir host işi veya ayrı worker olarak çalıştırmak. Alternatif olarak askıya alma, API'yi durdurmadan erişimi kısıtlayabilir; bu seçenekte bildirim ve diğer işçilerin davranışı ayrıca belirlenmeli.

**Kabul kanıtı:** Askıdaki okul için zamanlanmış yeni bir yedeğin oluşması ve boş bir test veritabanına başarıyla geri yüklenmesi.

### 4. P1 — Geri dönüş ve sürüm sabitleme için uyumluluk sözleşmesi gerekir

**Dayanak:** `ARCHITECTURE.md` §6.1 ve §7.

Yeni sürüm açılışta migration çalıştırıyor; sağlık başarısızsa eski imaja dönülüyor. Taslak şemanın geri dönmediğini kabul ediyor. Ancak sabitlenmiş ve sürüm atlayarak güncellenen okullarda desteklenen geçişler tanımlı değil. Kolon silmeyi iki sürüme yaymak tek başına güvenli geri dönüş garantisi vermez. Sağlıklı HTTP yanıtı da iş akışlarının ve verinin doğru kaldığını kanıtlamaz.

**Önerim:** Desteklenen sürüm aralığı, minimum yükseltme sürümü ve geri dönüş uyumluluğunu release kaydına eklemek; API/web imajlarını kayıtlı digest üzerinden dağıtmak; migration uygulanmış DB üzerinde önceki desteklenen imajı test etmek. DB geri yüklemesi gerekiyorsa yazıların durdurulması ve yedekten sonraki işlemlerin kaybı açıkça yönetilmeli.

**Kabul kanıtı:** Gerçekçi veriyle yükseltme ve eski imaja dönüş sonrası giriş, ders, tahsilat ve bildirim akışlarının doğrulanması. Uyumsuz sürüm geçişinin platform tarafından reddedilmesi.

### 5. P2 — Modül kapatmanın ürün ve uygulama anlamı netleşmelidir

**Dayanak:** `ARCHITECTURE.md` §5.2 ve §15; `Shared/Modules/ModuleCatalog.cs`; `Modules/People/Features/GuardianPortalData.cs` (backend API altında).

İlk uygulama modül uçlarını kapatıyor, ancak modüller arası çağrıların korunması henüz tamamlanmamış. Örneğin banka modülü kapalı olsa da veli aidat cevabı aktif IBAN'ı koşulsuz sorgulayıp döndürüyor. Ayrıca boş veya eksik `Modules__Enabled`, tüm modülleri açıyor. Sihirbazın “hiçbir opsiyon seçilmedi” durumunu boş değerle yazması ters sonuç üretir. Eksik ayar da sessizce özellik açabilir.

**Önerim:** Eksik ayarla açıkça boş seçimi ayırmak; eski kurulum uyumluluğunu açık bir geçiş kuralıyla sağlamak. Her modül için menü, okuma, yazma, işçi, bekleyen işler ve dış entegrasyon davranışlarını tanımlamak. Geçmiş veriyi göstermeye devam etmek bazı modüllerde doğru olabilir, ancak bu bilinçli ürün kararı olmalı.

**Kabul kanıtı:** Sadece endpoint 404 testleri değil, modül kapalıyken veli portalı ve dashboard cevapları, bekleyen bildirimler ve yeniden açma akışları da doğrulanmalı.

### 6. P2 — Arıza kapasitesi hesabı mevcut sayılarla tutmamaktadır

**Dayanak:** `ARCHITECTURE.md` §10.

Belge sunucu başına yaklaşık 65 okul kapasitesi hesaplıyor, ardından 100 okulun iki sunucuya yerleşip biri çökerse diğerine taşınabileceğini belirtiyor. Kalan tek sunucunun belirtilen kapasitesi 100 okulu karşılamıyor. Bellek tahminleri ayrıca disk, I/O, eşzamanlı migration ve yedekleme yükünü kapsamıyor.

**Önerim:** Normal işletim kapasitesi ile bir host kaybedildiğindeki kapasiteyi ayrı planlamak. Kabul edilebilir kesinti süresi ve veri kaybı penceresini belirlemek; buna göre hazır yedek kapasite veya yeni host açma ve geri yükleme planı seçmek. Bellek rezervini ölçülmüş yük profiliyle doğrulamak.

**Kabul kanıtı:** Bir host tamamen kaybedilmiş kabul edilerek okulların hedef süre içinde başka host'a geri yüklenmesi ve birleşik yükün ölçülmesi.

## Ek mimari yorumlarım

- **Panel öncesinde işletim çekirdeği:** İlk yatırımım dokuz adımlı sihirbaz değil, iki test okulunu kurabilen, yükseltebilen ve kurtarabilen ajan olurdu. Panel, bu doğrulanmış işlemleri görünür ve kullanılabilir hale getirmeli.
- **Platform arızası okulları durdurmamalı:** Okullar son geçerli ayarlarıyla platformdan bağımsız çalışmalı. Yönetim platformunun kendi DB'si ve şifreleme anahtarları için birlikte kurtarma tatbikatı yapılmalı; anahtar yedeğinin var olması yeterli değil.
- **Yapılandırma sahipliği açık olmalı:** Marka okul DB'sinde kaynak kabul ediliyor ama platformdan da değiştirilebiliyor. Platform eski kopyayı yeniden iterek okul yöneticisinin değişikliğini ezmemeli. Hangi ayarın sahibi kim, sürümü ne ve çatışma nasıl çözülür tanımlanmalı.
- **Katalog sürüme bağlı olmalı:** Farklı sürümlerdeki okulların modül yetenekleri aynı olmayabilir. Platform kurulacak hedef sürümün kataloğunu bilmek zorunda; henüz kurulmamış okulun API'sini katalog kaynağı olarak kullanamaz. Sürümle birlikte yayımlanan bir yetenek bildirimi bunu çözebilir.
- **Yedek geri yükleme dış yan etkileri de kapsamalı:** Test okuluna geri yüklenen DB'de bekleyen gerçek bildirimler bulunabilir. Kurtarma provası varsayılan olarak e-posta, WhatsApp ve banka bağlantıları kapalı başlamalı; aksi halde test gerçek kişilere mesaj gönderebilir.
- **Ölçek hedefi karar kapısı olmalı:** Önce 3–5 okulda işletim yükü ve kurtarma süreleri ölçülmeli. Paylaşılan Postgres gibi optimizasyonları bu ölçümler gerektirdiğinde değerlendirirdim; sırf 100 okul varsayımıyla başlangıç mimarisine eklemezdim.

## Önerdiğim ilerleme sırası

1. İki test okulu için ağ, sır ve oturum izolasyonunu kanıtla.
2. Kesilip tekrar başlatılabilen kurulum ve sahiplik kontrollü temizlemeyi tamamla.
3. Uygulamadan bağımsız yedekleme ve başka host'a güvenli geri yüklemeyi doğrula.
4. Sürüm uyumluluğu, yükseltme ve geri dönüş testlerini tamamla.
5. Modül davranış sözleşmelerini ve yapılandırma sahipliğini netleştir.
6. Paneli bu işlemler üzerine kur; 3–5 okulluk pilotta kapasite ve operasyon yükünü ölç.

Benim değerlendirmem: temel yerleşim kararı korunabilir. Üretime hazırlık için öncelik daha fazla ekran eklemekten önce izolasyon, hata sonrası uzlaştırma ve kurtarmanın ölçülebilir biçimde kanıtlanmasıdır.
