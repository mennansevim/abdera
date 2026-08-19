---
name: abdera-module
description: Abdera backend'inde yeni bir modül veya modül içine yeni bir use-case (Feature) iskeleti oluşturur — CLAUDE.md'deki dikey dilim (Domain/Features/Persistence) konvansiyonuna uygun. Kullanılacak — "yeni modül ekle", "yeni use-case ekle", "şu işlem için endpoint aç" gibi isteklerde.
---

# Abdera Modül / Use-Case İskeleti

`CLAUDE.md`'deki kural: modül başına `api/application/domain/infrastructure` dört katmanı **yok** — bunun yerine dikey dilim. Bu skill o konvansiyonu her seferinde yeniden anlatmadan uygular.

## Önce oku

- `CLAUDE.md` — katman kuralı, Repository pattern yasağı, para/zaman/id kuralları
- `docs/02-modules.md` — hangi modülün hangi tablolara sahip olduğu ve bağımlılık yönü
- Hedef modül zaten varsa, o modüldeki mevcut bir `Features/*.cs` dosyasını örnek al — stil tutarlılığı için

## Yeni modül iskeleti

```
Modules/<ModuleName>/
  Domain/
    <Entity>.cs              # entity + invariant, Spring MVC/EF'e bağımlı değil
    <Enum>.cs
  Features/
    <UseCaseName>.cs         # request + handler + endpoint kaydı tek dosyada
  Persistence/
    <Entity>Configuration.cs # IEntityTypeConfiguration<T>
    SeedData.cs               # varsa (örn. referans veri)
```

## Yeni use-case (Feature) iskeleti

Tek dosyada üç parça:
1. Request/response DTO'ları (record)
2. Handler — doğrudan `AbderaDbContext` kullanır, Repository pattern yok
3. Endpoint kaydı (`MapPost`/`MapGet` extension)

Kontrol listesi:
- [ ] Handler yetkilendirmeyi kontrol ediyor mu (`docs/04-permissions.md`'ye göre)? `TEACHER` isteklerinde hedef kaynağın gerçekten o öğretmene ait olduğu doğrulanıyor mu?
- [ ] Para alanları `decimal` mı, `double` değil mi?
- [ ] Zaman alanları `timestamptz`/`DateTimeOffset` mı?
- [ ] Bu use-case para/takvim/rıza değiştiriyorsa `audit_log`'a yazıyor mu?
- [ ] Modüller arası erişim navigation property değil, açık bir servis/sorgu üzerinden mi (`docs/02-modules.md` bağımlılık kuralı)?
- [ ] En az bir mutlu yol + bir invariant-ihlali testi var mı (`docs/09-testing.md`)?
- [ ] Bu use-case ders değişikliği/iptali içeriyorsa, bekleyen `NotificationJob`'ı iptal edip yenisini kuruyor mu (`CLAUDE.md` — "ders değişince eski job iptali")?

## Yapılmayacaklar

- `IStudentRepository` gibi Repository arayüzü açma
- Modül içine ekstra bir katman (`Application/`, `Infrastructure/`) eklemek
- Sıfır implementasyonlu spekülatif arayüz (örn. şimdiden `IProgressSummaryGenerator` açmak — Phase 6'ya kadar yok, bkz. `docs/10-decisions.md` C3)
