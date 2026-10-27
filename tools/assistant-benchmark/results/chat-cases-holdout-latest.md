# Asistan sohbet benchmark'ı (cases-holdout.json, gerçek dil modeli)

Tarih: 27.10.2026 05:11:19 · API: http://localhost:8080

**Toplam: 21/25 (%84)** · yanıt süresi ortanca 2906 ms, en uzun 4472 ms

| Kategori | Geçen | Oran |
|---|---|---|
| Görülmemiş | 21/25 | %84 |

## Başarısız vakalar

- **H09** `Defne Eren'in dersi bu hafta 19:00'da olsun` — scope Unspecified (beklenen Once)
  - Cevap: Defne Eren'in pazartesi 18:00 dersini bu hafta 19:00'a almak için kartı açtım; onaylarsan taşınacak.
- **H13** `Ada Çetin'in kaç telafi hakkı kaldı` — makeup kartı yok (açılan: hiç)
  - Cevap: Ada Çetin'in şu an kullanılabilir telafi hakkı görünmüyor.
- **H21** `Aydın kardeşlerin gelişimi` — Emir Aydın için progress kartı yok; Bora Aydın için progress kartı yok
  - Cevap: Aydın soyadında iki öğrenci var: Emir Aydın (gitar) ve Bora Aydın (keman). Hangisinin ya da ikisinin birden gelişimini görmek istersin?
- **H25** `deniz arslan perşembe 17.30 dersi iptal mi` — lessons kartı yok (açılan: hiç)
  - Cevap: Hayır, iptal değil. Deniz Arslan'ın perşembe 17:30 piyano dersi (Ayşe Kaya) bu hafta 29 Ekim'de ve sonraki haftalarda görünüyor; yalnızca 19 Kasım'da saat 18:00'e kaymış.
