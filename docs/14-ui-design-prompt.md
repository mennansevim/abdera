# UI Tasarım Uygulama Görevi — Abdera

> **Bu prompt'u yeni bir oturuma yapıştırırken referans mockup görselini (4 ekranlı "Abdera Müzik Okulu Yönetim Sistemi - Örnek Ekranlar" PNG'si) de ekle.** Aşağıdaki metin görsel olmadan da uygulanabilecek kadar ayrıntılı yazıldı, ama görsel varken oran/boşluk kararları çok daha isabetli olur.

## 0. Bağlam ve kurallar

Proje kökü: `/Users/sevimm/Documents/Projects/abdera-web`. Bu görev **yalnızca `frontend/src` altını** ilgilendirir; backend endpoint'leri, `lib/*.ts` içindeki TanStack Query hook'ları ve veri sözleşmeleri **değişmez**. Eğer bir tasarım detayı backend'de olmayan bir alan istiyorsa (örn. veli kartında öğretmen adı) önce `docs/07-api.md`'ye bak; alan gerçekten yoksa **uydurma** — o detayı atla ve raporunda "şu alan API'de yok" diye bildir.

Başlamadan önce oku: `CLAUDE.md` (dil kuralı: kod İngilizce, arayüz metni **Türkçe**; yeni bağımlılık eklemeden önce sor), `docs/10-decisions.md`, `docs/02-modules.md`.

**Yeni bağımlılık yok.** Tailwind v4 + el yazımı bileşenler devam. shadcn/ui, framer-motion, bir takvim kütüphanesi, bir ikon paketi — hiçbiri onaysız eklenmez. İkonlar `src/components/icons.tsx` içindeki inline SVG setinden gelir; yeni bir ikon gerekiyorsa aynı dosyaya aynı stroke ağırlığıyla (1.6–1.75, `currentColor`, 24 viewBox) ekle.

Çalışma ağacı şu an temiz değil (Guardian portal üzerinde devam eden iş var). **Önce `git status`'a bak**; devam eden değişiklikler varsa onları bozmadan, ayrı commit'ler halinde ilerle.

## 1. Önce ölç, sonra yaz (bu adımı atlama)

Kod yazmadan önce mevcut durumu görsel olarak yakala:

1. `frontend` içinde dev sunucusunu **preview aracıyla** başlat (Bash ile `npm run dev` çalıştırma — `.claude/launch.json` yoksa oluştur, `npm run dev`, port 3000).
2. Backend'i `docker compose up -d db api` ile ayağa kaldır ve `.env`'deki bootstrap admin hesabıyla giriş yap. Veri boşsa ekranlar boş durumda görünür — gerçek bir hafta programı, en az 2 öğretmen, 6–8 ders, 1 bekleyen değişiklik talebi, 1 `NeedsReview` banka işlemi ve 1 veli hesabı seed et (SQL ile doğrudan yazmak serbest, kalıcı migration ekleme).
3. Şu 4 rota × 3 kırılımda ekran görüntüsü al: `/login`, `/dashboard` (admin), `/dashboard` (teacher oturumu), `/parent` — genişlikler **1440 / 834 / 390 px**.
4. Her ekran için mockup ile yan yana koyup **somut fark listesi** çıkar (renk/ölçü/boşluk/hiyerarşi/eksik öğe). Bu listeyi kullanıcıya sun ve onay al; ancak ondan sonra kod yaz.

Bu adımın amacı: "genel bir güzelleştirme" yapıp yine tatmin etmeyen bir sonuç üretmemek. Farklar ölçülür, tek tek kapatılır, tekrar ekran görüntüsü alınır.

## 2. Tasarım dili — tek kaynak `globals.css`

Mockup'un dili: krem kâğıt zemin, kırık-beyaz kartlar, koyu mor gradyan sidebar, tek bir mor birincil renk, enstrüman başına pastel ders blokları, yumuşak ve **çok az** gölge, 12–16px köşe yarıçapı.

Mevcut token'lar `src/app/globals.css` içinde zaten doğru temeli kuruyor (`--background: #f8f6f1`, `--surface: #fffdf9`, `--brand: #4a378f`, `--line: #e8e2da`, `.app-card`, `.field`, `.pressable`, `.skeleton`). **Bunları çoğaltma, genişlet:**

