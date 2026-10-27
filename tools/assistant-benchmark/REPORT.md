# Asistan benchmark ve simülasyon raporu (27 Ekim 2026)

Asistan iki aşamada ölçüldü. Önce kural tabanlı çözümleyici (AE, AE8), sonra kullanıcı kararıyla
onun yerine gelen dil modeli sohbeti (AE9, DeepSeek). Kural yolu kaldırıldı; bu rapor ikisini de
kayıt için tutar. Koşu ayrıntıları `results/*-latest.md`; çalıştırma [README.md](README.md).

## Sonuç: dil modeliyle sohbet (geçerli tasarım)

| Ölçüm | İlk koşu | Düzeltmelerden sonra |
|---|---|---|
| Sohbet benchmark'ı (`cases.json`, 117) | 96/117 (%82) | 116-117/117 (%99-100), son koşu 116 |
| Görülmemiş set (`cases-holdout.json`, 25) | - | 21-23/25 (%84-92); istem bu sete göre ayarlanmadı |
| Bir aylık simülasyon (ekrandan, gerçek model, 31 işlem) | - | 30/31; tek hata O01 (gelişim kartı açılmadı), istemle düzeltildi ve 3/3 tekrar denendi |
| Yanıt süresi | ortanca 2,5 sn | ortanca 2,3-2,9 sn, en uzun 5 sn |

Model aynı istemle her seferinde aynı cevabı vermez; oranlar koşudan koşuya birkaç vaka oynar.
Görülmemiş setteki başarısızlıkların hiçbiri yanlış işleme yol açmıyor: model ya "hak yok" gibi
doğru bilgiyi kart açmadan veriyor, ya teyit soruyor, ya da kapsamı boş bırakıp kartın
sormasını sağlıyor.

### Dil modelinde bulunan ve düzeltilen hatalar

| # | Hata | Nasıl bulundu | Düzeltme |
|---|---|---|---|
| 1 | **"Kartı açtım" deyip açmıyordu.** Kullanıcı işlemin hazır olduğunu sanıyordu. | R01, R20, D06 | Model kart ya da işlem iddia edip `show_card` çağırmadıysa sunucu bir kez düzelttiriyor (`AssistantChat.ClaimsAction`) |
| 2 | **"24 Kasım dersinden itibaren her hafta çarşamba" 24 Kasım'ı hedef sanıyordu;** program her salıya taşınacaktı. | R23 | İstemde somut örnekler; tarih listesi 14 günden 42 güne |
| 3 | **Taşınacak dersin gününü karta koymuyordu** ("cumartesi dersini"). | R08, R13 | Araç tanımında ve istemde zorunlu kılındı |
| 4 | **Takma adı kullanıcıya sızdırıyordu** ("Onur Koç bir öğretmen kodu"). Hitap sen/siz karışıktı. | Elle deneme | İstem: kod/takma addan söz etme; hep "sen" |
| 5 | **Seçenek sunarken öğrenci adlarını yazmıyordu** ("biri gitar, diğeri keman"). | A02 | İstem: seçenekte her öğrenciyi koduyla (ekranda adıyla) yaz |
| 6 | **Yeni saat söylenmediyse kart yerine soru soruyordu;** kart zaten boş saat öneriyor. | R10, R18, R22 | İstem: hedef yoksa da kartı aç |
| 7 | **"Ders notu nasıl yazılır" için Yoklama ekranını açıyordu** (doğrusu Gelişim). | N06 | Ekran listesine her ekranın anahtar kelimeleri eklendi |
| 8 | **Bilgi istendiğinde kartı her zaman açmıyordu.** | Simülasyon O01 | İstem: "getir, göster, listele" ve öğrenci sorularında kart zorunlu |
| 9 | Araç sonuçları Türkçe harfleri `ı` gibi kaçış dizisiyle gönderiyordu (gereksiz token). | Entegrasyon testi | Rahat JSON kodlaması |

Benchmark beklentilerinden bazıları kural döneminden kalmaydı ve modelin cevabı daha doğruydu
(Lara'nın perşembe dersi yokken "perşembe dersini taşı", "dersini 8'e al" belirsizliği, "velisine
mesaj at" için Mesaj Merkezi); bu vakalar düzeltildi, belirsiz ad vakaları `choice: true` ile
seçim sorulmasını bekliyor.

### Gizlilik (KVKK) kontrolü

Entegrasyon testi, modele giden her mesajda (istem, kullanıcı cümlesi, araç sonuçları, öğretmen
notları) gerçek adların geçmediğini doğrular (`AssistantFlowTests`). Ortak soyad ("Arslan") tek
başına bir kişiyi belirtmediği için maskelenmez. Notların ve devam bilgisinin içeriği sağlayıcıya
gider; aydınlatma metninde yurt dışı aktarım belirtilmeli.

## Kayıt: kural tabanlı çözümleyici (kaldırıldı)

Kural yolu 116 cümlede %86'dan 117/117'ye çıkarıldı, görülmemiş sette 24/25 idi; bir aylık
simülasyonda 31/31. Bulunan hatalar (öğretmen adının öğrenci sanılması, "cumaya al"ın yanlış
haftaya gitmesi, hatırlatma kartının yanlış neden göstermesi, tatildeki planlı dersin "ders yok"
denmesi, tarih ve göreli kaydırma eksikliği) ilk dördü arayüzde ve araçlarda hâlâ geçerli olarak
korunuyor; ayrıştırma kuralları dil modeliyle birlikte kalktı.

## Asistanın dışında kalan bulgular (düzeltilmedi)

- **Tatil eklemek o günün derslerini iptal etmiyor.** `POST /api/school-calendar-days` yalnızca
  kaydı ekler; önceden üretilmiş dersler takvimde kalır. Ürün kararı gerekir.
- **Örnek veri (yalnız geliştirme):** `DevelopmentMockData` yoklamayı doğrudan "Mazeretli" yazıyor,
  telafi hakkı açmıyor. Lara Arslan'ın pazartesi 16:00 dersi, öğretmeninin 17:00-21:00 müsaitlik
  penceresinin dışında.

## Bilinen eksikler

- Analitik sorular ("en çok devamsızlık yapan kim?") için araç yok; model veriyle cevap veremez,
  bunu söyler.
- Öğretmenin öğrencilerini listeleyen bir araç yok ("Onur Koç'un öğrencileri kim?").
- Simülasyon örnek verinin Kasım 2026 derslerine bağlı; o tarihten sonra tarihler güncellenmeli.
