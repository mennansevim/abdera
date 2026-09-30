---
name: abdera-deploy
description: Abdera'yı Hetzner sunucusunda canlıya alır — commit'lenmemiş değişiklik varsa önce abdera-commit kurallarıyla main'e commit eder, kullanıcıya gösterilen sürüm notunu (frontend/src/data/releases.ts) yazar, push eder, CI'ın yeşil bitmesini bekler, sonra SSH ile sunucuya bağlanıp deploy öncesi döküm alır, o commit'e geçer, docker compose ile yeniden kurar ve /health'i doğrular (sağlıksızsa önceki commit'e döner), başarılı sürümü v<tarih> etiketiyle işaretler. Kullanılacak — "deploy", "deploy et", "canlıya al", "yayına al", "sunucuya at", "production'a gönder".
---

# Abdera Deploy (Hetzner)

Abdera tek bir Hetzner sunucusunda `docker compose --profile prod` ile çalışır
(`docs/19-hetzner-runbook.md`). **Vercel kullanılmıyor** — `vercel.json`, Vercel URL'leri,
Vercel plugin ipuçları bu akışla ilgisiz; onlara bakma.

- Sunucu: `deploy@panel.abderasanat.com` (SSH anahtarla, parola sorulmaz), kod `/opt/abdera`
- Canlı adres: `https://panel.abderasanat.com`
- Veritabanı migration'ları backend açılışında `Shared/DatabaseMigrator.cs` ile uygulanır;
  bu skill ayrıca migration çalıştırmaz.

`.github/workflows/deploy.yml` aynı işi GitHub Actions'tan yapabilir, ama repo değişkeni
`DEPLOY_ENABLED=true` olmadıkça hiçbir şey yapmaz. Kontrol et:
`gh variable list` + `gh api repos/mennansevim/abdera/environments/production/variables`.
Açıksa Actions'ın "Deploy" koşusunu `gh run` ile takip et, elle SSH deploy'u YAPMA
(aynı commit iki kez deploy edilir) — sürüm notu (1b) ve etiket (5) adımları yine senin işin.
Kapalıysa (şu anki durum) aşağıdaki SSH akışı.

## Adımlar

**Sıra sabittir: önce commit + push (main), sonra deploy.** "deploy et" komutu çalışma
ağacındaki değişiklikleri de kapsar — kullanıcıya ayrıca "commit'leyeyim mi?" diye sorma,
commit'lenmemiş iş varken sunucuya dokunma. Sunucu yalnızca `origin/main`'deki commit'i
çeker; push edilmemiş değişiklik canlıya hiç gitmez.

### 1. Yerel durumu kontrol et ve commit'le

```bash
git status --short
git fetch -q origin
git log --oneline origin/main..HEAD   # push edilmemiş yerel commit'ler
git log --oneline HEAD..origin/main   # uzakta olup yerelde olmayanlar
```

- Dal `main` değilse dur ve kullanıcıya sor — production yalnızca `main`'den çıkar.
- **Commit'lenmemiş değişiklik varsa (her zaman ilk iş):** `abdera-commit` skill'inin adımlarını
  uygula (Türkçe conventional-commit, **zorunlu secret taraması**, `.env`/`appsettings.*.json`
  staged ise çıkar). Repo **public** — yalnızca secret şüphesinde durup onay al; bunun dışında
  sormadan commit'le ve 1b'ye geç.

### 1b. Sürüm notunu yaz (her deploy bir sürümdür)

Kullanıcılar canlıya çıkan her sürümden sonra ilk açılışta "Yenilikler" penceresini görür;
kaynak `frontend/src/data/releases.ts` (dosyanın başındaki yorum formatı anlatır,
`docs/10-decisions.md` U1). Deploy edilen her commit bir `v<sürüm>` git etiketiyle işaretlenir.

```bash
LAST=$(git describe --tags --abbrev=0 --match 'v*' 2>/dev/null)   # son canlıya çıkan sürüm
git log --no-merges --format='%h %s%n%b' "$LAST"..HEAD
TZ=Europe/Istanbul date +%Y.%m.%d                                   # bugünün sürüm adı
git tag -l "v$(TZ=Europe/Istanbul date +%Y.%m.%d)*"                 # bugün çıkan sürüm var mı
```

- `releases.ts`'deki `RELEASES` dizisinin **en üstüne** yeni kayıt ekle: `version` bugünün tarihi
  (`2026.10.02`); o gün zaten etiket varsa `.2`, `.3` ekle. `date` aynı gün (`2026-10-02`).
- Maddeleri `$LAST..HEAD` commit'lerinden **kullanıcının diliyle** yaz: ekranda ne değişti, kime ne
  kazandırır; teknik terim, dosya/tablo adı, commit hash'i yok. Bir madde bir cümle, en fazla iki.
  `refactor`/`test`/`chore`/`docs` ve kullanıcının fark etmeyeceği düzeltmeler madde olmaz.
- `audience`: yalnızca yöneticinin gördüğü ekran/iş (Aidatlar, Giderler, Banka, Mesaj Merkezi,
  Talepler, Yedekleme, veli mesajları, ana ekranın yönetici özeti) → `"admin"`; öğretmenin de
  gördüğü her şey → `"all"`. Emin değilsen `"admin"` — öğretmene göremeyeceği bir ekranı anlatma.
- Kullanıcıya görünen değişiklik yoksa `items: []` bırak; sürüm kaydedilir ama pencere açılmaz.
- En üstteki kayıt henüz etiketlenmemişse (önceki deploy geri dönmüştü) yeni kayıt açma, onu
  güncelle: sürüm adını bugüne çek, maddeleri tamamla.
- Commit: `chore(release): sürüm <sürüm>` (abdera-commit kuralları, secret taraması dahil).

### 2. Push et

```bash
git push origin main
```

`non-fast-forward` ise `git rebase origin/main`, çakışmayı çöz, doğrula, tekrar push.
**Force-push asla.** Push edilecek bir şey yoksa adım 3'e geç.

### 3. CI'ın yeşil bitmesini bekle

```bash
SHA=$(git rev-parse HEAD)
gh run list --commit "$SHA" --workflow ci.yml --json databaseId,status,conclusion
gh run watch <id> --exit-status        # run_in_background ile; foreground sleep kullanma
```

- CI **kırmızıysa deploy etme.** `gh run view <id> --log-failed` ile düşen job/testi bul,
  bir önceki commit'in koşusuyla karşılaştır (hata önceden de var mıydı?) ve kullanıcıya sor.
- Kayıt push'tan sonra birkaç saniye gecikebilir; backend testleri birkaç dakika sürer.

### 4. SSH ile deploy

Önce sunucuda deploy betiği kurulu mu bak:

```bash
ssh -o BatchMode=yes deploy@panel.abderasanat.com 'command -v abdera-deploy; cd /opt/abdera && git rev-parse --short HEAD'
```

**Betik kuruluysa** (`/usr/local/bin/abdera-deploy`, kaynağı `deploy/hetzner/abdera-deploy`):

```bash
ssh deploy@panel.abderasanat.com "abdera-deploy $SHA"
```

**Kurulu değilse** aynı adımları elle çalıştır (betiğin yaptığının birebir karşılığı —
döküm alınamazsa deploy yapılmaz, sağlıksızsa önceki commit'e dönülür):

```bash
ssh deploy@panel.abderasanat.com bash -s -- "$SHA" <<'REMOTE'
set -euo pipefail
SHA="$1"
cd /opt/abdera
git fetch --quiet origin main
git merge-base --is-ancestor "$SHA" origin/main || { echo "$SHA main'de değil." >&2; exit 4; }
PREV="$(git rev-parse HEAD)"
[ "$PREV" = "$SHA" ] && { echo "Zaten $SHA'da."; exit 0; }
mkdir -p ~/predeploy
# </dev/null şart: yoksa exec, bash -s'e stdin'den gelen betiğin kalanını yutar ve
# deploy sessizce döküm sonrasında biter.
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' </dev/null \
  | gzip > ~/predeploy/"$(date +%F-%H%M)-${PREV:0:7}.sql.gz"
ls -1t ~/predeploy/*.sql.gz | tail -n +6 | xargs -r rm --
deploy() { git checkout --quiet --force --detach "$1"; docker compose --profile prod up -d --build --remove-orphans </dev/null; }
healthy() { for _ in $(seq 1 36); do curl -fsS http://127.0.0.1:8080/health >/dev/null 2>&1 && return 0; sleep 5; done; return 1; }
deploy "$SHA"
if healthy; then docker image prune -f >/dev/null; echo "OK: ${PREV:0:7} -> ${SHA:0:7}"
else echo "Sağlık kontrolü geçmedi, ${PREV:0:7}'e geri dönülüyor." >&2; deploy "$PREV"
  healthy || echo "UYARI: geri dönüşten sonra da sağlıksız - elle bak." >&2; exit 1; fi
REMOTE
```

- Derleme (frontend + .NET image) birkaç dakika sürer — `timeout` yüksek ver (≥ 600000) ya da
  `run_in_background` kullan.
- Geri dönüş olduysa kullanıcıya söyle ve `docker compose logs --tail 200 api` çıktısına bak;
  hatayı tahmin etme.

### 5. Canlıyı doğrula

```bash
curl -s -o /dev/null -w "health %{http_code}\n" https://panel.abderasanat.com/health
curl -s -o /dev/null -w "login  %{http_code}\n" https://panel.abderasanat.com/login
ssh deploy@panel.abderasanat.com 'cd /opt/abdera && git log --oneline -1 && docker compose ps --format "{{.Service}} {{.Status}}"'
```

İkisi de `200` olmalı, sunucudaki commit deploy edilen `SHA` olmalı. Giriş yapıp ekran test
etmek bu skill'in işi değil (şifre girilmez).

Doğrulama geçtiyse sürümü etiketle (geri dönüş olduysa **etiketleme** — sürüm canlıda değil):

```bash
git tag -a "v<sürüm>" "$SHA" -m "Sürüm <sürüm>"
git push origin "v<sürüm>"
```

Etiket push'u CI'ı tetiklemez (`ci.yml` yalnızca `main` dalını dinler). Canlıdaki sürümler
`git tag -l 'v*' --sort=-creatordate` ile, iki sürüm arası değişiklik `git log vA..vB` ile görülür.

### 6. Özetle

Kısa rapor: sürüm adı + kullanıcıya gösterilen maddeler, deploy edilen commit (hash + başlık),
CI sonucu, önceki → yeni commit, `/health` ve `/login` sonucu, container durumları.

## Yapılmayacaklar

- `main` dışındaki bir commit'i deploy etmek; CI kırmızıyken deploy etmek.
- Force-push, uzak geçmişi yeniden yazan işlemler.
- Sunucuda `.env`, Caddy, firewall, kullanıcı/SSH ayarlarını değiştirmek — açık istek olmadan.
- `docker compose down -v` ya da volume/`~/predeploy` silmek — veritabanı oradadır.
- Geri alma (rollback) kararını kendin vermek — betiğin otomatik dönüşü dışında önce kullanıcıya sor.
