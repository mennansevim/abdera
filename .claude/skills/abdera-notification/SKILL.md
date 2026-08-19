---
name: abdera-notification
description: Abdera'da yeni bir WhatsApp bildirim tipini uçtan uca ekler — job type, template, Fake/CloudApi implementasyonu, idempotency anahtarı ve test. Kullanılacak — "yeni bildirim türü ekle", "şunun için WhatsApp mesajı gönder", "yeni template ekle" gibi isteklerde.
---

# Abdera Bildirim Ekleme

Bu, sistemin en hataya açık tekrar eden işi — eksik bırakılan tek bir adım (idempotency anahtarı, sessiz saat, fake implementasyon) canlıda velilere yanlış zamanda/mükerrer mesaj gitmesine yol açar. `docs/06-whatsapp.md` ve `CLAUDE.md`'yi önce oku.

## Kontrol listesi — sırayla

1. **Job type** — `notification_jobs.type` enum'una yeni değeri ekle. `reference_type`/`reference_id` neyi işaret edecek netleştir (örn. `PACKAGE_ENDING` → `reference_type="fee_plan"`).
2. **Idempotency anahtarı** — `UNIQUE (type, reference_type, reference_id)` zaten var; yeni tipin `reference_id`'sinin bu iş için doğru doğal anahtar olduğunu doğrula (aynı referansa iki kez job açılmamalı).
3. **Zamanlama sınıfı** — bu bildirim ders hatırlatması gibi olay-tetiklemeli mi (`LESSON_REMINDER`), yoksa cron kaynaklı mı (`PAYMENT_REMINDER`, `BIRTHDAY`, `PACKAGE_ENDING` gibi)?
   - Cron kaynaklıysa → sessiz saat kontrolüne tabi (`Notifications__QuietHoursStart/End`, `docs/06-whatsapp.md` A6). Bu kontrolü atlama.
4. **Rıza kontrolü** — job oluşturma use-case'i `Guardian.notification_consent == true` kontrolüyle başlıyor mu? Rızası kapalı veliye asla job açılmaz.
5. **Template** — yeni template gerekiyorsa `message_templates` tablosuna ekle, Meta'ya onaylatılacak gövdeyi `docs/06-whatsapp.md`'ye yaz. Onay beklerken `Fake` provider ile geliştirmeye devam edilebilir (D2).
6. **Konuşma penceresi** — bu bildirim serbest metin mi template mi kullanacak? Serbest metinse `Guardian.conversation_window_expires_at` kontrolü olmadan asla gönderilmez (A7).
7. **İki implementasyon** — `IWhatsAppClient`'ın hem `FakeWhatsAppClient` hem `CloudApiWhatsAppClient` tarafını güncelle; biri unutulursa dev/prod davranışı ayrışır.
8. **İptal senaryosu** — bu bildirimin kaynağı (ders, aidat, öğrenci) değişirse/silinirse bekleyen job iptal ediliyor mu? (Örnek: A4 — ders saati değişince eski `LESSON_REMINDER` iptali.)
9. **Test** — en az: job doğru anahtarla bir kez oluşuyor, ikinci çağrı mükerrer job açmıyor (unique kısıt), rızası kapalı veli için job açılmıyor, (cron kaynaklıysa) sessiz saat dışında ötelemenin çalıştığı.
10. **Dashboard/panel görünürlüğü** — `FAILED` durumuna düşerse yönetici panelinde görünüyor mu, "yeniden dene" uç noktası çalışıyor mu?

## Yapılmayacaklar

- Sessiz saat kontrolünü sadece `LESSON_REMINDER`'a benzeterek atlamak — cron kaynaklı bildirimler için zorunlu.
- Yeni bildirim tipini yalnızca `CloudApiWhatsAppClient`'a eklemek, `FakeWhatsAppClient`'ı unutmak — dev ortamı sessizce bozulur.
- Template onayı beklerken kod tarafını durdurmak — `Fake` provider ile paralel ilerle.
