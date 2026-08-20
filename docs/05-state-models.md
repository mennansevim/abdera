# Durum Makineleri

## Lesson

```mermaid
stateDiagram-v2
    [*] --> NORMAL: seriden üretildi
    NORMAL --> RESCHEDULED: değişiklik onaylandı
    NORMAL --> CANCELLED: iptal edildi
    NORMAL --> COMPLETED: ders gerçekleşti + yoklama girildi
    RESCHEDULED --> COMPLETED
    RESCHEDULED --> CANCELLED
    [*] --> MAKEUP: telafi kredisi kullanılarak yeni ders açıldı
    MAKEUP --> COMPLETED
    MAKEUP --> CANCELLED
    CANCELLED --> [*]
    COMPLETED --> [*]
```

Kurallar:
- `RESCHEDULED`/`CANCELLED`'a geçişte `original_lesson_id` korunur; geçmiş asla üzerine yazılmaz, yeni satır açılır (audit-friendly history — master prompt gereksinimi).
- Bu geçişte bekleyen `LESSON_REMINDER` job'ı **iptal edilir** ve gerekiyorsa yeni saate göre yenisi kurulur (`CLAUDE.md` — "ders değişince eski job iptali").
- `COMPLETED`'a yalnızca `LessonAttendance` kaydı girildiğinde geçilir; ders saati geçti diye otomatik `COMPLETED` olmaz (öğretmen unutmuş olabilir, dashboard bunu "dikkat" listesine düşürür).
- `MAKEUP` statüsündeki ders bir `MakeupCredit.used_lesson_id` ile ilişkilendirilir.

## LessonChangeRequest

```mermaid
stateDiagram-v2
    [*] --> PENDING: talep açıldı
    PENDING --> APPROVED: yönetici onayladı
    PENDING --> REJECTED: yönetici reddetti
    PENDING --> ALTERNATIVE_PROPOSED: yönetici farklı saat önerdi
    ALTERNATIVE_PROPOSED --> PARENT_CONFIRMATION_PENDING: veliye soruldu
    PARENT_CONFIRMATION_PENDING --> PARENT_ACCEPTED
    PARENT_CONFIRMATION_PENDING --> PARENT_REJECTED
    PARENT_ACCEPTED --> APPROVED
    PARENT_REJECTED --> PENDING: yönetici tekrar değerlendirir
    APPROVED --> [*]
    REJECTED --> [*]
```

Kural: `PARENT_REJECTED` durumunda sistem **sessizce** takvimi tekrar değiştirmez — talep `PENDING`'e döner ve yönetici ekranında "dikkat" olarak görünür (master prompt gereksinimi, `docs/00-master-prompt.md` "Lesson change" akışı).

**Bilinçli eksik (denetim ARC-2, `docs/13-audit-fix-prompt.md`):** `ALTERNATIVE_PROPOSED`, `PARENT_CONFIRMATION_PENDING`, `PARENT_ACCEPTED`, `PARENT_REJECTED` yukarıdaki diyagramda tasarlanmış durumlardır ama bugün hiçbir use-case tarafından üretilmiyorlar (`LessonChangeRequestStatus` enum'unda tanımlı, kodda hiçbir yerde set edilmiyor) - bugünkü akış yalnızca `PENDING -> APPROVED/REJECTED`'i uyguluyor. Veliye alternatif saat önerme akışı Faz 7'ye kaldı; bu dört durum için ayrı bir kod değişikliği gerekmiyor, yalnızca gerçekten uygulanana kadar üretilmemeleri bilinçli.

## LessonRsvp.response

```mermaid
stateDiagram-v2
    [*] --> UNKNOWN
    UNKNOWN --> ATTENDING: WhatsApp "✅ Geliyorum"
    UNKNOWN --> NOT_ATTENDING: WhatsApp "❌ Gelemiyorum"
    ATTENDING --> NOT_ATTENDING: veli fikir değiştirdi
    NOT_ATTENDING --> ATTENDING: veli fikir değiştirdi
```

`ATTENDING`/`NOT_ATTENDING` velinin **niyetini** ifade eder, gerçek yoklamayı değil — bu ikisi asla birleştirilmez (master prompt'un açık kuralı).

## LessonAttendance.status

Tek yönlü kayıt, geçiş yok: öğretmen dersten sonra `PRESENT | ABSENT | EXCUSED` seçer, `lesson_id` üzerinde `UNIQUE` olduğu için bir kez girilir. Düzeltme gerekirse mevcut kayıt güncellenir ve `audit_log`'a düşer (kim, ne zaman, eski/yeni değer) — silinmez.

## Receivable.status

```mermaid
stateDiagram-v2
    [*] --> UNPAID: dönem başında oluşturuldu
    UNPAID --> PARTIAL: kısmi ödeme girildi
    UNPAID --> PAID: tam ödeme girildi
    PARTIAL --> PAID: kalan ödendi
    UNPAID --> OVERDUE: due_date geçti, gecelik job
    PARTIAL --> OVERDUE: due_date geçti, gecelik job
    OVERDUE --> PAID: ödeme girildi
    OVERDUE --> PARTIAL: kısmi ödeme girildi
    UNPAID --> CANCELLED: yönetici iptal etti (örn. kayıt sonlandı)
    PARTIAL --> CANCELLED
```

`OVERDUE` türetilmiş bir görünüm değil, **saklanan** bir statüdür — gecelik bir job `due_date < today AND status IN (UNPAID, PARTIAL)` olan kayıtları `OVERDUE`'ya çevirir (bkz. `docs/10-decisions.md` B3). Bu, dashboard sorgusunun her istekte tarih hesaplamak yerine indexlenmiş bir kolon okumasını sağlar.

## NotificationJob.status

```mermaid
stateDiagram-v2
    [*] --> PENDING: job oluşturuldu
    PENDING --> PROCESSING: scheduler FOR UPDATE SKIP LOCKED ile aldı
    PROCESSING --> SENT: Meta API başarılı
    PROCESSING --> FAILED: Meta API hata, attempt_count arttı
    FAILED --> PENDING: bounded retry ile yeniden kuyruğa
    PENDING --> CANCELLED: ilgili ders/aidat değişti, job artık geçersiz
    PROCESSING --> CANCELLED: nadiren — işlenirken referans geçersiz kalırsa
    SENT --> [*]
    CANCELLED --> [*]
```

`FAILED → PENDING` geçişi `attempt_count < Notifications__MaxAttempts` olduğu sürece otomatik; limit aşılınca job `FAILED` kalır ve yönetici panelinde "yeniden dene" ile elle tetiklenir (master prompt — "failed jobs must remain visible").

## MakeupCredit.status

```mermaid
stateDiagram-v2
    [*] --> AVAILABLE: ders ≥24 saat önce iptal edildi
    AVAILABLE --> USED: telafi dersi planlandı
    AVAILABLE --> EXPIRED: expires_at geçti, gecelik job
    USED --> [*]
    EXPIRED --> [*]
```

Kredi doğuran tek olay: dersin `CANCELLED` olma anı ile `start_at` arasında ≥`Policy__MakeupNoticeHours` (varsayılan 24) saat olması. Habersiz gelmeme (`ABSENT`) kredi doğurmaz.
