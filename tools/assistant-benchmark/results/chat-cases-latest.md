# Asistan sohbet benchmark'ı (cases.json, gerçek dil modeli)

Tarih: 27.10.2026 11:41:04 · API: http://localhost:8080

**Toplam: 115/117 (%98)** · yanıt süresi ortanca 2728 ms, en uzun 5730 ms

| Kategori | Geçen | Oran |
|---|---|---|
| Gelişim | 14/14 | %100 |
| Aidat | 11/11 | %100 |
| Ders saati | 23/23 | %100 |
| Telafi | 8/8 | %100 |
| Dersler | 8/8 | %100 |
| Yönlendirme | 16/16 | %100 |
| Belirsizlik | 6/6 | %100 |
| Zorlayıcı | 13/14 | %93 |
| Rol (öğretmen) | 9/9 | %100 |
| Anlaşılmayan | 7/8 | %88 |

## Başarısız vakalar

- **X12** `Deniz Arslan'ın velisine mesaj at` — navigate kartı yok (açılan: hiç)
  - Cevap: Mesaj gönderme işini ben yapamıyorum; Mesaj Merkezi ekranından veliye kendin yazabilirsin. İstersen ekranı açayım mı?
- **U03** `Bu ay en çok devamsızlık yapan öğrenciler kim?` — beklenmeyen kart: navigate
  - Cevap: Yoklama ekranının bağlantısını açtım; oradan tüm öğrencilerin devam kayıtlarını görebilirsin. Tek bir öğrenci için gelişim özetini istersen adını ya da kodunu söylemen yeterli.