- Sayfa içi hiçbir yeni yerde çıplak hex kullanma. Şu an kod içinde dağınık duran `#5948aa`, `#493690`, `#281e5c`, `#36a561`, `#b84c4c`, `#e75b55`, `#f2f1ff` gibi değerleri token'a taşı: `--brand`, `--brand-strong`, `--sidebar-from/--sidebar-to`, `--success`, `--danger`, `--today-tint`, `--on-brand`. Tek istisna: `src/lib/lesson-colors.ts` içindeki enstrüman paleti — orası zaten tek merkez, orada kalsın.
- **Tipografi ölçeği** ekle ve her ekranda ona uy. Şu an `.52rem`–`1.7rem` arası ~12 farklı serbest boyut var; mockup'ta 5 kademe var:
  `--text-display` 1.75rem/700/-0.035em (sayfa başlığı, KPI rakamı) · `--text-title` 1rem/700 (kart başlığı) · `--text-body` .875rem/500 · `--text-meta` .75rem/500 (ikincil satır) · `--text-micro` .6875rem/700/uppercase/.06em (etiket, chip).
  `.5rem`–`.62rem` arası mikro metinler mockup'takinden küçük ve okunmuyor — hepsini `--text-meta`/`--text-micro`'ya çek.
- **Boşluk ritmi**: kart iç dolgusu 1rem (mobil) / 1.25rem (≥sm), kartlar arası 1rem, bölüm arası 1.25rem. Rastgele `p-2.5`, `gap-1.5`, `mt-0.5` yığını yerine bu ritmi kullan.
- **Gölge**: yalnızca `--shadow-card` ve birincil butonun gölgesi. Ders bloklarında `shadow-sm`/`shadow-md` yerine kenarlık + sol renk şeridi.
- **Dokunma hedefi**: her tıklanabilir öğe ≥ 44px (`min-h-11`). Mevcut kodda buna uyulmuş, bozma.

## 3. Ekran ekran hedef ve kabul kriterleri

### A. Giriş — `/login`, `/parent/login`

Mockup'a **en yakın** ekran; büyük yapısal iş yok, rötuş var.

- Rol kartlarındaki açıklama metni `--text-meta`'ya çıkar (şu an `.68rem`, mockup'takinden küçük).
- Seçili rol kartının vurgusu mockup'ta belirgin (mor kenarlık + hafif mor zemin); şu anki `ring-[#6a54b3]/8` neredeyse görünmez — `--brand-soft` zemin + 1.5px mor kenarlık yap.
- Kart maksimum genişliği 420px kalsın; ≥sm'de dikeyde ortalansın (şu an `min-h-dvh` yüzünden mobilde üstte yapışık duruyor, masaüstünde ortalı — mobilde de üst boşluk 2rem'i geçmesin).
- Kabul: 390px'te klavye açıkken şifre alanı görünür kalıyor; sekme sırası rol butonları → e-posta → şifre → giriş; hata mesajı `role="alert"` ile duyuruluyor (mevcut).

### B. Yönetici paneli — `/dashboard` (Admin)

**B1. Üst bar.** Başlık + tarih satırı + arama mockup'la uyumlu. Tek fark: mockup'ta aramanın sağında bir ikon buton var (bildirim/hızlı ekleme). Bu butona **gerçek bir işlev** ver — `/dashboard/notifications`'a giden, okunmamış/başarısız bildirim sayısını rozet olarak taşıyan bir buton (veri `useNotifications("Failed", 1, 1)` ile zaten çekiliyor). Süs buton ekleme.

**B2. KPI kartları.** Mockup'ta rakam iri (≈2rem), etiket altında küçük ve sakin, ikon sağ üstte pastel kare içinde. Şu anki kartlarda üçüncüsü (`"4 kayıt · ₺8.400"`) tek satıra sıkışıp ölçeği bozuyor.
- `StatCard`'a ikinci bir satır ver: büyük değer (`4 kayıt`) + altında ikincil değer (`₺8.400`). Dört kartın rakam taban çizgisi hizalı olmalı.
- 390px'te 2×2, 834px'te 2×2, 1440px'te 1×4.
- Kabul: değer `undefined` iken kart iskelet gösteriyor (mevcut `loading`), sıfır değerde uyarı rengi **yanmıyor**.

**B3. "Bu Hafta" ızgarası — bu maddeyi ciddiye al, en görünür kusur burada.**

Şu anki `WeeklySchedule` üç yerde mockup'tan sapıyor:

