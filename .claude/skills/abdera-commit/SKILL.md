---
name: abdera-commit
description: Abdera (müzik okulu yönetim sistemi) reposunda kullanıcının elle yaptığı değişikliği anlamlı Türkçe conventional-commit mesajıyla main'e commit + push eder. Deploy YAPMAZ. Kullanılacak — sadece "commit", "kaydet", "pushla", "yükle", "değişiklikleri kaydet".
---

# Abdera Commit

Bu skill yalnızca **kaydetme** içindir; canlıya alma (deploy) bu skill'in kapsamı dışında.

## Adımlar

1. `git status` ve `git diff` ile neyin değiştiğini gör. Değişiklik yoksa kullanıcıya söyle, işlem yapma.
2. Değişiklikleri modüle göre grupla (`docs/02-modules.md`'deki modül adlarını kullan: auth, people, scheduling, attendance, pricing, billing, progress, messaging, dashboard, docs, infra).
3. Secret sızıntısı kontrolü — **commit'lemeden önce zorunlu**:
   ```bash
   git diff --cached --diff-filter=ACM | grep -iE 'password|secret|token|access_token|api[_-]?key' 
   ```
   Bir eşleşme varsa dur, kullanıcıya göster, onay almadan commit'leme. Repo **public**.
4. `.env`, `appsettings.Development.json`, `appsettings.Local.json` gibi dosyalar staged ise commit'ten çıkar — bunlar `.gitignore`'da olmalı zaten, olmayan bir sızıntı işaretidir.
5. Türkçe, conventional-commit formatında mesaj yaz: `<tip>(<modül>): <özet>`
   - Tipler: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`
   - Örnek: `feat(billing): fiyat listesi toplu güncelleme önizlemesi eklendi`
   - Örnek: `docs(scheduling): telafi kredisi durum makinesi eklendi`
6. `git add -A` (adım 4'teki dosyalar hariç), `git commit`, `git push origin main`.
7. Sonucu kısaca özetle: kaç dosya, hangi modül(ler), commit hash'i.

## Yapılmayacaklar

- Deploy, migration çalıştırma, sunucuya bağlanma — bunlar bu skill'in işi değil.
- `main` dışında bir dala push etmek istenmedikçe her zaman `main`.
- Kullanıcı onayı olmadan force-push.
