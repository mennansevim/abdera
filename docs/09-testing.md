# Test Stratejisi

`CLAUDE.md`'deki kural: gerçek Postgres yalnızca gerçekten gerektiğinde. Testcontainers testi 3–5 saniyeden başlar; her testte kullanılırsa paket dakikalar sürer ve kimse çalıştırmaz olur (`docs/10-decisions.md` C4).

## Birim testleri (xUnit, veritabanı yok)

- Ders üretimi: rolling window (8–12 hafta), idempotency — iki kez çalıştırınca mükerrer satır olmamalı
- Öğretmen uygunluğu / çakışma doğrulaması (öğrenci çakışması, öğretmen çakışması, geçerli süre/aralık)
- `LessonRsvp` durum geçişleri (UNKNOWN → ATTENDING → NOT_ATTENDING)
- `LessonAttendance` kuralları (bir kez girilir, düzeltme audit'e düşer)
- `Receivable` durum hesaplama (`docs/05-state-models.md`)
- Fiyat snapshot kuralı: `PriceList` güncellenince açık `Receivable`'ların **değişmediği**
- `LessonChangeRequest` onay/red kuralları, `PARENT_REJECTED` → sessizce tekrar değiştirmeme
- `MakeupCredit` doğuşu: ≥24 saat önce iptal → kredi; <24 saat / no-show → kredi yok
- WhatsApp buton payload çözümleme ve imza doğrulama (geçerli/geçersiz imza)
- Sessiz saat öteleme mantığı (A6)
- Konuşma penceresi hesaplama (A7)

## Entegrasyon testleri (Testcontainers.PostgreSql — yalnızca bunlar)

1. Migration'lar boş bir veritabanında baştan sona çalışıyor
2. `notification_jobs` üzerindeki `FOR UPDATE SKIP LOCKED` — iki eşzamanlı worker aynı job'ı iki kez işlemiyor
3. Unique kısıt ihlalleri gerçekten reddediyor (`lesson_series_id + start_at`, `type+reference_type+reference_id`, `provider_event_id`, `enrollment_id+period`)
4. Webhook idempotency — aynı `provider_event_id` iki kez POST edilince ikinci seferde iş etkisi tekrarlanmıyor
5. Rol bazlı yetkilendirme — `TEACHER` başka öğretmenin dersine yazamıyor (`403`)

**Ek önkoşul (Faz 4, `Integration/OpsFlowTests.cs`):** `BackupService` gerçek `pg_dump` çalıştırıyor (`Backup:Provider=Fake` yalnızca depolama/e-postayı sahteler, veritabanı dökümü her zaman gerçek). Bu testlerin çalıştığı makinede/CI runner'ında `pg_dump` PATH'te olmalı — yerel geliştirmede genelde `brew install libpq`/`postgresql` ile gelir, CI'da `.github/workflows/ci.yml`'e açıkça kurma adımı eklendi.

## Uçtan uca testler (master prompt'un asgari listesi)

1. Yönetici öğrenci, veli, öğretmen ve ders serisi oluşturur
2. Sistem bir `Lesson` ve bir hatırlatma `NotificationJob` üretir
3. WhatsApp RSVP'si, sağlayıcı olayı tekrar gönderse bile **bir kez** kaydedilir
4. Öğretmen yoklama işaretler ve not ekler
5. Yönetici bir ödeme kaydeder ve `Receivable` durumu değişir
6. Bir ders-değişikliği talebi onaylanır ve bildirim oluşturulur

Bu 6 senaryo CI'da (`.github/workflows/ci.yml`, `backend-build-test` job'ı) gerçekten çalışıyor - denetim OPS-1 (`docs/13-audit-fix-prompt.md` madde 14) ile açıldı.

## Phase 5 notları (uygulandıktan sonra eklendi)

