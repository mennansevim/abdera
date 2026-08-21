// Ders bloğu renkleri - enstrümana göre pastel ton ataması. Aynı harita hem dashboard'daki
// haftalık önizlemede (dashboard/page.tsx) hem tam takvim sayfasında (dashboard/calendar/page.tsx)
// kullanılır, böylece bir enstrüman uygulamanın her yerinde aynı renkte görünür.
export const INSTRUMENT_TONES = [
  { bg: "#f9e5c3", border: "#c48212", text: "#654107" },
  { bg: "#ffdcd2", border: "#d45e3e", text: "#71301f" },
  { bg: "#f6d8e7", border: "#b95788", text: "#642a48" },
  { bg: "#cfecea", border: "#2e918c", text: "#225b58" },
  { bg: "#dfddf8", border: "#6555ad", text: "#3d3470" },
] as const;

export type InstrumentTone = (typeof INSTRUMENT_TONES)[number];

export function buildInstrumentColorMap(instrumentNames: Iterable<string>): Map<string, InstrumentTone> {
  const unique = [...new Set(instrumentNames)];
  return new Map(unique.map((name, index) => [name, INSTRUMENT_TONES[index % INSTRUMENT_TONES.length]]));
}
