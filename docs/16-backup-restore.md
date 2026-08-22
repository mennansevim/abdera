# Yedekten Geri Yükleme (Runbook)

Faz 4 (`docs/15-product-phases.md`) bilinçli olarak uygulama içinde bir "geri yükle" düğmesi
**içermez** — geri yükleme mevcut veriyi geri dönülemez şekilde değiştirebilecek bir işlem,
arayüzden kazayla tetiklenebilecek bir riski kabul etmek yerine burada elle, adım adım bir
prosedür olarak tutulur (`docs/10-decisions.md` G9). Bu belge, docs/15'in "örnek geri yükleme
provası" kabul kriterinin nasıl yerine getirileceğini tarif eder.

## Ne zaman kullanılır

- Gerçek bir veri kaybı/bozulma sonrası kurtarma
- Yeni bir sunucuya taşıma
- Periyodik olarak (örn. ayda bir) "yedeğimiz gerçekten işe yarıyor mu" provası

## Adımlar

1. **Şifreli dosyayı indir.** `Backup__Sftp__RemoteDirectory` altındaki `abdera-YYYYMMDD-HHmmss.sql.enc` dosyasını SFTP ile kendi makinene indir.
2. **Şifreyi çöz.** `Backup__EncryptionKey` değerini `.env`'den al, aşağıdaki gibi bir konsol programıyla (veya `dotnet-script`/basit bir test projesiyle) çöz:
   ```csharp
   await BackupEncryption.DecryptFileAsync(
       "abdera-20260823-030000.sql.enc", "restored.sql", "<Backup__EncryptionKey degeri>");
   ```
   (`Abdera.Api.Modules.Ops.Infrastructure.BackupEncryption` - bkz. kaynak kod.)
3. **Ayrı, boş bir veritabanına geri yükle - ASLA canlı veritabanının üzerine değil.**
   ```bash
   createdb abdera_restore_test
   psql -d abdera_restore_test -f restored.sql
   ```
4. **Tutarlılık kontrolü** (docs/15 kabul kriteri - "aidat, ödeme, gider, audit ve mesaj job verileri"):
   ```sql
   -- Satır sayıları yedek alınan zamana göre makul mü?
   SELECT 'receivables', count(*) FROM receivables
   UNION ALL SELECT 'payments', count(*) FROM payments
   UNION ALL SELECT 'expenses', count(*) FROM expenses
   UNION ALL SELECT 'audit_log', count(*) FROM audit_log
   UNION ALL SELECT 'notification_jobs', count(*) FROM notification_jobs;

   -- Rastgele bir Receivable'ın Payment toplamı tutarıyla eşleşiyor mu?
   SELECT r.id, r.amount, r.status, COALESCE(SUM(p.amount), 0) AS total_paid
   FROM receivables r LEFT JOIN payments p ON p.receivable_id = r.id
   GROUP BY r.id, r.amount, r.status
   HAVING r.status = 'Paid' AND COALESCE(SUM(p.amount), 0) < r.amount;
   -- Boş dönmeli - Paid işaretli ama tam ödenmemiş bir kayıt olmamalı.
   ```
5. **Gerçek geri dönüşte** (yalnızca gerçek bir kayıp anında): `abdera_restore_test` yerine
   canlı veritabanı adını kullan, ama önce **mevcut (bozuk) veritabanının da bir anlık pg_dump'ını al**
   (geri yüklemeden önce ne olduğunu kaybetme).
6. **Sonucu kaydet.** `docs/11-progress-log.md`'ye ne zaman, hangi yedek, kaç satır, tutarlılık kontrolünün sonucu.

## Ne sıklıkla prova yapılmalı

Canlıya çıkmadan önce en az bir kez (docs/15 kabul kriteri), sonrasında üç ayda bir önerilir -
yedek dosyasının var olması onun **geri yüklenebilir** olduğunu kanıtlamaz.