- Yukarıdaki listenin tamamı `Unit/MessagingDomainTests.cs` (27 test) ve `Integration/MessagingFlowTests.cs` (12 test, Testcontainers) ile karşılanıyor — `docs/11-progress-log.md`'de ayrıntılı liste.
- **Bilinçli boşluk — `FOR UPDATE SKIP LOCKED`'ın gerçek eşzamanlı iki-worker senaryosu testlenmedi.** Bu ölçekte (`CLAUDE.md` — 6–8 öğretmen, tek `NotificationDispatcher` instance'ı, `docs/10-decisions.md`'nin "mikroservis/Kubernetes yok" kararı) birden fazla worker instance'ı hiç çalışmıyor; `SKIP LOCKED` yalnızca aynı instance içindeki teorik bir yarışa karşı savunma. Sorgunun kendisi (`SELECT ... FOR UPDATE SKIP LOCKED`) `MigrationTests.cs`'in de doğruladığı gibi gerçek Postgres'e karşı çalışıyor (`Dispatcher_sends_a_due_job_through_fake_client_and_marks_it_sent` testi bunu dolaylı doğruluyor - sorgu sözdizimi hatalıysa test de patlardı).
- **Bilinçli boşluk — sessiz saat (A6) dispatch-anı davranışı yalnızca birim testli.** `IClock` gerçek `SystemClock` olduğu için entegrasyon testinde "şu an sessiz saat içinde" durumunu deterministik kuramıyoruz - saf fonksiyonlar (`QuietHours.IsWithinQuietHours`/`ResolveSendTime`) ayrı ayrı birim testli, dispatcher'daki çağrı tek satırlık düz bir if.
- **Yeni öğrenilen kural — globalizasyon/Alpine bug'ları yerel testle yakalanamaz.** `docker compose up` ile Alpine container'ında canlı doğrulama, bu sınıf bug için testin yerini tutmuyor, tamamlıyor. Ayrıntı: `CLAUDE.md` "Kullanıcıya gösterilecek metinde `new CultureInfo(...)` kullanıyorsan Dockerfile'ı kontrol et".

## Phase 6 notları — Banking (E1)

