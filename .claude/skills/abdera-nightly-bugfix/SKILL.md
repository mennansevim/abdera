---
name: abdera-nightly-bugfix
description: Abdera gece bakım turu — açık `bug` issue'larını ve canlıdaki yeni hata bildirimlerini toplar, her birinin gerçekten bug olup olmadığını kodda/canlıda doğrular, doğrulananları testle kanıtlayıp düzeltir, `Closes #N` ile commit'ler ve tek seferde abdera-deploy akışıyla canlıya alır. Emin olmadığı veya riskli işi düzeltmez, `needs-human` etiketiyle bırakır. Kullanılacak — zamanlanmış gece görevi, "gece turunu çalıştır", "bug'ları otomatik düzelt".
---

# Abdera Gece Bug Turu

Kullanıcı yokken çalışır: **soru sorma, beklenmedik durumda dur ve raporla.** Sorulacak bir şey
çıkarsa o işi `needs-human` olarak bırak, diğerlerine devam et. Amaç "her gece bir şey deploy
etmek" değil: yanlış bir düzeltmeyi canlıya almak, düzeltmemekten kötüdür.

Proje kuralları `CLAUDE.md`'de — özellikle para/takvim/rıza audit kuralları, JSON
serileştirme, OrderBy sırası, PersonEraser. Gece düzeltmesi de bunlara tabidir.

## 0. Hazırlık — kullanıcının çalışma ağacına dokunma

Kullanıcının `main` checkout'unda yarım iş olabilir; abdera-deploy "commit'lenmemiş her şeyi
commit'le" der, bu yüzden gece turu **asla** ana checkout'ta çalışmaz. Ayrı bir worktree kullan:

```bash
REPO=/Users/sevimm/Documents/Projects/abdera-web
WT=/Users/sevimm/Documents/Projects/abdera-nightly
git -C "$REPO" fetch -q origin
[ -d "$WT" ] || git -C "$REPO" worktree add --detach "$WT" origin/main
cd "$WT" && git checkout -q --detach origin/main && git reset -q --hard origin/main && git clean -fdq
git switch -C nightly/$(TZ=Europe/Istanbul date +%F)
```

Bundan sonraki her komut `$WT` içinde. Commit'ler bu yerel dalda birikir, en sonda
`git push origin HEAD:main` ile main'e gider (force asla).

Ön koşullar — biri tutmazsa **hiçbir şey düzeltmeden** raporla ve bitir:
- `gh auth status` geçiyor, `ssh -o BatchMode=yes deploy@panel.abderasanat.com true` geçiyor.
- Docker/Podman ayakta (`docker info`) — backend integration testleri Testcontainers ister.
- `origin/main`'in son CI koşusu yeşil (`gh run list --branch main --workflow ci.yml -L1`).
  Kırmızıysa gece turu o kırmızıyı düzeltmeye kalkmaz; raporlar.
- Canlı sağlıklı: `curl -fsS https://panel.abderasanat.com/health`.

## 1. Adayları topla

```bash
gh issue list --state open --label bug --json number,title,body,labels,createdAt,comments \
  --jq 'sort_by(.createdAt)'
```