1. **Kırılım.** Izgara yalnızca `xl:` (≥1280px) altında görünüyor; 768–1279px arasında ajanda listesine düşüyor. Mockup'un notu net: **ajanda görünümü yalnızca <768px'te**. Izgarayı `md:` (≥768px) itibarıyla göster, ajanda `md:hidden` olsun. 768–1024 arası için sütun genişliği daralacağından ders bloğu içeriğini kademeli azalt (saat + öğrenci adı kalsın, enstrüman satırı `lg:` altında gizlensin).
2. **Sabit saat penceresi.** `TimeLabels` 09:00–19:00'a, konum hesabı `/600*100` ile aynı pencereye çakılı; `Math.min(96, ...)` clamp'i pencere dışındaki dersi yanlış yere yapıştırıyor. Pencereyi **o haftanın gerçek min/max ders saatinden** türet (en erken dersin saatinin başı, en geç dersin bitişinin bir sonraki saati; hiç ders yoksa 09:00–19:00 varsayılanı). Satır yüksekliğini saat başına sabit px yap (örn. 3.25rem) — yüzde yerine px, böylece 45 dakikalık ders her pencerede doğru oranda görünür.
3. **Çakışan dersler.** Aynı saatte iki öğretmenin dersi şu an üst üste biniyor. Aynı gün içinde zaman aralığı kesişen blokları grupla ve grup içinde eşit genişlikte yan yana yerleştir (`left: i/n`, `width: 1/n`, aralarında 2px). Mockup'ta da paralel dersler yan yana duruyor.

Ek olarak:
- **Gösterge (legend).** Mockup'ta ızgaranın sağ üstünde katılım durumu göstergesi var: **Katıldı / Bekliyor / İptal**. Şu anki enstrüman renk göstergesinin yerine bunu koy; enstrüman ayrımı zaten blok renginden ve blok içindeki metinden okunuyor. Bloğun katılım durumu sağ üst köşede 6px'lik bir nokta ile gösterilsin (yeşil/amber/kırmızı), iptal edilen ders `line-through` + %55 opaklık.
- **Blok etkileşimi.** Blok şu an doğrudan `/dashboard/calendar`'a gidiyor. Mockup'ta blok üzerinde detay görünüyor: tıklayınca **popover** aç (öğrenci, enstrüman, öğretmen, saat, katılım durumu + "Takvimde aç" bağlantısı). Popover kaçış tuşuyla kapansın, odak tuzağı olmasın, ekran dışına taşmasın.
- **Sürükle-bırak.** Mockup notu "yalnızca genişletilmiş grid görünümünde aktif" diyor ve bu işlev `/dashboard/calendar`'da zaten var (commit a67e832). Dashboard ızgarasına **ekleme** — bunun yerine ızgara başlığına "Takvimi aç" bağlantısı koy ve `/dashboard/calendar`'daki sürükle-bırak ızgarasının aynı görsel dili (yeni saat aralığı, çakışma düzeni, durum noktası) kullandığından emin ol. İki ızgara ortak bir bileşenden türemeli; kodu kopyalama, `src/components/week-grid.tsx` gibi ortak bir bileşene çıkar.

