---
name: abdera-deploy
description: Abdera'yı Vercel'de canlıya alır — commit'lenmemiş değişiklik varsa önce abdera-commit kurallarıyla main'e commit + push eder, ardından Vercel'in Git entegrasyonunun main için açtığı Production deploy'unu takip eder ve canlı adresi (/health, /login) doğrular. Kullanılacak — "deploy", "deploy et", "canlıya al", "yayına al", "Vercel'e at", "production'a gönder".
---

# Abdera Deploy (Vercel)

Abdera Vercel'de tek proje olarak yayında (`vercel.json`: `web` = `frontend/` Next.js,
`api` = `backend/Dockerfile.vercel` container; `/api/*` ve `/health` → api). Proje
`.vercel/project.json` ile `abdera-web`'e bağlı.

**Deploy mekanizması Git entegrasyonudur:** `main`'e yapılan her push, Vercel'de otomatik bir
**Production** deploy'u başlatır. Yani "deploy et" = `main`'i güncel hale getir + Vercel'in o
commit için açtığı deploy'un bittiğini doğrula. Elle `vercel --prod` çalıştırmak normal akış
DEĞİLDİR (aynı commit'i iki kez deploy eder); yalnızca aşağıdaki "Yeniden deploy" durumunda.

- Canlı adres: `https://abdera-web-nine.vercel.app`
- Vercel CLI yüklü olmayabilir; takip `gh api` üzerinden GitHub deployment kayıtlarıyla yapılır
  (Vercel her deploy'u `vercel[bot]` olarak oraya yazar).
- Veritabanı migration'ları backend açılışında `Shared/DatabaseMigrator.cs` ile uygulanır;
  bu skill ayrıca migration çalıştırmaz.

## Adımlar

### 1. Yerel durumu kontrol et

```bash
git status --short
git fetch -q origin
git log --oneline origin/main..HEAD   # push edilmemiş yerel commit'ler
git log --oneline HEAD..origin/main   # uzakta olup yerelde olmayanlar
```

- Dal `main` değilse dur ve kullanıcıya sor — Production yalnızca `main`'den çıkar.
- **Commit'lenmemiş değişiklik varsa:** `abdera-commit` skill'inin adımlarını uygula (modüle göre
  Türkçe conventional-commit mesajı, **zorunlu secret taraması**, `.env`/`appsettings.*.json`
  staged ise çıkar). Repo **public** — secret şüphesinde onay almadan devam etme.

### 2. Push et

```bash
git push origin main
```

- Push `non-fast-forward` ile reddedilirse: `git rebase origin/main`, çakışma varsa çöz,
  ardından `cd frontend && npx tsc --noEmit -p .` (ve dokunulan dosyalar için `npx eslint …`)
  ile doğrula, sonra tekrar push et. **Force-push asla** (kullanıcı açıkça istemedikçe).
- Push edilecek hiçbir şey yoksa (yerel = `origin/main`) adım 3'e geç: son commit zaten deploy
  edilmiş olabilir.

### 3. Vercel Production deploy'unu takip et

```bash
SHA=$(git rev-parse HEAD)
gh api "repos/mennansevim/abdera/deployments?sha=$SHA&environment=Production" \
  --jq '.[0] | {id, sha: .sha[0:7], created_at}'
# id ile durum:
gh api "repos/mennansevim/abdera/deployments/<id>/statuses" --jq '.[0] | {state, environment_url, log_url}'
```

- Kayıt push'tan sonra birkaç saniye içinde düşer; `state` sırasıyla `pending`/`in_progress` →
  `success` ya da `failure`/`error` olur. Derleme (frontend + .NET container) birkaç dakika sürer.
- Beklerken foreground `sleep` kullanma. Monitor aracıyla bir until-döngüsü kur (ör. 20 sn'de bir
  yukarıdaki durum sorgusu, `success|failure|error` görünce çık, üst sınır ~15 dk) ya da komutu
  `run_in_background` ile çalıştır.
- `failure`/`error` ise `log_url`'yi kullanıcıya ver; commit'in Vercel status'u da şuradan okunur:
  `gh api repos/mennansevim/abdera/commits/$SHA/status --jq '.statuses[] | {context, state, target_url}'`.
  Hatayı tahmin etme — Vercel CLI yoksa logu kullanıcının açması gerekir.

### 4. Canlıyı doğrula

```bash
curl -s -o /dev/null -w "health %{http_code}\n" https://abdera-web-nine.vercel.app/health
curl -s -o /dev/null -w "login  %{http_code}\n" https://abdera-web-nine.vercel.app/login
```

İkisi de `200` olmalı. Deploy'a özel URL'ler (`abdera-<hash>-…vercel.app`) Vercel Deployment
Protection yüzünden `302` dönebilir — bu normal, doğrulamayı canlı alias üzerinden yap.
Giriş yapıp ekran test etmek bu skill'in işi değil (şifre girilmez).

### 5. CI durumunu bildir (engelleyici değil)

```bash
gh run list --commit "$SHA" --json name,status,conclusion --jq '.[] | "\(.name): \(.status) \(.conclusion)"'
```

Vercel deploy'u CI'ı beklemez; CI sonucu bilgi amaçlı raporlanır. Bilinen durum (2026-09-23):
`e2e-smoke` job'u birkaç commit'tir aynı 4 testte (giriş sonrası `waitForURL` zaman aşımı,
`OTP isteği başarısız (HTTP 404)`) düşüyor; `guard-secrets`, `frontend-build-lint`,
`backend-build-test` geçiyor. Yeni bir job ya da farklı bir test düşüyorsa bunu ayrıca vurgula.

### 6. Özetle

Kısa rapor: deploy edilen commit (hash + başlık), Vercel durumu, canlı adresin `/health` ve
`/login` sonucu, CI özeti. Commit gerekmediyse bunu da söyle.

## Yeniden deploy (kod değişmeden)

Kullanıcı aynı commit'i yeniden yayına almak isterse (ör. Vercel ortam değişkeni değişti):
Vercel CLI yüklüyse `vercel deploy --prod` (proje `.vercel/project.json` ile bağlı). Yüklü
değilse kullanıcıya `npm i -g vercel` önerip Vercel panelindeki "Redeploy"u işaret et. Bunun
için boş commit atma.

## Yapılmayacaklar

- `main` dışındaki bir daldan Production deploy'u.
- Force-push, `git reset --hard` gibi uzak geçmişi yeniden yazan işlemler.
- Vercel ortam değişkenlerini değiştirmek, domain/alias ayarlamak — kullanıcının açık isteği olmadan.
- CI kırmızı diye deploy'u "geri almak" — geri alma (rollback) ayrı bir karar, önce kullanıcıya sor.