- **Atla:** `needs-human`, `no-auto`, `wontfix`, `duplicate`, `question` etiketli olanlar;
  ve son yorumu gece turuna ait olup o tarihten beri yeni yorum/düzenleme gelmemiş olanlar
  (aynı issue'ya her gece aynı yorumu yazma).
- `enhancement` issue'ları gece turunun işi **değil** — yeni özellik kullanıcıyla birlikte yapılır.

Canlıdaki yeni hata bildirimleri (Hata bildir sayfası, `bug_reports`):

```bash
ssh deploy@panel.abderasanat.com "cd /opt/abdera && docker compose exec -T db sh -c \
  'psql -U \"\$POSTGRES_USER\" \"\$POSTGRES_DB\" -At -F \"|\" -c \
  \"select id, created_at, kind, page_path, app_version, description from bug_reports where status = '\''New'\'' order by created_at\"'" </dev/null
```

Triage güncellemesinde `updated_at = now()` da yaz. Her bildirim için triage
(`github-issues-task-flow` kuralı): kodda/canlıda doğrula →
- gerçek bug → `gh issue create --label bug` (başlık `fix: ...`, gövdede bildirimdeki sayfa +
  açıklama, kişi adı/telefon gibi kişisel veri **yazma** — repo public), kaydı
  `status='Triaged', github_issue_number=N` yap, issue aday listesine girer;
- bug değil (istek ise) → `enhancement` issue aç, `Triaged` yap, bu gece uygulama;
- ne bug ne makul istek → `status='Rejected', triage_note='...'`.

Gece başına **en fazla 3 bug** düzelt (eskiden yeniye). Kalanlar ertesi geceye kalır.

## 2. Her aday için: doğrula → kanıtla → düzelt

### 2a. Gerçekten bug mı?

Issue metnini değil kodu ve davranışı esas al. Hata gerçekten var mı, hâlâ var mı (başka bir
commit düzeltmiş olabilir), beklenen davranış `docs/` ve `CLAUDE.md` ile tutarlı mı?

- Zaten düzelmiş → kanıtıyla (commit hash) yorum yaz, issue'yu kapat.
- Bug değil, tasarım gereği → `docs/10-decisions.md`'deki ilgili kararı göstererek yorum yaz,
  `needs-human` ekle (kapatma kararı kullanıcının).
- Yeniden üretemiyorsan → ne denediğini yorumla, `needs-human`.

### 2b. Risk kapısı — şunlardan biri varsa düzeltme, `needs-human` bırak

- Yeni migration gerekiyor, ya da veri düzeltmesi (canlı DB'de UPDATE/DELETE) gerekiyor.
- Para hesabının kendisi: `TuitionCalculator`, `TeacherPayRate`, `ProfitabilitySnapshotBuilder`,
  `Receivable.Reprice`, tahsilat/ödeme yazan handler'lar, banka eşleştirme algoritması.
- `PersonEraser`, auth/oturum/rol denetimi, webhook imza doğrulaması, rıza/opt-out akışı.
- Düzeltme bir iş kuralını yorumlamayı gerektiriyor (iki makul davranış var, hangisi doğru belirsiz).
- Değişiklik ~150 satırı veya 2 modülü aşıyor; ya da yeni bir bağımlılık istiyor.

Böyle bir issue'ya yorumda kök nedeni, önerilen düzeltmeyi ve riskin ne olduğunu yaz — sabah
kullanıcı karar versin.

### 2c. Önce kırmızı test

Hatayı yeniden üreten testi **düzeltmeden önce** yaz ve kırmızı olduğunu gör
(`CLAUDE.md` → Test stratejisi: saf kural → birim testi; endpoint/sorgu → `WebApplicationFactory`
ile gerçek HTTP; yalnızca stil/etiket gibi test edilemeyen UI düzeltmesinde test şartı yok,
o durumda yorumda nasıl doğruladığını yaz).

### 2d. Düzelt ve doğrula

Minimum düzeltme; ilgisiz refactor yok. Sonra:

```bash
dotnet build backend/Abdera.slnx && dotnet test backend/Abdera.slnx     # backend değiştiyse (tamamı)
npm run lint --prefix frontend && npm run build --prefix frontend       # frontend değiştiyse
```

Uzun komutları `run_in_background` ile çalıştır. Bir test kırmızıysa ve düzeltmen yüzünden
ise iki denemede çözemezsen değişikliği geri al (`git checkout -- .`), `needs-human`.
Testleri geçirmek için mevcut testi gevşetmek/silmek **yasak**.

### 2e. Commit

`abdera-commit` kuralları (Türkçe conventional commit, secret taraması, `.env` kontrolü) —
ama push etme, yerel dalda biriktir:

```
fix(<modül>): <kullanıcının gördüğü hata, tek cümle>

<kök neden, 1-3 satır>

Closes #N
```

## 3. Deploy — yalnızca en az bir düzeltme commit'lendiyse

```bash
git fetch -q origin && git rebase origin/main     # çakışma → dur, push etme, raporla
git push origin HEAD:main
```

Deploy'dan **önce** sunucudaki mevcut commit'i kaydet — geri dönüş hedefi budur:

```bash
PREV=$(ssh -o BatchMode=yes deploy@panel.abderasanat.com 'cd /opt/abdera && git rev-parse HEAD' </dev/null)
```

Ardından `.claude/skills/abdera-deploy/SKILL.md`'nin **1b'den 4'e kadar** adımlarını uygula
(sürüm notu → push → CI yeşil → `ssh deploy@panel.abderasanat.com "abdera-deploy $SHA"`).
Sürüm notu commit'i de bu worktree'de yapılır. **Etiketi (`v<sürüm>`) duman testi geçmeden atma.**

- CI kırmızı → deploy etme. Düşen testin senin commit'inden geldiğini doğrularsan
  `git revert` ile o commit'i geri al, push et, CI'ı tekrar bekle ve kalan düzeltmelerle deploy
  et; revert edilen issue'ya `needs-human` + log özeti. Emin değilsen hiç deploy etme, raporla.
- Kullanıcı gün içinde deploy edilmemiş commit'ler bırakmışsa (`v*` etiketinden sonra main'de
  başka commit'ler) onlar da bu deploy'la canlıya çıkar — raporda ayrıca listele.

### 3b. Canlıyı kontrol et — duman testi

```bash
deploy/nightly/smoke-check.sh "$SHA" 15
```

Sayfalar (`/`, `/login`, `/dashboard`, `/parent`, `/gizlilik`), `/health` gövdesi, API'nin
`/api/auth/me`'ye 401 dönmesi, JS paketlerinin servis edilmesi, sunucudaki commit'in `$SHA`
olması, container durumları ve son 15 dakikadaki api log hataları. `SMOKE OK` → etiketle
(abdera-deploy adım 5), rapora geç. Geçici ağ sorunu ihtimaline karşı başarısızsa 2 dakika
sonra **bir kez** tekrar çalıştır; ikinci kez de başarısızsa 3c.

### 3c. Sorun varsa: geri dön + mail at

Kullanıcı gece turuna otomatik geri dönüş yetkisi verdi (2026-10-13) — abdera-deploy'un
"rollback kararını kullanıcıya sor" kuralı **yalnızca bu akışta** geçersiz.

1. Geri dönüş güvenli mi? `git diff --name-only "$PREV" "$SHA" | grep -E '/Migrations/'`
   - Migration **yoksa** → `ssh deploy@panel.abderasanat.com "abdera-deploy $PREV"` ile önceki
     sürüme dön, sonra `deploy/nightly/smoke-check.sh "$PREV" 5` ile dönüşü doğrula.
   - Migration **varsa** → kod geri dönerse yeni şemayla eski kod çakışabilir; **geri dönme**,
     yalnızca mail at / bildir (2). Veritabanını dökümden geri yüklemek asla otomatik yapılmaz.
   - Betiğin kendisi sağlıksız deyip zaten `$PREV`'e döndüyse yeniden deploy etme, doğrudan 2.
2. Bildir (her durumda — dönüş başarılı olsa da). Kullanıcı mail istiyor (2026-10-13):
   - **Mail:** gövdeyi (Türkçe) scratchpad'e yaz, sonra
     ```bash
     deploy/nightly/alert-mail.sh "Abdera: gece deploy'u sorunlu - <geri dönüldü | GERİ DÖNÜLEMEDİ>" <gövde dosyası>
     ```
     Gövde: ne deploy edildi (`$PREV`→`$SHA`, issue'lar), duman testinin FAIL satırları, geri
     dönüş yapıldı mı ve sonucu, `docker compose logs --tail 100 api` özeti, sabah yapılması
     gereken. Secret/kişisel veri koyma.
   - **Kayıt:** aynı metinle `gh issue create --label bug --label needs-human`, başlık
     `deploy: gece deploy'u sorunlu <tarih> - <geri dönüldü | GERİ DÖNÜLEMEDİ>` (repo public).
   - **Push:** `PushNotification` ile tek satır, issue numarasıyla.
   - Mail betiği çıkış 3 (ayar dosyası yok) ya da hata verirse issue + push yine gider;
     raporun en üstüne "mail gönderilemedi: <neden>" yaz.
3. Etiket atma. `Closes #N` ile kapanan issue'ları yeniden aç, "canlıya çıkamadı, geri dönüldü"
   yorumu yaz, `needs-human` ekle. Aynı gece **tekrar deploy deneme**.

## 4. Raporla

- Her işlenen issue'ya kısa yorum: ne bulundu, ne yapıldı (commit), canlıda mı.
  Yorumun ilk satırı `🤖 Gece turu <tarih>` olsun (1. adımdaki tekrar-yorum kontrolü buna bakar).
- `docs/nightly/` gibi repo dosyasına log **yazma**; rapor issue yorumları + son mesajdır.
- Son mesaj (zamanlanmış görev çıktısı olarak kullanıcı sabah görür), Türkçe:
  - Canlıya çıkan sürüm + düzeltilen issue'lar,
  - `needs-human` bırakılanlar ve bir cümlelik nedeni,
  - triage edilen hata bildirimleri,
  - atlanan ön koşul / kırmızı CI / rollback varsa en üstte.
- Hiç aday yoksa tek satır: "Bekleyen bug yok, deploy yapılmadı."
- Bir şey deploy edildiyse veya kullanıcının kararı gerekiyorsa `PushNotification` ile tek satır
  bildir; boş gecelerde bildirim gönderme. Canlı sorunu (3c) ayrıca mail + issue ile bildirilir.

## Yapılmayacaklar

- Ana checkout'ta (`abdera-web`) çalışmak, oradaki değişiklikleri commit'lemek ya da stash'lemek.
- `enhancement` uygulamak, kapsam dışı "fırsat" refactor'ları.
- Canlı veritabanında `bug_reports` triage güncellemesi dışında yazma.
- Force-push, tag silme, sunucuda `.env`/Caddy/volume değişikliği, `pkill -f` (Podman VM paylaşılıyor).
- Testleri susturmak, atlamak (`Skip=`), eşik gevşetmek.
