# Asistan benchmark'ı ve bir aylık simülasyon

Abdera Asistanı'nın (docs/10-decisions.md AE) yerelde sınanması. Üç parça:

| Parça | Dosya | Neyi ölçer |
|---|---|---|
| Çözümleyici benchmark'ı | `cases.json` (117 vaka) + `run-resolve.mjs` | `POST /api/assistant/resolve`: niyet, eşleşen öğrenciler, gün/saat, kapsam, ekran, rol izni |
| Görülmemiş set | `cases-holdout.json` (25 vaka) | Aynı ölçüm; kurallar bu sete göre **ayarlanmaz**, genelleme oranını gösterir |
| Bir aylık simülasyon | `frontend/e2e/assistant-month.sim.ts` (31 işlem) | Yöneticinin asistanla 4 hafta boyunca yaptığı işlemler; sonuç API'den doğrulanır |

Her vakanın beklenen sonucu (`expected`) dosyanın içinde yazılı; koşu sonuçları `results/` altına
`*-latest.md` olarak düşer. Bulgular ve düzeltmeler: [REPORT.md](REPORT.md).

## Yerel ortam

Simülasyon veriyi değiştirir; **yalnızca geçici bir veritabanında** çalıştır.

```bash
docker run -d --name abdera-asistan-bench -e POSTGRES_USER=abdera -e POSTGRES_PASSWORD=bench -e POSTGRES_DB=abdera -p 127.0.0.1:55499:5432 postgres:16-alpine
```

API'yi geliştirme modunda, şifresiz girişle başlat (`backend/src/Abdera.Api` içinde; ilk yönetici
için `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` da ver):

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:8080 "ConnectionStrings__Default=Host=localhost;Port=55499;Database=abdera;Username=abdera;Password=bench" Auth__DevLogin__Enabled=true Frontend__Origin=http://localhost:3001 Database__AutoMigrate=true dotnet run --no-launch-profile
```

Başlangıç durumu (örnek okul + ücret tarifesi + Ekim aidatları; 3 ödenmiş, 1 kısmi):

```bash
node tools/assistant-benchmark/seed.mjs
```

## Çalıştırma

```bash
node tools/assistant-benchmark/run-resolve.mjs
```

```bash
node tools/assistant-benchmark/run-resolve.mjs --cases cases-holdout.json
```

Simülasyon frontend'i 3001'de ister (`npm run dev -- -p 3001`). İkisi de `frontend` klasöründe:

```bash
npx playwright test --config playwright.assistant-month.config.ts
```

Simülasyonu tekrar koşmadan önce veritabanını sıfırla (container'ı silip yeniden aç, `seed.mjs`).
Tarihler bugüne göre hesaplanır; birkaç işlem örnek verinin 4 Kasım-26 Kasım 2026 aralığındaki
derslerine bağlıdır (seriler 24 Kasım'da biter).
