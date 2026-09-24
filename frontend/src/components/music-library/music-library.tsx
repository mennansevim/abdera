"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { Icon } from "@/components/icons";
import { scoreFileUrl, useLibraryPieces, useScoreFiles, type LibraryPiece } from "@/lib/library";
import { categories, fromLibraryPiece, initialMusicFilters, instruments, learningSteps, matchesMusic, sources, styles, youtubeSearchUrl, type ArchiveScore, type BookEntry, type CustomPiece, type MusicFilters, type SheetMusic } from "@/lib/sheet-music";
import { AddPieceForm, CustomPieceFile, SuggestToStudent } from "./library-staff";
import css from "./music-library.module.css";

const PAGE_SIZE = 24;
const SAVED_KEY = "abdera-public-score-bookmarks-v1";
const number = (value: number) => value.toLocaleString("tr-TR");

// staff: paneldeki kütüphane (öğretmen/yönetici). Kitap PDF'leri, okulun eklediği eserler ve
// öğrenciye öneri yalnızca orada; herkese açık /kutuphane staff olmadan çizilir.
export function MusicLibrary({ catalogue, checkedAt, staff = false }: { catalogue: SheetMusic[]; checkedAt: string; staff?: boolean }) {
  const customPieces = useLibraryPieces(staff).data;
  const pieces = useMemo(() => [...(customPieces ?? []).map(fromLibraryPiece), ...catalogue], [customPieces, catalogue]);
  const [adding, setAdding] = useState(false);
  const [filters, setFilters] = useState<MusicFilters>(initialMusicFilters);
  const [view, setView] = useState<"library" | "saved" | "path">("library");
  const [saved, setSaved] = useState<string[]>([]);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<SheetMusic | null>(null);
  const [notice, setNotice] = useState("");
  const dialog = useRef<HTMLDialogElement>(null);
  const resultTop = useRef<HTMLDivElement>(null);

  useEffect(() => {
    try {
      const value: unknown = JSON.parse(localStorage.getItem(SAVED_KEY) ?? "[]");
      // Hydrate browser-local preferences after the identical server/client render.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      if (Array.isArray(value)) setSaved(value.filter((id): id is string => typeof id === "string" && pieces.some(p => p.id === id)));
    } catch { /* Storage is optional; the catalogue remains usable. */ }
    const id = new URLSearchParams(window.location.search).get("eser");
    const piece = pieces.find(p => p.id === id);
    if (piece) setSelected(piece);
  }, [pieces]);

  useEffect(() => {
    const node = dialog.current;
    if ((selected || adding) && node && !node.open) node.showModal();
    if (!selected && !adding && node?.open) node.close();
  }, [selected, adding]);

  // Eklenen eser silinince açık ayrıntı penceresi kapanır.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (selected?.source === "custom" && customPieces && !customPieces.some(p => p.entryId === selected.id)) setSelected(null);
  }, [customPieces, selected]);

  const counts = useMemo(() => Object.fromEntries(Object.keys(instruments).map(key => [key, key === "all" ? pieces.length : pieces.filter(p => p.instruments.includes(key)).length])), [pieces]);
  const filtered = useMemo(() => pieces.filter(p => (view !== "saved" || saved.includes(p.id)) && matchesMusic(p, filters)), [pieces, saved, view, filters]);
  const pages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, pages);
  const rows = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);
  const filterCount = Number(filters.source !== "all") + Number(filters.level !== "all") + Number(filters.category !== "all") + Number(filters.style !== "all") + Number(filters.solo);
  const activeFilters = filterCount > 0 || filters.instrument !== "all" || !!filters.query;
  const suggestedCount = pieces.filter(p => p.level !== null).length;
  const bookCount = pieces.filter(p => p.source === "book").length;
  const customCount = pieces.filter(p => p.source === "custom").length;
  const scoreFiles = useScoreFiles(staff).data;

  function updateFilters(patch: Partial<MusicFilters>) { setFilters(current => ({ ...current, ...patch })); setPage(1); }
  function changeView(next: typeof view) { setView(next); setPage(1); setNotice(""); }
  function resetFilters() { setFilters(initialMusicFilters); setPage(1); }
  function savePiece(piece: SheetMusic) {
    const adding = !saved.includes(piece.id);
    const next = adding ? [...saved, piece.id] : saved.filter(id => id !== piece.id);
    setSaved(next);
    try { localStorage.setItem(SAVED_KEY, JSON.stringify(next)); setNotice(adding ? "Çalışma listene eklendi. Bu tarayıcıda saklanır." : "Çalışma listenden çıkarıldı."); }
    catch { setNotice("Liste bu oturumda güncellendi. Tarayıcı kalıcı saklamaya izin vermiyor."); }
  }
  function openPiece(piece: SheetMusic) {
    setSelected(piece);
    const url = new URL(window.location.href); url.searchParams.set("eser", piece.id);
    window.history.replaceState(null, "", url);
  }
  function pieceAdded(piece: LibraryPiece) {
    setAdding(false);
    openPiece(fromLibraryPiece(piece));
  }
  function closePiece() {
    setAdding(false);
    setSelected(null);
    const url = new URL(window.location.href); url.searchParams.delete("eser");
    window.history.replaceState(null, "", url);
  }
  function goToPage(next: number) {
    setPage(Math.max(1, Math.min(pages, next)));
    resultTop.current?.scrollIntoView({ block: "start", behavior: "instant" });
  }

  return <section className={css.library} aria-label="Nota kütüphanesi">
    <header className={css.heading}><h1>Kütüphane</h1><span>{number(pieces.length)} nota kaydı</span>{staff && <button className={css.addPiece} onClick={() => { setSelected(null); setAdding(true); }}><Icon name="plus" />Eser ekle</button>}</header>
    <div className={css.world}>
      <nav className={css.tabs} aria-label="Kütüphane bölümleri">
        <button aria-current={view === "library" ? "page" : undefined} onClick={() => changeView("library")}><Icon name="music" />Keşfet</button>
        <button aria-current={view === "saved" ? "page" : undefined} onClick={() => changeView("saved")}><Icon name="note" />Çalışma listem <span>{saved.length}</span></button>
        <button aria-current={view === "path" ? "page" : undefined} onClick={() => changeView("path")}><Icon name="target" />Öğrenme yolu</button>
      </nav>
      <div className={css.body}>
        {view === "path" ? <div className={css.path}>
          <p>Öğretmeninle birlikte çalışma basamağını seç. Yaş tek başına seviye belirlemez.</p>
          {learningSteps.map((step, i) => <article key={step.name}><b className={css.stepNumber}>{i + 1}</b><div><h2>{step.name}</h2><p>{step.goal}</p><small>{step.practice}</small></div><button onClick={() => { resetFilters(); updateFilters({ level: String(i + 1) }); changeView("library"); }}>Notaları gör<Icon name="arrow-right" /></button></article>)}
          <p className={css.explanation}>{number(suggestedCount)} kayıt için çalışma basamağı önerisi bulunur. Bunlar sınav derecesi veya öğretmen onayı değildir. Diğer eserlerin seviyesi henüz değerlendirilmedi.</p>
        </div> : <>
          <div className={css.instrumentTabs} role="group" aria-label="Enstrüman">
            {["all", "piano", "violin"].map(key => <button key={key} aria-pressed={filters.instrument === key} onClick={() => updateFilters({ instrument: key })}>{key !== "all" && <Icon name={key === "piano" ? "piano" : "violin"} />}{instruments[key]} <span>{number(counts[key])}</span></button>)}
            <label className={css.otherInstrument}><span className={css.sr}>Diğer enstrümanlar</span><select value={["all", "piano", "violin"].includes(filters.instrument) ? "" : filters.instrument} onChange={e => updateFilters({ instrument: e.target.value || "all" })}><option value="">Diğer enstrümanlar</option>{Object.entries(instruments).filter(([key]) => !["all", "piano", "violin"].includes(key)).map(([key, label]) => <option key={key} value={key}>{label} · {counts[key]}</option>)}</select></label>
          </div>
          <div className={css.searchRow}>
            <label className={css.search}><Icon name="search" /><span className={css.sr}>Eser, besteci veya opus ara</span><input type="search" placeholder="Eser, besteci veya opus ara…" value={filters.query} onChange={e => updateFilters({ query: e.target.value })} /></label>
            <button className={css.filterToggle} aria-expanded={filtersOpen} aria-controls="music-filters" onClick={() => setFiltersOpen(!filtersOpen)}><Icon name="filter" />Filtre{filterCount ? ` · ${filterCount}` : ""}</button>
          </div>
          <div id="music-filters" className={`${css.filters} ${filtersOpen ? css.filtersOpen : ""}`}>
            {staff && <label>Kaynak<select value={filters.source} onChange={e => updateFilters({ source: e.target.value })}>{Object.entries(sources).map(([key, label]) => <option key={key} value={key}>{label}{key === "book" ? ` · ${bookCount}` : key === "custom" ? ` · ${customCount}` : ""}</option>)}</select></label>}
            <label>Seviye<select value={filters.level} onChange={e => updateFilters({ level: e.target.value })}><option value="all">Tüm seviyeler</option>{learningSteps.map((step, i) => <option key={step.name} value={i + 1}>{i + 1} · {step.name} (öneri)</option>)}<option value="unknown">Henüz değerlendirilmedi</option></select></label>
            <label>Koleksiyon<select value={filters.category} onChange={e => updateFilters({ category: e.target.value })}>{Object.entries(categories).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
            <label>Dönem / tür<select value={filters.style} onChange={e => updateFilters({ style: e.target.value })}><option value="all">Tüm dönemler</option>{Object.entries(styles).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
            <label className={css.solo}><input type="checkbox" checked={filters.solo} onChange={e => updateFilters({ solo: e.target.checked })} />Yalnız solo</label>
          </div>
          <div ref={resultTop} className={css.results}>
            <span role="status" aria-live="polite"><strong>{number(filtered.length)}</strong> {view === "saved" ? "kayıt listende" : "sonuç"}{filters.source !== "all" ? ` · ${sources[filters.source]}` : ""}{filters.instrument !== "all" ? ` · ${instruments[filters.instrument]}` : ""}{filters.level !== "all" ? ` · ${filters.level === "unknown" ? "Değerlendirilmedi" : "Seviye " + filters.level + " önerisi"}` : ""}{filters.category !== "all" ? ` · ${categories[filters.category]}` : ""}{filters.style !== "all" ? ` · ${styles[filters.style]}` : ""}{filters.solo ? " · Solo" : ""}</span>
            {activeFilters && <button onClick={resetFilters}>Temizle</button>}
          </div>
          {view === "saved" && <p className={css.explanation}>Çalışma listen bu tarayıcıda saklanır; hesabınla eşitlenmez.</p>}
          <div className={css.columnHead} aria-hidden="true"><span>Eser / besteci</span><span>Enstrüman / koleksiyon</span><span>Seviye</span><span /></div>
          <div role="list" aria-label="Nota sonuçları">
            {rows.map(piece => <article className={css.scoreRow} key={piece.id} role="listitem">
              <button className={css.openScore} onClick={() => openPiece(piece)} aria-label={`${piece.title}: notayı aç`}>
                <span className={css.identity}><strong>{piece.title}</strong><small>{piece.composer}{piece.source === "mutopia" && piece.opus && !piece.title.includes(piece.opus) ? ` · ${piece.opus}` : ""}{piece.source === "book" && <span className={css.mobileOnly}> · {piece.book.title}, s. {piece.page}</span>}</small></span>
                <span className={css.context}><span>{piece.instrumentation}</span><small>{piece.source === "book" ? <><b className={css.bookTag}>{scoreFiles?.has(piece.id) ? "PDF" : "Kitap"}</b>{piece.book.title} · s. {piece.page}</> : piece.source === "custom" ? <><b className={css.bookTag}>{scoreFiles?.has(piece.id) ? "PDF" : "Okul"}</b>Okulun eklediği · {categories[piece.category]}</> : <>{categories[piece.category]}{piece.style ? ` · ${styles[piece.style] ?? piece.style}` : ""}</>}</small></span>
                <span className={piece.level ? css.level : css.unrated}>{piece.level ? `${piece.level} · Öneri` : "Değerlendirilmedi"}</span>
              </button>
              <button className={css.save} aria-pressed={saved.includes(piece.id)} aria-label={`${piece.title}: ${saved.includes(piece.id) ? "listemden çıkar" : "listeme ekle"}`} onClick={() => savePiece(piece)}><Icon name={saved.includes(piece.id) ? "check" : "plus"} /></button>
            </article>)}
          </div>
          {!rows.length && <div className={css.empty}><Icon name="search" /><h2>{view === "saved" && !saved.length ? "Çalışma listen henüz boş" : "Bu filtrelerle nota bulunamadı"}</h2><p>{view === "saved" && !saved.length ? "Eserlerin yanındaki + düğmesiyle listeni oluştur." : "Seviye önerisi bulunmayan eserler için tüm seviyeleri seçebilirsin."}</p><button onClick={() => { resetFilters(); changeView("library"); }}>Tüm notaları göster</button></div>}
          {!!rows.length && <nav className={css.pagination} aria-label="Sonuç sayfaları"><span>{number((currentPage - 1) * PAGE_SIZE + 1)}–{number(Math.min(currentPage * PAGE_SIZE, filtered.length))} / {number(filtered.length)}</span><div><button disabled={currentPage === 1} onClick={() => goToPage(currentPage - 1)} aria-label="Önceki sayfa"><Icon name="arrow-left" /></button><label><span className={css.sr}>Sayfa</span><select aria-label="Sayfa" value={currentPage} onChange={e => goToPage(Number(e.target.value))}>{Array.from({ length: pages }, (_, i) => <option key={i} value={i + 1}>{i + 1} / {pages}</option>)}</select></label><button disabled={currentPage === pages} onClick={() => goToPage(currentPage + 1)} aria-label="Sonraki sayfa"><Icon name="arrow-right" /></button></div></nav>}
        </>}
        {notice && <p className={css.notice} role="status">{notice}</p>}
      </div>
    </div>
    <details className={css.sourceNote}><summary>Kaynak, seviyeler ve kullanım</summary>{bookCount > 0 && <p>Nota kitapları okulun yayıncı izniyle kullandığı basılı kitaplardır; PDF&apos;leri yalnızca öğretmen ve yönetici girişiyle açılır, okul dışıyla paylaşılmaz. Seviye önerisi kitabın kendi sırasına veya serisine dayanır.</p>}<p>&quot;YouTube&apos;da dinle&quot; eser adı ve besteciyle YouTube araması açar; dinleyeceğin yorumu kendin seçersin.</p><p>Açık arşiv notaları <a href="https://www.mutopiaproject.org/" target="_blank" rel="noopener noreferrer">Mutopia Project</a> arşivinden gelir. Her kayıt ayrı bir nota yayınıdır; aynı eserin farklı düzenlemeleri bulunabilir. Dosyalar kaynağında açılır. Bağlantı kontrolü: {checkedAt}. Enstrüman sayıları eşlikli ve topluluk eserlerini de içerir.</p><p>Her notanın lisansı ayrıntısında gösterilir; düzenleyen ve yayına hazırlayan bilgilerini kaynak sayfası ve PDF üzerinde koruyun. Çocuklara uygun düzenlemeyi öğretmen seçer. Seviye önerileri sınav derecesi değildir; değerlendirilmemiş eserler ayrıca işaretlidir.</p></details>
    <dialog ref={dialog} className={css.dialog} onCancel={closePiece} onClick={e => { if (e.target === e.currentTarget) closePiece(); }}>
      {adding && <AddPieceForm defaultInstrument={filters.instrument === "all" ? "piano" : filters.instrument} onDone={pieceAdded} onCancel={closePiece} />}
      {!adding && selected && <ScoreDetail key={selected.id} piece={selected} staff={staff} saved={saved.includes(selected.id)} scoreFile={scoreFiles?.get(selected.id)} onSave={() => savePiece(selected)} onClose={closePiece} />}
    </dialog>
  </section>;
}

type ScoreFileInfo = { version: string };

function ScoreDetail({ piece, staff, saved, scoreFile, onSave, onClose }: { piece: SheetMusic; staff: boolean; saved: boolean; scoreFile?: ScoreFileInfo; onSave: () => void; onClose: () => void }) {
  return <div className={css.detail}>
    <div className={css.detailTop}><span>{piece.source === "book" ? "Nota kitabı" : piece.source === "custom" ? "Okulun eklediği eser" : categories[piece.category]}</span><button onClick={onClose} aria-label="Nota ayrıntısını kapat"><Icon name="close" /></button></div>
    <h2>{piece.title}</h2><p className={css.composer}>{piece.composer}{piece.source === "mutopia" && piece.opus ? ` · ${piece.opus}` : ""}</p>
    <div className={css.detailTags}><span>{piece.instrumentation}</span>{piece.style && <span>{styles[piece.style] ?? piece.style}</span>}<span>{piece.level ? `Seviye ${piece.level} · Öneri` : "Seviye değerlendirilmedi"}</span></div>
    {piece.source === "book" ? <BookDetail piece={piece} saved={saved} scoreFile={scoreFile} onSave={onSave} />
      : piece.source === "custom" ? <CustomDetail piece={piece} saved={saved} scoreFile={scoreFile} onSave={onSave} />
      : <ArchiveDetail piece={piece} saved={saved} onSave={onSave} />}
    {staff && <SuggestToStudent piece={piece} />}
  </div>;
}

function Practice({ piece }: { piece: SheetMusic }) {
  if (!piece.levelBasis || !piece.level) return null;
  return <div className={css.practice}><h3>Çalışma önerisi</h3><p>{piece.levelBasis}</p><p>{learningSteps[piece.level - 1].practice}</p><small>Bu bir çalışma basamağı önerisidir. Düzenleme, yaş ve teknik uygunluk öğretmenle değerlendirilmelidir.</small></div>;
}

function ListenLink({ piece }: { piece: SheetMusic }) {
  return <a href={youtubeSearchUrl(piece)} target="_blank" rel="noopener noreferrer" title="YouTube'da bu eserin kayıtlarını ara"><Icon name="music" />YouTube&apos;da dinle</a>;
}

function SaveButton({ saved, onSave }: { saved: boolean; onSave: () => void }) {
  return <button aria-pressed={saved} onClick={onSave}><Icon name={saved ? "check" : "plus"} />{saved ? "Listemden çıkar" : "Listeme ekle"}</button>;
}

function BookDetail({ piece, saved, scoreFile, onSave }: { piece: BookEntry; saved: boolean; scoreFile?: ScoreFileInfo; onSave: () => void }) {
  const { book } = piece;
  const [preview, setPreview] = useState(false);
  const url = scoreFile ? scoreFileUrl(piece.id, scoreFile.version) : null;
  const pages = piece.endPage > piece.page ? `Sayfa ${piece.page}–${piece.endPage}` : `Sayfa ${piece.page}`;
  return <>
    <div className={css.bookLocation}><Icon name="note" /><div><strong>{book.title}</strong><span>{pages} · {piece.fourHands ? "Dört el bölümü, " : ""}{piece.number}. eser</span></div></div>
    <div className={css.detailActions}>
      {url && <a className={css.primary} href={url} target="_blank" rel="noopener noreferrer"><Icon name="note" />Notayı aç (PDF)</a>}
      <ListenLink piece={piece} />
      <SaveButton saved={saved} onSave={onSave} />
      {url && <button aria-expanded={preview} onClick={() => setPreview(!preview)}>{preview ? "Önizlemeyi kapat" : "Burada önizle"}</button>}
    </div>
    {url && preview && <div className={css.preview}><iframe src={url} title={`${piece.title} notası`} /><p>Önizleme görünmüyorsa <a href={url} target="_blank" rel="noopener noreferrer">notayı yeni sekmede aç.</a></p></div>}
    {!url && <p className={css.explanation}>Bu eserin PDF&apos;i henüz yüklenmedi; nota basılı kitaptan çalışılır.</p>}
    <Practice piece={piece} />
    <dl className={css.metadata}>
      <div><dt>Kitap</dt><dd>{book.title}</dd></div>
      <div><dt>Hazırlayan</dt><dd>{book.authors}</dd></div>
      {(book.publisher || book.edition) && <div><dt>Yayın</dt><dd>{[book.publisher, book.edition].filter(Boolean).join(" · ")}</dd></div>}
    </dl>
    <p className={css.explanation}>Bu nota yayıncı izniyle yalnızca okul içinde kullanılır; dosyayı veya bağlantısını okul dışıyla paylaşma.</p>
  </>;
}

function CustomDetail({ piece, saved, scoreFile, onSave }: { piece: CustomPiece; saved: boolean; scoreFile?: ScoreFileInfo; onSave: () => void }) {
  return <>
    <div className={css.detailActions}><ListenLink piece={piece} /><SaveButton saved={saved} onSave={onSave} /></div>
    <CustomPieceFile piece={piece} scoreFile={scoreFile} />
    <Practice piece={piece} />
  </>;
}

function ArchiveDetail({ piece, saved, onSave }: { piece: ArchiveScore; saved: boolean; onSave: () => void }) {
  const [preview, setPreview] = useState(false);
  return <>
    <div className={css.detailActions}><a className={css.primary} href={piece.downloadUrl} target="_blank" rel="noopener noreferrer"><Icon name="note" />{piece.format === "pdf" ? "PDF notayı aç" : "Nota paketini indir (ZIP)"}</a><ListenLink piece={piece} /><SaveButton saved={saved} onSave={onSave} />{piece.format === "pdf" && <button aria-expanded={preview} onClick={() => setPreview(!preview)}>{preview ? "Önizlemeyi kapat" : "Burada önizle"}</button>}</div>
    {preview && <div className={css.preview}><iframe src={piece.downloadUrl} title={`${piece.title} PDF notası`} referrerPolicy="no-referrer" /><p>Önizleme görünmüyorsa <a href={piece.downloadUrl} target="_blank" rel="noopener noreferrer">PDF’yi yeni sekmede aç.</a></p></div>}
    <Practice piece={piece} />
    <dl className={css.metadata}><div><dt>Lisans</dt><dd>{piece.license}</dd></div>{piece.arranger && <div><dt>Düzenleme</dt><dd>{piece.arranger}</dd></div>}<div><dt>Kaynak baskı</dt><dd>{piece.editionSource || "Kaynak sayfasına bakınız"}</dd></div><div><dt>Katalog</dt><dd>Mutopia Project · {piece.id.replace("mutopia-", "")}</dd></div></dl>
    <div className={css.detailLinks}><a href={piece.sourceUrl} target="_blank" rel="noopener noreferrer">Kaynak ve yayına hazırlayan bilgileri<Icon name="arrow-right" /></a>{piece.midiUrl && <a href={piece.midiUrl} target="_blank" rel="noopener noreferrer">MIDI dosyası</a>}<a href="https://www.mutopiaproject.org/legal.html" target="_blank" rel="noopener noreferrer">Lisans koşulları</a></div>
  </>;
}
