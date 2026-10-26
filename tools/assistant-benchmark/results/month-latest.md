# Asistan - bir aylık işlem simülasyonu

Tarih: 27.10.2026 01:08:58 · 31/31 işlem beklenen sonucu verdi

| # | Hafta | İstek | Beklenen | Gerçekleşen | Sonuç |
|---|---|---|---|---|---|
| O01 | Hafta 1 | Deniz Arslan'ın gelişimini getir | Devam oranı analiz ekranıyla aynı (%geldi/işaretli, 3 ay); çalıştığı eser görünür | %92, Bach - Minuet in G | ✓ |
| O02 | Hafta 1 | Aidat günü gelenleri listele | Ödenmemiş + kısmi ödenmiş aidatlar (ödenmişler yok), toplam kalan tutar doğru | 9 aidat, ₺52.000 | ✓ |
| O03 | Hafta 1 | Deniz Arslan'a aidat hatırlatması (karttan, onaylı) | Onaydan sonra 1 hatırlatma sıraya alınır | 1 hatırlatma sıraya alındı | ✓ |
| O04 | Hafta 1 | Deniz Arslan'ın aidat durumu → tekrar hatırlatma | Aynı aidat için ikinci hatırlatma gönderilmez, "atlandı" denir | 0/1, gönderilmedi | ✓ |
| O05 | Hafta 1 | Vadesi geçenlere toplu hatırlatma | Yalnızca WhatsApp izni veren velilere ve henüz hatırlatılmamış aidatlara gider (Deniz'inki O03'te gitti); gönderilmeyenlerin nedeni söylenir | 0/9 (izinli veli sayısına göre) | ✓ |
| O06 | Hafta 1 | Defne Eren'den ödeme al (karttaki bağlantı → Aidatlar) | Aidatlar'daki ödeme penceresi açılır, tam tutar alınınca aidat Ödendi olur | Defne Eren Paid | ✓ |
| O07 | Hafta 1 | Aidat günü gelenleri listele (tahsilattan sonra) | Defne Eren listeden düşer | Defne Eren listede yok | ✓ |
| O08 | Hafta 1 | Lara Arslan'ın pazartesi dersini 17:00'ye al (yalnız bu ders) | 2 Kasım Pazartesi 16:00 dersi 17:00'ye taşınır; eski ders "taşındı" olur | 2 Kas Pzt 16:00 Rescheduled, 2 Kas Pzt 17:00 Normal | ✓ |
| O09 | Hafta 1 | Lina Koç'un cuma dersini salı 17:30'a al | Mert Yılmaz salı 17:30'da Emir Aydın'la dolu: çakışma uyarısı, onay yok, takvim değişmez | çakışma uyarısı, değişiklik yok | ✓ |
| O10 | Hafta 1 | Mina Şahin'in dersini her hafta perşembe 16:00'ya taşı | 30 Ekim Cuma dersinden itibaren program her perşembe 16:00; o tarihten sonra cuma dersi kalmaz | 5 Kas Per 16:00 Normal, 12 Kas Per 16:00 Normal, 19 Kas Per 16:00 Normal | ✓ |
| O11 | Hafta 1 | Ege Kurt'un perşembe dersini yarım saat erkene al | 29 Ekim Perşembe 19:30 dersi 19:00'a alınır | 29 Eki Per 19:00 Normal | ✓ |
| O12 | Hafta 1 | Kerem Aksoy'un geçen salı dersi Mazeretli → Kerem'e telafi oluştur | Telafi hakkı kullanılır, hakkın süresi içinde Makeup ders açılır | seçilen 27 Ekim Salı 17:00; 27 Eki Sal 17:00 Makeup | ✓ |
| O13 | Hafta 1 | Onur Koç'un dersleri | Onur Koç öğretmen: öğrenci Aras/Lina Koç eşleşmez, "Hangi öğrenci için?" sorulur | öğrenci soruldu | ✓ |
| O14 | Hafta 1 | Arslan'ın dersleri → Deniz seç | İki Arslan seçenek olarak gelir; Deniz seçilince her perşembe 17:30 programı görünür | seçim sonrası program görünür | ✓ |
| O15 | Hafta 2 | Ada Çetin'in 4 Kasım dersini cuma 18:00'e al | Aynı haftanın cuması: 4 Kasım 18:00 → 6 Kasım Cuma 18:00 | 4 Kas Çar 18:00 Rescheduled, 6 Kas Cum 18:00 Normal | ✓ |
| O16 | Hafta 2 | 9 Kasım resmi tatil → Lara'nın dersini 9 Kasım'a taşı | Tatil uyarısı, onay yok | tatil uyarısı | ✓ |
| O17 | Hafta 1 | Deniz Arslan'ın perşembe dersini sabah 10'a al | Ayşe Kaya perşembe 16:00–21:00 müsait: müsaitlik uyarısı, onay yok | müsaitlik uyarısı | ✓ |
| O18 | Hafta 1 | Lina Koç'un dersini her hafta salı 17:30'a al | Her salı 17:30 Emir Aydın'la çakışır: uyarı + haftalık boş saat önerileri | uyarı + öneriler | ✓ |
| O19 | Hafta 3 | Mina Şahin'in dersleri | Kart yeni programı gösterir: her perşembe 16:00 | Her hafta Perşembe 16:00 | ✓ |
| O20 | Hafta 1 | Bora'nın dersini bir saat erkene çek | 31 Ekim Cumartesi 19:30 dersi 18:30'a alınır | 31 Eki Cmt 18:30 Normal | ✓ |
| O21 | Hafta 1 | Ada'ya telafi oluştur | Kullanılabilir hak yok mesajı | hak yok | ✓ |
| O22 | Hafta 1 | Kerem Aksoy'un aidat durumu | Kısmi ödenmiş Ekim aidatının kalanı (₺4.000) Ödenmedi olarak görünür | ₺4.000 | ✓ |
| O23 | Hafta 1 | Kerem'in kalan borcunu al (karttaki bağlantı) | Pencere kalan tutarla (₺4.000) açılır, alınca aidat Ödendi | Kerem Aksoy Paid | ✓ |
| O24 | Hafta 1 | Kerem'in borcu var mı | Ödenmemiş aidat yok | borç yok | ✓ |
| O25 | Hafta 1 | Fiyat politikası nerede → ekrana git | Aidatlar ekranı Fiyat politikası sekmesiyle açılır | Fiyat politikası açık | ✓ |
| O26 | Hafta 4 | Deniz Arslan'ın 19 Kasım dersini 18:00'e al | 19 Kasım 17:30 dersi 18:00'e; önceki perşembe değişmez | 12 Kas Per 17:30 Normal, 19 Kas Per 17:30 Rescheduled, 19 Kas Per 18:00 Normal | ✓ |
| O27 | Hafta 4 | Emir Aydın'ın 17 Kasım dersinden itibaren her hafta çarşamba 17:00'ye al | 10 Kasım salı dersi yerinde kalır; 17 Kasım salı dersi kalkar, 18 Kasım'dan itibaren her çarşamba 17:00 | 10 Kas Sal 17:30 Normal, 18 Kas Çar 17:00 Normal | ✓ |
| O28 | Hafta 3 | Lina Koç'un son üç haftadaki iki dersi Mazeretli → Lina'ya telafi oluştur | İki hak listelenir; süresi en erken dolan (eski dersin hakkı) kullanılır, diğeri kalır | eski hak kullanıldı, yeni hak duruyor | ✓ |
| O29 | Hafta 2 | Lara Arslan'ın dersleri (9 Kasım tatil, ders önceden planlı) | Kart "o gün ders yok" demez; tatile rağmen planlı ders olduğunu söyler | tatilde planlı ders uyarısı | ✓ |
| O31 | Hafta 5 | Deniz Arslan'ın 26 Kasım dersini 18:00'e al (seri 24 Kasım'da bitiyor) | Söylenen günü anarak o gün ders olmadığını söyler; onay yok | 26 Kasım'da ders yok mesajı | ✓ |
| O30 | Hafta 5 | Ay sonu: Aidat günü gelenleri listele | Bir ayın tahsilatlarından sonra liste ve toplam API ile birebir aynı | 7 aidat, ₺42.000 | ✓ |
