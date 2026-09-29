// Ders bloğu renkleri - enstrümana göre ton ataması. Aynı harita hem dashboard'daki
// haftalık önizlemede (dashboard/page.tsx) hem tam takvim sayfasında (dashboard/calendar/page.tsx)
// kullanılır, böylece bir enstrüman uygulamanın her yerinde aynı renkte görünür.
// Renk enstrümanın ADINA sabitlenir: önceden görünme sırasına göre dağıtılıyordu, aynı enstrüman
// haftadan haftaya renk değiştiriyor ve ilk iki ton (amber/şeftali) birbirinden ayırt
// edilemiyordu. Tonlar kasıtlı olarak farklı renk ailelerinden seçildi; turuncu marka/bugün
// vurgusuyla karışmasın diye kullanılmaz.
export const INSTRUMENT_TONES = [
  { bg: "#dbe7f7", border: "#3f6fb0", text: "#1f3f6b" }, // mavi
  { bg: "#fde3b8", border: "#c98a1f", text: "#7a4a09" }, // amber
  { bg: "#f6d3e3", border: "#b0507a", text: "#7a2f52" }, // gül
  { bg: "#d9ead3", border: "#4f8a3c", text: "#2f5424" }, // yeşil
  { bg: "#e6dcf6", border: "#6b4fa0", text: "#4b3777" }, // lavanta
  { bg: "#e0dbc4", border: "#7d8a4a", text: "#48521f" }, // zeytin
] as const;

export type InstrumentTone = (typeof INSTRUMENT_TONES)[number];

const FIXED_TONE_INDEX: Record<string, number> = {
  piyano: 0,
  gitar: 1,
  keman: 2,
  bateri: 3,
};

function toneIndexFor(name: string): number {
  const key = name.trim().toLocaleLowerCase("tr-TR");
  const fixed = FIXED_TONE_INDEX[key];
  if (fixed !== undefined) return fixed;
  // Bilinmeyen enstrüman: sabit dörtlünün dışındaki tonlardan, adın özetine göre - sıraya değil.
  const spare = INSTRUMENT_TONES.length - 4;
  let hash = 0;
  for (const char of key) hash = (hash * 31 + char.charCodeAt(0)) >>> 0;
  return 4 + (hash % spare);
}

export function buildInstrumentColorMap(instrumentNames: Iterable<string>): Map<string, InstrumentTone> {
  const unique = [...new Set(instrumentNames)];
  return new Map(unique.map((name) => [name, INSTRUMENT_TONES[toneIndexFor(name)]]));
}
