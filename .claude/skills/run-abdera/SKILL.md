---
name: run-abdera
description: Abdera otomatik bakım turu — GitHub'daki açık task'ları (bug + geliştirme isteği) ve canlıdaki yeni hata bildirimlerini toplar, her birini doğrular, güvenli olanları (mimariyi/mevcut yapıyı bozmayan, öğrenci/öğretmen verisi silmeyen, tehlikesiz) testle geliştirip `Closes #N` ile commit'ler, SSH ile canlıya alır, canlıyı kontrol eder, sorun varsa geri döner ve sonucu mail atar. Riskli işi yapmaz, `needs-human` etiketiyle bırakır. Kullanılacak — kullanıcı **"run abdera"** yazınca (bu ifade uygulamayı yerel çalıştırma isteği DEĞİL, bu skill'dir) ve her akşamki zamanlanmış görevde.
---

# run abdera

İki yoldan tetiklenir: kullanıcı sohbette **"run abdera"** yazar ya da akşam zamanlanmış görevi
çalışır. İkisinde de akış aynıdır ve **sorusuzdur**: kullanıcı onayı baştan verdi (2026-10-13).
Sorulacak bir şey çıkarsa o işi `needs-human` olarak bırak, diğerlerine devam et. Amaç "her akşam
bir şey deploy etmek" değil: yanlış bir değişikliği canlıya almak, hiç almamaktan kötüdür.

Proje kuralları `CLAUDE.md`'de — para/takvim/rıza audit kuralları, JSON serileştirme, OrderBy
sırası, PersonEraser, dil kuralları. Otomatik iş de bunlara tabidir. Yeni use-case için
`abdera-module`, migration için `abdera-migration`, bildirim için `abdera-notification`
skill'lerinin kontrol listelerini uygula.

## 0. Hazırlık — kullanıcının çalışma ağacına dokunma

Kullanıcının `main` checkout'unda yarım iş olabilir; abdera-deploy "commit'lenmemiş her şeyi
commit'le" der, bu yüzden bu akış **asla** ana checkout'ta çalışmaz. Ayrı worktree:

```bash
REPO=/Users/sevimm/Documents/Projects/abdera-web
WT=/Users/sevimm/Documents/Projects/abdera-nightly
git -C "$REPO" fetch -q origin
[ -d "$WT" ] || git -C "$REPO" worktree add --detach "$WT" origin/main
cd "$WT" && git checkout -q --detach origin/main && git reset -q --hard origin/main && git clean -fdq
git switch -C run/$(TZ=Europe/Istanbul date +%F-%H%M)
```

Bundan sonraki her komut `$WT` içinde. Commit'ler bu yerel dalda birikir, en sonda
`git push origin HEAD:main` ile main'e gider (force asla).

