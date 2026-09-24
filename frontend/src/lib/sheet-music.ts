type LibraryEntryBase = {
  id: string;
  title: string;
  composer: string;
  instrumentation: string;
  instruments: string[];
  style: string;
  category: string;
  level: number | null;
  levelBasis: string | null;
};

// An edition from the Mutopia open archive: the score itself is linked at its source.
export type ArchiveScore = LibraryEntryBase & {
  source: "mutopia";
  opus: string;
  arranger: string;
  editionSource: string;
  license: string;
  sourceUrl: string;
  downloadUrl: string;
  format: string;
  previewUrl: string | null;
  midiUrl: string | null;
};

// A piece inside a printed book the school works from. Only the contents-page facts are
// catalogued; the score is read from the book, never stored or served by Abdera.
export type BookEntry = LibraryEntryBase & {
  source: "book";
  book: { id: string; title: string; authors: string; publisher: string; edition: string };
  number: number;
  page: number;
  endPage: number;
  fourHands: boolean;
};

// A piece a teacher or admin added to the library themselves (e.g. drum grooves the open
// archive lacks). Lives in the API (library_pieces), visible only in the staff panel.
export type CustomPiece = LibraryEntryBase & {
  source: "custom";
  pieceId: string;
  notes: string | null;
  canEdit: boolean;
};

export type SheetMusic = ArchiveScore | BookEntry | CustomPiece;

export function fromLibraryPiece(piece: { id: string; entryId: string; title: string; composer: string; instrument: string; category: string; level: number | null; notes: string | null; canEdit: boolean }): CustomPiece {
  return {
    source: "custom", id: piece.entryId, pieceId: piece.id,
    title: piece.title, composer: piece.composer,
    instrumentation: instruments[piece.instrument] ?? piece.instrument, instruments: [piece.instrument],
    style: "", category: piece.category,
    level: piece.level, levelBasis: piece.level ? "Eseri ekleyen öğretmenin seviye önerisi." : null,
    notes: piece.notes, canEdit: piece.canEdit,
  };
}

type SchoolBooks = {
  books: {
    id: string; title: string; authors: string; publisher: string; edition: string;
    category: string; levelBasis: string;
    entries: { id: string; no: number; section?: string; composer: string; title: string; page: number; endPage: number; style: string; level: number | null }[];
  }[];
};
type ArchiveCatalogue = { items: Omit<ArchiveScore, "source">[] };

// Printed school books come first: they are the teacher-verified repertoire the school
// actually teaches from; the open archive follows as the broad reference collection.
// The public /kutuphane page passes null: school books are for signed-in staff only.
export function buildLibrary(schoolBooks: SchoolBooks | null, archive: ArchiveCatalogue): SheetMusic[] {
  const books: BookEntry[] = (schoolBooks?.books ?? []).flatMap(({ id, title, authors, publisher, edition, entries, category, levelBasis }) => entries.map(entry => {
    const fourHands = entry.section === "four-hands";
    const book = { id, title, authors, publisher, edition };
    return {
      source: "book" as const,
      id: entry.id,
      title: entry.title, composer: entry.composer,
      instrumentation: fourHands ? "Piyano, dört el" : "Piyano", instruments: ["piano"],
      style: entry.style, category, level: entry.level, levelBasis: entry.level ? levelBasis : null,
      book, number: entry.no, page: entry.page, endPage: entry.endPage, fourHands,
    };
  }));
  return [...books, ...archive.items.map(item => withPracticeSuggestion({ ...item, source: "mutopia" as const }))];
}

export const instruments: Record<string, string> = {
  all: "Tümü", piano: "Piyano", violin: "Keman", guitar: "Gitar", drums: "Bateri", flute: "Flüt / blok flüt",
  cello: "Çello", voice: "Şan / koro", organ: "Org / klavsen", other: "Diğer",
};
export const categories: Record<string, string> = {
  all: "Tüm koleksiyonlar", education: "Etüt ve eğitim", classical: "Batı klasikleri",
  popular: "Popüler ve film müzikleri", world: "Dünyadan melodiler", children: "Çocuk repertuvarı",
};
export const styles: Record<string, string> = {
  Baroque: "Barok", Classical: "Klasik", Romantic: "Romantik", Renaissance: "Rönesans",
  Folk: "Halk müziği", Jazz: "Caz", Modern: "Modern", Hymn: "İlahi", Song: "Şarkı",
  Technique: "Teknik çalışma", March: "Marş", "Popular / Dance": "Popüler / dans", Gospel: "Gospel",
  Popular: "Popüler", Film: "Film ve müzikal",
};
export const learningSteps = [
  { name: "İlk adımlar", goal: "Nota takibi, düzenli vuruş ve rahat duruş.", practice: "Kısa bir cümleyi yavaş tempoda, ritmi koruyarak çalış." },
  { name: "Alışıyorum", goal: "Temel ritimler, küçük aralıklar ve iki el / yay koordinasyonu.", practice: "Zorlandığın ölçüyü ayır; doğru hareketi birkaç kez tekrarla." },
  { name: "Gelişiyorum", goal: "Cümleleme, artikülasyon ve farklı tonaliteler.", practice: "Cümlelerin başlangıç ve bitişlerini işaretleyerek çalış." },
  { name: "İlerliyorum", goal: "Bağımsız sesler, pozisyon değişimleri ve yorum.", practice: "Teknik çalışmayı parçadaki ilgili cümleyle birleştir." },
  { name: "Sahneye hazırım", goal: "Bütünlük, müzikal anlatım ve performans hazırlığı.", practice: "Eseri kesintisiz çal, kaydını dinle ve öğretmeninle değerlendir." },
];

