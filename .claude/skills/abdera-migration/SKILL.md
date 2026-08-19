---
name: abdera-migration
description: Abdera backend'inde yeni bir EF Core migration oluşturur ve kritik kısıt/index/geri-alınabilirlik kontrol listesini uygular. Kullanılacak — "yeni migration ekle", "tabloya kolon ekle", "şema değiştir" gibi isteklerde.
---

# Abdera Migration

Master prompt Flyway öneriyor; Abdera EF Core Migrations kullanıyor (`CLAUDE.md` — stack kararı). Bu skill her migration'da unutulan kısıtları tekrar hatırlatmak için var.

## Önce oku

- `docs/08-migrations.md` — mevcut migration sırası ve hangi numaranın hangi modüle ait olduğu
- `docs/03-erd.md` — yeni/değişen tablonun tam kolon listesi ve kısıtları
- Yıkıcı bir değişiklikse (kolon/tablo silme): `docs/08-migrations.md`'deki "expand/contract" kuralı

## Adımlar

1. `dotnet ef migrations add <AçıklayıcıAd>` — ad İngilizce, `docs/08-migrations.md`'deki numaralandırma sırasına uygun (`0NN_modül_açıklama` deseninden esinlenerek).
2. Oluşan migration dosyasında kontrol et:
   - [ ] `created_at`, `updated_at` mutasyona açık her yeni tabloda var mı?
   - [ ] Dışa açık id `uuid` mi (sıralı int değil)?
   - [ ] Para kolonu `numeric(12,2)` + ayrı `currency` mi (`double`/`float` değil)?
   - [ ] Zaman kolonu `timestamptz` mi?
   - [ ] `docs/03-erd.md`'deki `UNIQUE`/`CHECK` kısıtları migration'a girdi mi? (özellikle: `notification_jobs (type, reference_type, reference_id)`, `whatsapp_webhook_events (provider_event_id)`, `receivables (enrollment_id, period)`, `lessons (lesson_series_id, start_at)`)
   - [ ] Sık sorgulanacak filtre/join kolonlarında index var mı (örn. `lessons.start_at`, `receivables.status`, `notification_jobs.status, scheduled_at`)?
3. `Down()` metodunun gerçekten geri aldığını yerel veritabanında test et (`dotnet ef database update <öncekiMigration>`).
4. Yıkıcı işlemse (kolon/tablo silme), bunu ayrı bir migration'da yap ve `docs/08-migrations.md`'ye not düş — aynı migration'da hem ekleme hem silme yapma.
5. Seed/referans veri değişikliğiyse (`docs/08-migrations.md` — 009), var olan satırları güncellerken `ON CONFLICT DO NOTHING`/`DO UPDATE` kullan, tekrar çalıştırılabilir olsun.
6. `docs/08-migrations.md`'yi yeni migration'ı yansıtacak şekilde güncelle.

## Yapılmayacaklar

- Üretim verisi olan bir kolonu tek adımda silmek (expand/contract kuralına uy)
- Finansal/audit tablosunda satır silen bir migration yazmak — bunlar asla silinmez, durum kolonu kullanılır (`CLAUDE.md`)
- Kısıtı kod tarafında (`if` kontrolü) bırakıp veritabanına yazmamak — race condition'a açık kapı bırakır
