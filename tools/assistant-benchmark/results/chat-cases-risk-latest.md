# Asistan sohbet benchmark'ı (cases-risk.json, gerçek dil modeli)

Tarih: 27.10.2026 12:29:54 · API: http://localhost:8080

**Toplam: 45/46 (%98)** · yanıt süresi ortanca 2598 ms, en uzun 7882 ms

Maliyet (yoğun saat fiyatı, üst sınır): $0.0222 · 54 istek, giriş 453411 token (önbellek 417792), çıkış 7541 · toplam $0.1644

| Kategori | Geçen | Oran |
|---|---|---|
| Ders saati (dil) | 16/17 | %94 |
| Telafi (risk) | 7/7 | %100 |
| Yanlış kişi | 7/7 | %100 |
| Toplu / iptal | 4/4 | %100 |
| Çok adımlı | 8/8 | %100 |
| Rol (öğretmen) | 3/3 | %100 |

## Başarısız vakalar

- **K05** `Bora'nın bu cumartesiki dersini pazar 11'e al` — scope Unspecified (beklenen Once)
  - Cevap: Bora Aydın'ın bu cumartesi dersini pazar 11:00'e taşımak için kartı açtım. Kart dersi ve uygunluğu kontrol ediyor; onaylamadan önce bir göz at.