Ön koşullar — biri tutmazsa **hiçbir şeye dokunmadan** rapor maili at ve bitir:
- `gh auth status` ve `ssh -o BatchMode=yes deploy@panel.abderasanat.com true` geçiyor.
- Container gerçekten başlıyor mu: `docker run --rm docker.io/library/alpine:3.20 true` (`docker info`
  yetmez - 2026-10-13'te Podman VM depolaması bozukken `info` geçip her container 500 verdi).
  Başlamıyorsa durma: backend integration testleri yalnızca CI'da koşar (CI zaten deploy kapısı);
  yerelde birim testleri + derleme yeter. Podman'ı yeniden başlatma (paylaşılan VM, diğer
  projelerin container'ları düşer) - maile "yerel container çalışmıyor" notu ekle.
- `origin/main`'in son CI koşusu yeşil (`gh run list --branch main --workflow ci.yml -L1`).
  Kırmızıysa onu düzeltmeye kalkma; raporla.
- Canlı sağlıklı: `curl -fsS https://panel.abderasanat.com/health`.

## 1. Bekleyen task'ları topla

```bash
gh issue list --state open --json number,title,body,labels,createdAt,comments --jq 'sort_by(.createdAt)'
```

- **Atla:** `needs-human`, `no-auto`, `wontfix`, `duplicate`, `question` etiketli olanlar; ve son
  yorumu bu akışa ait olup (`🤖 run abdera` ile başlar) o tarihten beri yeni yorum/düzenleme
  gelmemiş olanlar — aynı issue'ya her akşam aynı yorumu yazma.
- Sıra: önce `bug`, sonra `enhancement`/etiketsiz; her grupta eskiden yeniye.

Canlıdaki yeni hata bildirimleri (Hata bildir sayfası, `bug_reports`):

```bash
ssh deploy@panel.abderasanat.com "cd /opt/abdera && docker compose exec -T db sh -c \
  'psql -U \"\$POSTGRES_USER\" \"\$POSTGRES_DB\" -At -F \"|\" -c \
  \"select id, created_at, kind, page_path, app_version, description from bug_reports where status = '\''New'\'' order by created_at\"'" </dev/null
```

Her bildirim için triage (`github-issues-task-flow` kuralı), güncellemede `updated_at = now()`:
- gerçek bug → `gh issue create --label bug` (başlık `fix: ...`), makul istek →
  `--label enhancement` (başlık `feat: ...`); gövdede sayfa + açıklama, kişi adı/telefon gibi
  kişisel veri **yazma** (repo public). Kaydı `status='Triaged', github_issue_number=N` yap;
  issue aday listesine girer.
- ne bug ne makul istek → `status='Rejected', triage_note='...'`.

"Hâlâ hatalı" teyidi gelen bildirimler (`docs/10-decisions.md` W6) — kullanıcı çözümü
beğenmedi, görev yeniden açılır:

```bash
# select id, github_issue_number, feedback_note from bug_reports where status = 'Reopened'
gh issue reopen <N> --comment "🤖 run abdera <tarih>: bildirimi yapan hâlâ hatalı dedi - <feedback_note | açıklama yok>"
# update bug_reports set status = 'Triaged', resolved_at = null, updated_at = now() where id = '<id>'
```

Yeniden açılan issue bu turun aday listesine girer (bug'lardan önce, en öncelikli); `feedback_note`
ilk düzeltmenin neyi kaçırdığını söyler, 2a'da onu esas al.

Tur başına **en fazla 3 task** işle. Kalanlar sonraki tura kalır.

## 2. Her task için: anla → güvenlik kapısı → geliştir

### 2a. Doğrula

Issue metnini değil kodu ve davranışı esas al.
- **Bug:** gerçekten var mı, hâlâ var mı (başka commit düzeltmiş olabilir), beklenen davranış
  `docs/` ve `CLAUDE.md` ile tutarlı mı? Zaten düzelmişse kanıtıyla (commit) yorum yaz, kapat.
  Tasarım gereğiyse `docs/10-decisions.md`'deki kararı göstererek yorumla, `needs-human`.
  Yeniden üretemiyorsan ne denediğini yorumla, `needs-human`.
- **Geliştirme isteği:** ne istendiği net mi, `docs/10-decisions.md`'deki bir kararla ya da
  `CLAUDE.md` → "Yapılmayacaklar" ile çelişiyor mu? Çelişiyorsa ya da iki farklı makul
  yorumu varsa yapma, sorunu ve seçenekleri yorumla, `needs-human`.

### 2b. Güvenlik kapısı — kullanıcının kuralı

Otomatik yapılır **yalnızca** şunların üçü de tutuyorsa:
1. **Tüm yapıyı / mevcut yapıyı etkilemiyor.** Değişiklik ekleme niteliğinde ve bir modülün
   içinde kalıyor; mevcut davranışı yalnızca issue'nun istediği kadar değiştiriyor.
2. **Öğrenci/öğretmen verisini silmeyi içermiyor** — kişi, kayıt, ders, aidat, ödeme satırı
   silen ya da toplu değiştiren hiçbir yol, betik veya migration.
3. **Tehlikeli değil.**

Şunlardan biri varsa **yapma**, `needs-human` + yorum (kök neden / plan / risk) bırak:
- Mimari değişiklik: yeni modül, modüller arası bağımlılık, `Program.cs`/kimlik doğrulama
  altyapısı, yeni NuGet/npm bağımlılığı, `docker-compose`/Dockerfile/Caddy/CI değişikliği.
- Mevcut tablo/kolonu değiştiren, yeniden adlandıran, silen ya da NOT NULL yapan migration;
  canlı veride UPDATE/DELETE gerektiren veri düzeltmesi. **İzinli:** yalnızca yeni tablo ya da
  nullable/varsayılanlı yeni kolon ekleyen migration (`abdera-migration` kontrol listesiyle).
- Para hesabının kendisi: `TuitionCalculator`, `TeacherPayRate`, `ProfitabilitySnapshotBuilder`,
  `Receivable.Reprice`, tahsilat/ödeme yazan handler'lar, banka eşleştirme algoritması.
- `PersonEraser`, kişi/veri silme, auth/oturum/rol/yetki denetimi, webhook imza doğrulaması,
  rıza/opt-out, veliye giden WhatsApp/e-posta metninin gönderim kuralları.
- `CLAUDE.md`'deki "Kritik veritabanı kısıtları" ya da bir iş kuralı invariant'ına dokunmak.
- Değişiklik ~400 satırı (testler hariç) veya 2 modülü aşıyor.

### 2c. Önce test

- **Bug:** hatayı yeniden üreten testi **önce** yaz, kırmızı olduğunu gör.
- **Geliştirme:** `CLAUDE.md` → "Her yeni use-case en az bir mutlu yol + bir invariant-ihlali
  testiyle gelir". Endpoint/sorgu → `WebApplicationFactory` ile gerçek HTTP çağrısı.
- Yalnızca stil/metin gibi test edilemeyen UI işinde test şartı yok; yorumda nasıl
  doğruladığını yaz.

### 2d. Geliştir ve doğrula

En küçük doğru değişiklik; ilgisiz refactor yok. UI metni Türkçe, kod İngilizce. Sonra:

```bash
dotnet build backend/Abdera.slnx && dotnet test backend/Abdera.slnx     # backend değiştiyse (tamamı)
npm run lint --prefix frontend && npm run build --prefix frontend       # frontend değiştiyse
```

Uzun komutları `run_in_background` ile çalıştır. Kendi değişikliğin yüzünden kırmızı olan bir
testi iki denemede çözemezsen değişikliği geri al (`git checkout -- . && git clean -fd`),
`needs-human`. Testleri geçirmek için mevcut testi gevşetmek/silmek **yasak**.

### 2e. Commit

`abdera-commit` kuralları (Türkçe conventional commit, secret taraması, `.env` kontrolü) —
push etme, yerel dalda biriktir. Her task ayrı commit:

```
fix(<modül>): <kullanıcının gördüğü hata>      |   feat(<modül>): <kullanıcının kazandığı şey>

<kök neden ya da ne eklendiği, 1-3 satır>

Closes #N
```

## 3. Deploy — yalnızca en az bir commit varsa

```bash
git fetch -q origin && git rebase origin/main     # çakışma → dur, push etme, raporla
PREV=$(ssh -o BatchMode=yes deploy@panel.abderasanat.com 'cd /opt/abdera && git rev-parse HEAD' </dev/null)
```

`$PREV` geri dönüş hedefidir. Ardından `.claude/skills/abdera-deploy/SKILL.md`'nin **1b'den
4'e kadar** adımlarını uygula (sürüm notu → `git push origin HEAD:main` → CI yeşil →
`ssh deploy@panel.abderasanat.com "abdera-deploy $SHA"`). Sürüm notu bu worktree'de yazılır;
geliştirmeler kullanıcıya görünüyorsa "Yenilikler" maddesi olur. **Etiketi (`v<sürüm>`)
duman testi geçmeden atma.**

- CI kırmızı → deploy etme. Düşen test senin commit'inden geliyorsa `git revert` ile o commit'i
  geri al, push et, CI'ı tekrar bekle ve kalanlarla deploy et; revert edilen issue'yu yeniden aç,
  `needs-human` + log özeti. Emin değilsen hiç deploy etme.
- Kullanıcının deploy edilmemiş commit'leri (son `v*` etiketinden sonra main'de) varsa onlar da
  bu deploy'la canlıya çıkar — mailde ayrıca listele.

