# Asistan benchmark'ı ve bir aylık simülasyon

Abdera Asistanı'nın (docs/10-decisions.md AE, AE9) yerelde, **gerçek dil modeliyle** sınanması.
Üç parça:

| Parça | Dosya | Neyi ölçer |
|---|---|---|
| Sohbet benchmark'ı | `cases.json` (117 vaka) + `run-chat.mjs` | Her cümle `POST /api/assistant/chat`'e tek mesaj olarak gider; modelin açtığı kart (tür, öğrenci, gün/saat, kapsam, ekran) ve rol izni beklenenle karşılaştırılır |
| Görülmemiş set | `cases-holdout.json` (25 vaka) | Aynı ölçüm; istem bu sete göre **ayarlanmaz**, genelleme oranını gösterir |
| Bir aylık simülasyon | `frontend/e2e/assistant-month.sim.ts` (31 işlem) | Yöneticinin asistanla 4 hafta boyunca yaptığı işlemler (ekrandan, gerçek model); sonuç API'den doğrulanır |

Her vakanın beklenen sonucu (`expected`) dosyanın içinde yazılı; koşu sonuçları `results/` altına
`*-latest.md` olarak düşer. Bulgular ve düzeltmeler: [REPORT.md](REPORT.md).

Dil modeli her seferinde birebir aynı cevabı vermez: aynı istemle oranlar koşudan koşuya birkaç
vaka oynar. Bir değişikliği değerlendirirken iki koşuya bak.

## Yerel ortam

Simülasyon veriyi değiştirir; **yalnızca geçici bir veritabanında** çalıştır.

```bash
docker run -d --name abdera-asistan-bench -e POSTGRES_USER=abdera -e POSTGRES_PASSWORD=bench -e POSTGRES_DB=abdera -p 127.0.0.1:55499:5432 postgres:16-alpine
```

API'yi geliştirme modunda, şifresiz girişle ve sohbet anahtarıyla başlat (`backend/src/Abdera.Api`
içinde; ilk yönetici için `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` da ver). Anahtar
`.env`'deki `Ai_Chatbot_ApiKey`; ortama aktarılması yeterli, değeri yazdırma:

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:8080 "ConnectionStrings__Default=Host=localhost;Port=55499;Database=abdera;Username=abdera;Password=bench" Auth__DevLogin__Enabled=true Frontend__Origin=http://localhost:3001 Database__AutoMigrate=true dotnet run --no-launch-profile
```

Başlangıç durumu (örnek okul + ücret tarifesi + Ekim aidatları; 3 ödenmiş, 1 kısmi; Lara Arslan
ve Emir Aydın'ın birer telafi hakkı):

```bash
node tools/assistant-benchmark/seed.mjs
```

## Çalıştırma

Her vaka bir model çağrısıdır (2-5 sn); 117 vaka yaklaşık 5 dakika sürer.

```bash
node tools/assistant-benchmark/run-chat.mjs
```

```bash
node tools/assistant-benchmark/run-chat.mjs --cases cases-holdout.json
```

Yalnızca bir kategori (ör. ders saati vakaları R ile başlar):

```bash
node tools/assistant-benchmark/run-chat.mjs --only R
```

Simülasyon frontend'i 3001'de ister (`npm run dev -- -p 3001`). İkisi de `frontend` klasöründe:

```bash
npx playwright test --config playwright.assistant-month.config.ts
```

Simülasyonu tekrar koşmadan önce veritabanını sıfırla (container'ı silip yeniden aç, `seed.mjs`).
Tarihler bugüne göre hesaplanır; birkaç işlem örnek verinin 4 Kasım-26 Kasım 2026 aralığındaki
derslerine bağlıdır (seriler 24 Kasım'da biter).
