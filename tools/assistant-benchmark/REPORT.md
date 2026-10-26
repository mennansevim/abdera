# Asistan benchmark ve simülasyon raporu (27 Ekim 2026)

Kural yolunun (docs/10-decisions.md AE) yerelde, gerçek API ve örnek okul verisiyle sınanması.
Koşu ayrıntıları `results/*-latest.md` dosyalarında; nasıl çalıştırılacağı [README.md](README.md).

## Sonuç

| Ölçüm | İlk koşu | Düzeltmelerden sonra |
|---|---|---|
| Çözümleyici benchmark'ı (`cases.json`) | 100/116 (%86) | 117/117 (%100) |
| Görülmemiş set (`cases-holdout.json`, düzeltmelerden sonra yazıldı) | - | 24/25 (%96) |
| Bir aylık simülasyon (arayüz + gerçek API) | 20/25 | 31/31 |
| Ortalama çözümleme süresi | 3 ms | 3-4 ms |

Ana set düzeltmelerin hedefi olduğu için %100'ü iyimserdir; kuralların yeni ifadelere ne kadar
genellendiğini görülmemiş setteki %96 gösterir.

## Bulunan ve düzeltilen hatalar

| # | Hata | Nasıl bulundu | Düzeltme |
|---|---|---|---|
| 1 | **Öğretmen adı öğrenci sanılıyordu.** "Onur Koç'un dersleri" → öğrenciler Aras ve Lina Koç; "Burak Eren'in dersleri" → Defne Eren. Yanlış öğrencinin dersine dokunma riski. | X01-X03, simülasyon O13 | Ad + soyadıyla geçen öğretmen kelimeleri öğrenci eşlemesinden çıkarılır (`StudentNameMatcher.FullNameIndexes`, `ResolveIntent`) |
| 2 | **"Cumaya al" yanlış haftaya gidiyordu.** "Ada'nın 4 Kasım dersini cumaya al" 6 Kasım yerine 30 Ekim'e taşınıyordu. | Simülasyon O15 | Gün adı hedefleri taşınan dersin haftasına çözülür (`LessonMove.TargetWeekday`, `reschedule-flow.tsx`) |
| 3 | **Hatırlatma kartı yanlış neden söylüyordu.** Gönderilmeyen hatırlatma için "velisi kayıtlı değil ya da zaten sırada" deniyordu; asıl en sık neden velinin WhatsApp izni olmaması. | Simülasyon O05 (0/9) | Metin gerçek üç nedeni sayar (`dues-card.tsx`) |
| 4 | **Telafi açılınca başarı mesajı kayboluyordu.** Hak listesi tazelenince kart "hak yok"a dönüyordu. | Tarayıcı denemesi (F4) | Sonuç üst bileşende tutulur (`makeup-flow.tsx`) |
| 5 | **Tatile denk gelen planlı ders için "o gün ders yok" deniyordu.** | Simülasyon O29 | Kart ve ders taşıma akışı, tatile rağmen planlı dersi açıkça söyler |
| 6 | **Belirli bir gün sorulduğunda genel cevap veriliyordu.** "26 Kasım dersini taşı" → "5 haftada ders görünmüyor". | Simülasyon O27/O31 | Cevap sorulan günü anar |
| 7 | **Tarih anlaşılmıyordu:** "4 Kasım dersini", "3 Kasım'a". | R19, R20 | `DayReference` ay + gün; en yakın gelecekteki tarih |
| 8 | **Göreli kaydırma anlaşılmıyordu:** "yarım saat ileri", "bir saat erkene". | R11 | `ShiftMinutes`; dersin kendi saatine göre |
| 9 | Eksik ifadeler: "son durumu", "hakkında bilgi", "X nasıl" (öğrenci varsa gelişim), "ders notları", "ödevleri", "alacağımız", "… olsun", "alacağım", "… istiyorum", "… ekranı", "itibaren". | P08-P13, D09, R06, N14-N16, birim testleri | `IntentParser` anahtar sözcükleri; belirsiz ifade yalnızca öğrenci adı varsa gelişim olur |

Her düzeltme birim ya da entegrasyon testiyle sabitlendi (`AssistantIntentParserTests`,
`StudentNameMatcherTests`, `AssistantFlowTests`).

## Asistanın dışında kalan bulgular (düzeltilmedi)

- **Tatil eklemek o günün derslerini iptal etmiyor.** `POST /api/school-calendar-days` yalnızca
  kaydı ekler; önceden üretilmiş dersler takvimde kalır. Ders üretimi tatili atlıyor, sonradan
  eklenen tatil ise mevcut dersleri etkilemiyor. Ürün kararı gerekir.
- **Örnek veri (yalnız geliştirme):** `DevelopmentMockData` yoklamayı doğrudan "Mazeretli" yazıyor,
  telafi hakkı açmıyor; aynı dersi tekrar Mazeretli işaretlemek değişiklik sayılmadığından hak
  hiç oluşmuyor. Ayrıca Lara Arslan'ın pazartesi 16:00 dersi, öğretmeninin pazartesi 17:00-21:00
  müsaitlik penceresinin dışında.

## Bilinen eksikler

- "yeni bir öğrenci kaydetmem lazım" (H16) yönlendirme sayılmıyor: araya giren kelime ve "lazım".
  Görülmemiş seti anlamlı tutmak için kurala eklenmedi; LLM yolunun işi.
- Analitik sorular ("en çok devamsızlık yapan kim?") ve mesaj taslağı bilerek `Unknown`.
- Simülasyon örnek verinin Kasım 2026 derslerine bağlı; o tarihten sonra koşmak için tarihler
  güncellenmeli.
