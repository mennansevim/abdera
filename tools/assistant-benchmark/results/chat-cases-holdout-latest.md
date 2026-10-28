# Asistan sohbet benchmark'ı (cases-holdout.json, gerçek dil modeli)

Tarih: 27.10.2026 12:12:25 · API: http://localhost:8080

**Toplam: 22/25 (%88)** · yanıt süresi ortanca 2712 ms, en uzun 4771 ms

Maliyet (yoğun saat fiyatı, üst sınır): $0.0137 · 25 istek, giriş 247146 token (önbellek 221568), çıkış 3945 · toplam $0.1422

| Kategori | Geçen | Oran |
|---|---|---|
| Görülmemiş | 22/25 | %88 |

## Başarısız vakalar

- **H13** `Ada Çetin'in kaç telafi hakkı kaldı` — makeup kartı yok (açılan: hiç)
  - Cevap: Ada Çetin'in şu an kullanılabilir telafi hakkı görünmüyor. İstersen gelişim bilgilerine birlikte bakalım.
- **H21** `Aydın kardeşlerin gelişimi` — Emir Aydın için progress kartı yok; Bora Aydın için progress kartı yok
  - Cevap: Aydın soyadında iki öğrenci var: Emir Aydın (gitar) ve Bora Aydın (keman). İkisinin de gelişimini açayım mı, yoksa sadece birini mi görmek istersin?
- **H25** `deniz arslan perşembe 17.30 dersi iptal mi` — lessons kartı yok (açılan: hiç)
  - Cevap: Hayır, iptal değil. Deniz Arslan'ın perşembe 17:30 piyano dersi (Ayşe Kaya ile) 29 Ekim ve 5 Kasım'da normal görünüyor; arada tatil de yok.