### 3b. Canlıyı kontrol et

```bash
deploy/nightly/smoke-check.sh "$SHA" 15
```

Sayfalar, `/health` gövdesi, API'nin `/api/auth/me`'ye 401 dönmesi, JS paketleri, sunucudaki
commit, container durumları, son 15 dakikanın api log hataları. `SMOKE OK` → etiketle
(abdera-deploy adım 5) ve 3d. Başarısızsa 2 dakika sonra **bir kez** tekrar; yine başarısızsa 3c.

### 3d. Çözülen bildirimleri "Çözüldü" yap

Yalnızca duman testi geçtikten sonra (eski kod `Resolved` durumunu okuyamaz; geri dönülürse
ekran bozulur). `status = 'Triaged'` her bildirim için: issue'su kapalı **ve** kapatan commit
canlıdaysa → çözüldü. Bu, bu turun düzeltmelerini de kullanıcının elle kapattıklarını da kapsar.

```bash
gh issue view <N> --json state --jq .state                          # CLOSED mı
C=$(git log origin/main --format=%H -E --grep "Closes #<N>([^0-9]|$)" -1)   # kapatan commit (git ERE'de \b yok)
git merge-base --is-ancestor "$C" "$SHA"                             # canlıda mı ($SHA = canlıdaki commit)
# update bug_reports set status = 'Resolved', resolved_at = now(), updated_at = now() where id = '<id>' and status = 'Triaged'
```

