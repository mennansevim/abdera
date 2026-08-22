// Ders bloğu renkleri - enstrümana göre pastel ton ataması. Aynı harita hem dashboard'daki
// haftalık önizlemede (dashboard/page.tsx) hem tam takvim sayfasında (dashboard/calendar/page.tsx)
// kullanılır, böylece bir enstrüman uygulamanın her yerinde aynı renkte görünür.
// "Sıcak Atölye" yön değişimi: pastel ton yerine daha doygun/sıcak bir palet - redesign/sicak-atolye.
export const INSTRUMENT_TONES = [
  { bg: "#fde3b8", border: "#c98a1f", text: "#7a4a09" },
  { bg: "#ffd8c2", border: "#d9662a", text: "#8a3a1c" },
  { bg: "#f6d3e3", border: "#b0507a", text: "#7a2f52" },
  { bg: "#e0dbc4", border: "#7d8a4a", text: "#48521f" },
  { bg: "#e6dcf6", border: "#7d3a56", text: "#4b3777" },
] as const;

export type InstrumentTone = (typeof INSTRUMENT_TONES)[number];

export function buildInstrumentColorMap(instrumentNames: Iterable<string>): Map<string, InstrumentTone> {
  const unique = [...new Set(instrumentNames)];
  return new Map(unique.map((name, index) => [name, INSTRUMENT_TONES[index % INSTRUMENT_TONES.length]]));
}