// A YouTube search, not a fixed video: no single recording can be verified for ~2,100 pieces,
// and a search always resolves while letting the teacher pick the performance to listen to.
export function youtubeSearchUrl(piece: SheetMusic) {
  const composer = piece.composer
    .replace(/\([^)]*\d{3,4}[^)]*\)/g, "") // life dates: "F. Abt (1819–1885)"
    .replace(/\((film|dizi|müzikal|Disney)\)/gi, "");
  const query = [piece.title, composer, piece.instruments.includes("piano") ? "piano" : ""].join(" ").replace(/\s+/g, " ").trim();
  return `https://www.youtube.com/results?search_query=${encodeURIComponent(query)}`;
}

export function normalizeSearch(value: string) {
  return value.toLocaleLowerCase("tr-TR").normalize("NFD").replace(/[\u0300-\u036f]/g, "").replace(/ı/g, "i");
}

export const sources: Record<string, string> = { all: "Tüm kaynaklar", custom: "Okulun eklediği", book: "Nota kitapları", mutopia: "Açık arşiv (Mutopia)" };

export type MusicFilters = { query: string; source: string; instrument: string; category: string; level: string; style: string; solo: boolean };
export const initialMusicFilters: MusicFilters = { query: "", source: "all", instrument: "all", category: "all", level: "all", style: "all", solo: false };

function isSolo(piece: SheetMusic) {
  if (piece.source === "custom") return true;
  return piece.source === "book" ? !piece.fourHands : /^(piano|violin|guitar|flute|recorder|cello|organ|harpsichord)( solo)?$/i.test(piece.instrumentation);
}

export function matchesMusic(piece: SheetMusic, filters: MusicFilters) {
  const terms = normalizeSearch(filters.query).split(/\s+/).filter(Boolean);
  const text = normalizeSearch([piece.title, piece.composer, piece.source === "book" ? piece.book.title : piece.source === "mutopia" ? piece.opus : piece.notes ?? "", piece.instrumentation, categories[piece.category], styles[piece.style], ...piece.instruments.map(i => instruments[i])].join(" "));
  return (filters.source === "all" || piece.source === filters.source)
    && (filters.instrument === "all" || piece.instruments.includes(filters.instrument))
    && (filters.category === "all" || piece.category === filters.category)
    && (filters.level === "all" || (filters.level === "unknown" ? piece.level === null : piece.level === Number(filters.level)))
    && (filters.style === "all" || piece.style === filters.style)
    && (!filters.solo || isSolo(piece))
    && terms.every(term => text.includes(term));
}

// These are broad Abdera practice suggestions, not examined grades or a claim of
// teacher approval. Only recognisable pedagogical works receive a suggestion.
function withPracticeSuggestion(piece: ArchiveScore): ArchiveScore {
  let level: number | null = null;
  let basis: string | null = null;
  const text = normalizeSearch(piece.title + " " + piece.opus);
  const composer = normalizeSearch(piece.composer);
  if (composer.includes("burgmuller") && /\b100\b/.test(text)) {
    level = 3; basis = "Burgmüller Op. 100: cümleleme ve artikülasyon çalışmaları.";
  } else if (composer.includes("czerny") && /\b599\b/.test(text)) {
    const number = text.match(/(?:no\.?|nr\.?)\s*(\d+)/)?.[1];
    if (number) { level = Number(number) <= 10 ? 1 : Number(number) <= 30 ? 2 : 3; basis = "Czerny Op. 599 içindeki çalışma sırasına göre başlangıç önerisi."; }
  } else if (composer.includes("bach") && /invention|sinfonia/.test(text)) {
    level = /sinfonia/.test(text) ? 4 : 3; basis = "Bağımsız sesler ve kontrpuan çalışması için öneri.";
  } else if (composer.includes("kreutzer") && /etude|study|studies/.test(text)) {
    level = 4; basis = "Kreutzer etütleri: yay tekniği ve sol el koordinasyonu için öneri.";
  } else if (composer.includes("paganini") && /capric/.test(text)) {
    level = 5; basis = "İleri keman tekniği ve performans çalışması için öneri.";
  } else if (/twinkle|frere jacques/.test(text)) {
    level = 1; basis = "Tanıdık melodi çalışması; düzenlemenin zorluğunu öğretmeninle kontrol et.";
  }
  return { ...piece, level, levelBasis: basis };
}