Kapatan commit bulunamazsa (issue elle kapatılmış) çözüldü yapma, raporda belirt. Bildirimi yapan
kişi Hata bildir sayfasında teyit eder; teyit bu akışın işi değil. Bu adım deploy olmayan turda da
çalışır (`$SHA` = sunucudaki commit).

### 3c. Sorun varsa geri dön

Kullanıcı otomatik geri dönüş yetkisi verdi (2026-10-13) — abdera-deploy'un "rollback kararını
kullanıcıya sor" kuralı **yalnızca bu akışta** geçersiz.

- `git diff --name-only "$PREV" "$SHA" | grep /Migrations/` boşsa ya da migration'lar yalnızca
  ekleme (2b'deki izinli tür) ve yeni enum değeri canlı veriye henüz yazılmamışsa → `ssh deploy@panel.abderasanat.com "abdera-deploy $PREV"`,
  sonra `deploy/nightly/smoke-check.sh "$PREV" 5`. Eski kod yeni tablo/nullable kolonu görmezden
  gelir; şema geri alınmaz.
- Başka tür migration varsa (kullanıcının kendi commit'lerinden gelmiş olabilir) **geri dönme**,
  yalnızca bildir. Veritabanını dökümden geri yüklemek asla otomatik yapılmaz.
- Betik sağlıksız deyip zaten `$PREV`'e döndüyse yeniden deploy etme.
- Etiket atma; `Closes #N` ile kapanan issue'ları yeniden aç, "canlıya çıkamadı, geri dönüldü"
  yorumu + `needs-human`. Aynı turda **tekrar deploy deneme**.
- Ayrıca `gh issue create --label bug --label needs-human`, başlık
  `deploy: run abdera sorunlu <tarih> - <geri dönüldü | GERİ DÖNÜLEMEDİ>`, gövdede FAIL satırları
  + `docker compose logs --tail 100 api` özeti (secret/kişisel veri yok — repo public).

## 4. Rapor maili — her tur sonunda

Her işlenen issue'ya kısa yorum (ilk satır `🤖 run abdera <tarih>`): ne bulundu, ne yapıldı
(commit), canlıda mı.

Sonra kullanıcıya mail at — **iş yapıldıysa, bir şey `needs-human` kaldıysa, ön koşul tutmadıysa
ya da canlıda sorun çıktıysa**. Hiç bekleyen task ve yeni bildirim yoksa mail atma (yalnızca son
mesajda "Bekleyen task yok" yaz).

```bash
deploy/nightly/alert-mail.sh "<konu>" <gövde dosyası>      # gövdeyi scratchpad'e yaz
```

Konu: `Abdera: <sürüm> canlıda - N task` · `Abdera: karar bekleyen N task` ·
`Abdera: deploy sorunlu - <geri dönüldü | GERİ DÖNÜLEMEDİ>` (sorun varsa her zaman bu, en önce).

Gövde (Türkçe, sade):
- Sorun / geri dönüş / ön koşul hatası varsa en üstte.
- Canlıya çıkan sürüm; yapılan her task: `#N başlık` — kullanıcının diliyle bir cümle ne değişti,
  issue linki.
- Senin kararını bekleyenler (`needs-human`): `#N` — neden yapılmadı, bir cümle.
- Triage edilen hata bildirimleri: kaç tane, hangi issue'lara dönüştü; "Çözüldü" yapılanlar
  (teyit bekliyor) ve "hâlâ hatalı" dönüp yeniden açılanlar.
- Kullanıcının deploy'a dahil olan kendi commit'leri varsa listesi.
- Secret, şifre, kişisel veri (öğrenci/veli adı, telefon) yok.

Mail betiği çıkış 3 (ayar dosyası `~/.config/abdera-nightly/mail.env` yok) ya da hata verirse
`PushNotification` ile tek satır özet gönder ve son mesajın başına "mail gönderilemedi: <neden>"
yaz. Mail gittiyse ayrıca push bildirimi gönderme — yalnızca canlı sorununda gönder.

Son mesaj (sohbette ya da zamanlanmış görev çıktısında) mailin kısa özetidir.

## Yapılmayacaklar

- Ana checkout'ta (`abdera-web`) çalışmak, oradaki değişiklikleri commit'lemek ya da stash'lemek.
- 2b kapısından geçmeyen işi "küçük görünüyor" diye yapmak; kapsam dışı "fırsat" refactor'ları.
- Canlı veritabanında `bug_reports` durum güncellemeleri (triage, çözüldü, yeniden açma) dışında
  yazma; kişi/kayıt silme.
- Force-push, tag silme, sunucuda `.env`/Caddy/volume değişikliği, `pkill -f` (Podman VM paylaşılıyor).
- Testleri susturmak, atlamak (`Skip=`), eşik gevşetmek.