`Unit/BankingDomainTests.cs` (13 test): `VirtualIban`/`BankIncomingTransaction` durum makinesi (çift eşleşme reddi, `Ignore`'un `Matched`'ten sonra reddi), `PaymentMatcher` (docs/12-bank-integration.md algoritmasının tamamı — tek net eşleşme, birden fazla eşleşme belirsiz kalır, açıklama-dönem eşleşmesi amount-only'e önceliklidir, açıklama eşleşmesi kalan bakiyeyi karşılamıyorsa reddedilir). `Integration/BankingFlowTests.cs` (5 test, Testcontainers): tek aktif sanal IBAN kısıtı, net tutar eşleşmesi otomatik `Payment` oluşturup `Receivable`'ı günceller (`CreatedBy=null`), belirsiz tutar hiçbir `Receivable`'a dokunmadan `NeedsReview`'da kalır, admin elle çözebilir (`CreatedBy`=admin), aynı `provider_transaction_id` iki kez işlenmez.

**En önemli test:** `Incoming_transaction_with_ambiguous_amount_stays_needs_review_and_does_not_touch_receivable` — bu, docs/10-decisions.md E1'in "belirsizlikte otomatik davranma" kararının gerçekten uygulandığını doğrulayan test. Bu testin kırılması, para yanlış hesaba işlenebilir demektir.

## Faz 4 notları — Sağlık, yedekleme, e-posta alarmı

`Unit/OpsDomainTests.cs` (10 test): `BackupEncryption` round-trip (AES-GCM şifrele/çöz, yanlış anahtarla `AuthenticationTagMismatchException`), `SystemHealthMonitor.Evaluate` (saf karar fonksiyonu - DB down/hiç yedek yok/son yedek başarısız/yaşa göre kademeli Healthy→Degraded→Unhealthy), `SystemHealthStatus.ShouldSendAlert` (soğuma süresi/cooldown mantığı). `Integration/OpsFlowTests.cs` (4 test, Testcontainers): manuel tetiklemenin gerçek `pg_dump` + şifreleme + `FakeBackupStorage` ile başarılı bir `BackupRun` ürettiği, liste uç noktasının sayfalandığı/en yeniden eskiye sıralandığı, sağlık uç noktasının DB'deki durumu doğru yansıttığı, admin olmayan isteklerin `401` aldığı.

**`docker compose`/gerçek Docker imajıyla canlı doğrulama (bu oturumda yapıldı):** `postgresql16-client` paketinin gerçek Alpine imajına eklendiği doğrulandı (`docker exec ... pg_dump --version` → 16.15). Gerçek admin oturumuyla `/api/backup-runs/trigger` çağrıldı → gerçek `pg_dump` (55KB'lık gerçek bir dökümü) çalıştı → AES-GCM ile şifrelendi → `FakeBackupStorage`'a "yüklendi" → `BackupRun.Succeeded` olarak kaydedildi, tüm bunlar ~1 saniyede. `SystemHealthMonitor`'ın bir sonraki turda durumu otomatik `Healthy`'ye çektiği, uyarı e-postasının (`Ops__AlertRecipients` doluyken, eşikler geçici olarak sıfırlanarak) gerçekten `FakeEmailSender` üzerinden gönderildiği ve panoda kırmızı/yeşil şeridin doğru göründüğü tarayıcıda teyit edildi.

**Bilinçli boşluk — gerçek SFTP sunucusuna karşı `SftpBackupStorage` bu oturumda test edilmedi.** Kullanıcı henüz kendi sunucusunun bağlantı bilgilerini (host/kullanıcı/anahtar) paylaşmadı; `FakeBackupStorage` ile tüm akış (pg_dump → şifreleme → "yükleme" → retention → health) uçtan uca doğrulandı, yalnızca gerçek ağ transferi (SSH.NET → gerçek sunucu) doğrulanmadı. Kullanıcı sunucu bilgilerini `.env`'e (`Backup__Sftp__*`) girip `Backup__Provider=Sftp` yaptığında bu adım ayrıca doğrulanmalı.

## OPS-1 — CI (denetim, `docs/13-audit-fix-prompt.md` madde 14)

Altı fazdır `.github/workflows/ci.yml`'de yalnızca `guard-secrets` (`.env` commit kontrolü) çalışıyordu; `backend-build-test` (`dotnet restore/build/test`) yorum satırında kalmıştı - 156 testin koruma değeri yalnızca elle `dotnet test` çalıştırmaya bağlıydı, bir PR testleri kırsa CI bunu yakalamıyordu.

- `backend-build-test` job'ı açıldı. Testcontainers.PostgreSql kendi Postgres container'ını doğrudan Docker daemon'ı üzerinden başlatıyor (`AbderaWebApplicationFactory`) - `ubuntu-latest` runner'ında Docker zaten hazır çalıştığından ayrı bir `services: postgres` bloğuna **gerek yok**, eklenmedi (ikisi aynı iş için çakışan/gereksiz bir ikinci Postgres olurdu).
- Birim/entegrasyon testleri tek `dotnet test` çağrısında birlikte kalıyor (ayrı job'lara bölünmedi) - suite yerel olarak ~15-25 saniyede bitiyor, bu ölçekte ayırmanın getirisi yok.
- `frontend-build-lint` **hâlâ yorumda** - `npm run lint` şu an `banking/page.tsx`'teki önceden var olan (bu denetimin kapsamı dışında, ayrı bir görev olarak flag'lenmiş) bir `react/no-unescaped-entities` hatasından dolayı kırmızı döner. O düzeltilmeden açılırsa CI ilk çalıştığı andan itibaren kırmızı kalır - o yüzden düzeltme ayrı görev tamamlanana kadar bilinçli olarak açılmadı.
- **Eksik/kullanıcı eylemi gerektiren adım:** bu job'ın `main`'e push/PR'da **zorunlu (required) status check** olması bir workflow dosyası değişikliğiyle değil, GitHub repo ayarlarından yapılır (Settings → Branches → Branch protection rules → "Require status checks to pass" → `backend-build-test` seç). Bu, kod değişikliği kapsamının dışında - repo sahibinin GitHub arayüzünden yapması gerekiyor.