**B4. Sağ sütun ("dikkat rayı").**
- İki kart: "Bekleyen Değişiklik Talepleri" ve "Gözden Geçirilmesi Gereken Banka İşlemleri" (mockup'taki başlığı kullan; şu an "İncelenecek Banka İşlemleri").
- Değişiklik talebi satırı: öğrenci adı + eski→yeni saat + talep eden, sağda yuvarlak yeşil ✓ / kırmızı ✕ butonları (44px). İşlem sırasında yalnızca o satır kilitlenir (mevcut `busyId` deseni doğru), başarıda satır yumuşak bir şekilde listeden çıkar.
- Banka işlemi satırı: tutar (kalın, tabular-nums) + gönderen açıklaması + "İncele" butonu.
- Boş durum: "Bekleyen talep yok" gibi tek satır sakin metin + küçük ikon; kart tamamen kaybolmasın.
- ≥1280px'te sağda 18rem sütun, 768–1279px'te ızgaranın **altında** iki kart yan yana, <768px'te alt alta.

### C. Öğretmen portalı — `/dashboard` (Teacher rolü)

- **Gün şeridi** var (7 gün) ama mockup'taki gibi okunmuyor: seçili gün mockup'ta koyu dolgulu ve belirgin, diğerleri kâğıt beyazı; gün kısaltması `--text-micro`, tarih `--text-title`. Bugünün altında küçük bir nokta olsun (seçili gün ≠ bugün olabilir). Şerit 390px'te taşarsa yatay kaydırılabilir olsun (`overflow-x-auto`, snap), 7 günü küçültüp okunmaz hale getirme.
- **Ders kartı**: mockup'ta sol blok yalnızca saat (iri, tabular) + altında enstrüman; sağda öğrenci adı, altında küçük gri satır, sağ üstte durum chip'i ("Geliyor" yeşil / "Bekliyor" amber / "Gelmiyor" kırmızı); altta iki eşit buton **Yoklama Al** ve **Not Ekle**. Şu anki 14×14 renkli kare bloğu bu hiyerarşiye çevir; enstrüman rengi sol kenarda 3px şerit olarak kalsın.
- **Genişletilen panel** (yoklama/not) mevcut; kartın içinde açılırken kart yüksekliği zıplamasın — panel `grid-template-rows` geçişiyle açılsın, `prefers-reduced-motion`'da anında.
- **Masaüstü davranışı tanımlı olsun.** Mockup öğretmen portalını dar bir kolon olarak gösteriyor; kodda ≥1024px'te sol sidebar da görünüyor. Karar: sidebar kalsın, içerik `max-w-[32rem]` yerine `max-w-3xl` ile ortalansın ve ≥1280px'te ders listesi 2 sütuna geçsin. Öğretmende üst bar gizli (`me.role === "Teacher"` koşulu) — bu doğru, koru.
- Kabul: dersi olmayan günde boş durum kartı görünüyor (mevcut), gün değiştirildiğinde liste iskeleti gösteriliyor, yoklama kaydı sonrası chip anında güncelleniyor.

### D. Veli uygulaması — `/parent`

- **Başlık.** Mockup: öğrenci baş harfleri (turuncu gradyan) + adı + "enstrüman · öğretmen" + sağda **"…" menüsü**. Şu anki hâlde "+1" öğrenci değiştirme butonu ve ayrı çıkış butonu var. İkisini "…" menüsünün içine al: menü içeriği = öğrenci listesi (birden fazlaysa, seçili olan işaretli) + "Çıkış yap". Menü `Escape` ile kapansın, dışarı tıklamayla kapansın.
- **Sıradaki ders kartı.** Mockup'un görsel çapası bu: şeftali/krem zemin, üstte `--text-micro` "SIRADAKİ DERS", altında iri başlık "Piyano Dersi", sonra "Yarın, 21 Ağustos Cuma · 15:00–15:45", sonra "… ile", en altta iki buton — dolu yeşil **Geliyorum**, kenarlıklı **Gelemiyorum**. Bugün/yarın/gün adı hesaplaması Türkçe ve `Europe/Istanbul`'a göre olmalı (`CLAUDE.md` zaman kuralı). Yanıt verildikten sonra kart, seçilen yanıtı gösteren sakin bir duruma geçsin ve "Değiştir" bağlantısı sunsun (mevcut `forceEditing` mantığı korunur).
- **İki küçük kart.** "Eylül Aidatı" (tutar iri + durum rozeti Ödendi/Bekliyor/Gecikti) ve "Telafi Hakkı" (adet iri + son kullanma tarihi). Eşit yükseklik, 2 sütun.
- **Son Bildirimler.** Satır başına: kanal ikonu (dairesel pastel zemin), 2 satırda kesilen metin (`line-clamp-2`), sağda/altta göreli zaman ("2 saat önce"). Boş durum metni Türkçe ve sakin.
- **Alt sekme çubuğu** (Ana Sayfa / Takvim / Aidat / Mesajlar) mevcut ve doğru; ikon+etiket boyutunu `--text-micro`'ya hizala, aktif sekmede ikonun üstünde ince bir vurgu olsun.
- **Kabuk.** ≥640px'te telefon çerçevesi (max 390px, yuvarlatılmış) mockup'la uyumlu — koru. Gerçek bir telefonda (390px) tam ekran davranışını `env(safe-area-inset-bottom)` ile birlikte doğrula (mevcut).

### E. Genel — her ekranda geçerli

- **Boş / yükleniyor / hata** üçlüsü her veri bölümünde tanımlı olmalı. Yükleniyor = `.skeleton` (spinner değil), hata = kart içinde tek satır Türkçe mesaj + "Tekrar dene" butonu.
- **Erişilebilirlik**: kontrast AA (özellikle `--muted` üzerindeki `.6rem` metinler — büyütülünce zaten düzelecek), her ikon-butonda `aria-label`, aktif menüde `aria-current="page"`, popover/menülerde `Escape`.
- **Türkçe metin**: büyük/küçük harf dönüşümlerinde daima `toLocaleUpperCase("tr-TR")` (mevcut kodda doğru yapılmış), tarih/para daima `Intl` + `tr-TR`.
- Konsolda tek bir uyarı/hata kalmasın (React key, hydration, `<a>` içinde `<a>` vb.).

## 4. Doğrulama — her ekran bitiminde

1. `npm run build` temiz (uyarı dahil sıfır).
2. `npm run lint` — **not:** `banking/page.tsx`'te önceden var olan bir lint hatası CI'da `frontend-build-lint` job'ının kapalı kalmasına sebep oluyor (`docs/11-progress-log.md`, OPS-1). Bu görev kapsamında o hatayı da düzelt ve `.github/workflows/ci.yml`'deki job'ı aç.
3. Preview aracıyla 390 / 834 / 1440 px'te ekran görüntüsü al, mockup ile karşılaştır, farkı raporla.
4. Konsol ve ağ isteklerini kontrol et (hata yok, gereksiz yeniden istek yok).
5. Klavye ile tam tur: sekme sırası mantıklı, odak halkası her yerde görünür, hiçbir tuzak yok.

## 5. Teslim

- Ekran başına bir commit (`abdera-commit` skill'i, Türkçe conventional commit). Push için kullanıcıdan onay iste (repo public).
- `docs/11-progress-log.md`'ye tek bölüm ekle: neyi değiştirdin, hangi tasarım kararını neden aldın (özellikle B3'teki saat penceresi ve kırılım kararı), neyi bilerek yapmadın.
- Mockup'ta olup API'de karşılığı olmadığı için atladığın her şeyi ayrı bir liste olarak raporun sonunda ver.

## 6. Güncel desen — form yerine "+" (2026-09 sadeleştirmesi)

Yukarıdaki bölümler ilk tasarım görevinin metnidir; aşağıdaki kural onların üzerine gelir ve
yeni ekran yazarken **bağlayıcıdır**:

- Ekran üstünde her zaman açık duran oluşturma formu **yok**. Başlık satırının sağında küçük
  bir `+` (`AddButton`) durur, form `Modal` içinde açılır.
- Ortak bileşenler: `src/components/ui.tsx` → `PageHeader`, `SectionHeader`, `AddButton`,
  `Modal`, `FormActions`, `FormMessage`. Yeni ekran bunları kullanır, kendi başlık/pencere
  iskeletini kurmaz.
- Buton ve etiket sınıfları `globals.css`'te: `.btn` + `.btn-primary`/`.btn-quiet`,
  `.icon-btn` + `.icon-btn-brand`/`.icon-btn-quiet`, `.form-label`. Uzun Tailwind zinciriyle
  yeni bir buton varyantı üretme.
- `AddButton` metin taşımaz; erişilebilir adı `label`'dan gelir ve **ne eklendiğini** söyler
  ("Öğretmen ekle"). E2E seçicileri bu ada bağlıdır, değiştirirken testi de güncelle.
- Her form alanının görünen bir etiketi olur (`.form-label`); yalnızca `placeholder` ya da
  `title` ile alan anlatılmaz.

### Bileşen sınıfları `@layer components` içinde durur (gerçek bir hata)

`globals.css`'teki `.field`, `.btn`, `.icon-btn`, `.app-card`, `.text-*` gibi sınıflar
katmansız yazıldığında Tailwind'in **utilities katmanını eziyor** - CSS'te katman sırası
özgüllükten önce gelir, katmansız kural her zaman kazanır. Sonuç: `class="field pl-9"` yazan
arama kutusunda `.field`'in kısayol `padding`'i geçerli kalıyor, `pl-9` hiç uygulanmıyor ve
placeholder ikonun üstüne biniyordu (kullanıcı ekran görüntüsüyle bildirdi). Aynı sessiz
eziliş `btn-quiet border-dashed` (kesikli çerçeve görünmüyordu) ve `icon-btn h-9 w-9`
(buton hep 2.75rem kalıyordu) örneklerinde de vardı.

Kural: yeni bir bileşen sınıfı `@layer components { ... }` içine yazılır. Böylece tek bir
utility onu geçersiz kılabilir - beklenen davranış bu. Katmanın dışında yalnızca bilinçli
"son söz" kuralları kalır: `:root` token'ları, `body`, element reset'leri ve
`prefers-reduced-motion`/`prefers-contrast` gibi erişilebilirlik media query'leri.

İçinde ikon olan bir alan yazarken: ikon `absolute left-3`, alan `pl-9` (ikon 1rem ise) -
ölçüp doğrula, `padding-left` gerçekten uygulanmış olmalı (yazı ile ikon arasında ~7px boşluk).
