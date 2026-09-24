# Nota kütüphanesi

- Herkese açık arşiv: `/kutuphane`; okul içi sekme: `/dashboard/library`.
- Açık arşiv, Mutopia'nın 2.124 kaynak kaydını içerir: 1.948 PDF ve 176 PDF paketi (ZIP).
- Her Mutopia kimliği ve indirme adresi tektir. Kayıt sayısı benzersiz beste sayısı değildir; farklı düzenlemeler ve ayrı yayımlanmış bölümler kaynakta ayrı kayıtlardır. A4/Letter kopyaları ayrı sayılmaz.
- 797 kayıt piyano, 229 kayıt keman veya yaylı topluluğu etiketi taşır. Aynı eser birden fazla enstrümana girebilir; eşlikli eserler dahildir. Solo filtresi ayrıca bulunur.
- Dosyalar Mutopia'da açılır. Okul kitaplarının veya öğrenci bilgilerinin herkese açık arşive aktarılması söz konusu değildir.
- Arşiv eserlerindeki Abdera basamakları yalnızca açıklaması bulunan çalışma önerileridir. Doğrulanmış sınav derecesi değildir. Öneri atanmayan eserler `null` kalır ve arayüzde “Değerlendirilmedi” görünür.
- Çalışma listesi bu tarayıcıda saklanır; kullanıcı hesabıyla eşitlenmez.

## Kaynak ve yenileme

Kaynak: https://www.mutopiaproject.org/ — lisanslar: https://www.mutopiaproject.org/legal.html

`python3 frontend/scripts/import-sheet-music.py` arşivin ilan ettiği sayfa listesini okur, kaynak bilgisini ve lisansı korur, dosya bağlantılarını HTTP HEAD ile denetler. Başarılı olmayan bağlantılar alınmaz; 1.200 kayıt altına düşülürse işlem hata verir. Son kontrol tarihi JSON'da ve arayüzde bulunur. Ağ kesintilerinde geliştirme makinesinin `/tmp/abdera-sheet-music-import` önbelleği yeniden kullanılabilir; tam yeni denetim için bu önbellek ayrı bir yere taşınmalıdır.

Derleme ve kullanıcı araması bu kaynağa ağ isteği yapmaz; sürümlenen `frontend/src/data/sheet-music.json` kullanılır. İndirme, MIDI ve PDF önizlemesi kullanıcı eylemiyle kaynağa gider. Kaynak yayını değiştirilmediği için telif ve yayına hazırlayan bilgileri PDF'de korunur; kayıt ayrıntısında lisans, düzenleme ve kaynak sayfası bağlantısı yer alır.

## Kontroller

`frontend/e2e/library.spec.ts`: tekil kaynaklar, sayı ve lisans bütünlüğü; arama/filtre/sayfalama; kalıcı yerel liste; PDF/ZIP ayrımı; mobil taşma; menü açma/kapatma. Testin okul paneli senaryosu oturum yanıtlarını taklit eder ve canlı okul verisini değiştirmez.
