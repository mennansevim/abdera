import type { ProgressEntry } from "./progress";

export interface PieceInsight {
  title: string;
  averageDifficulty: number;
  appearances: number;
  latestAt: string;
  difficultySource: "teacher" | "assistant";
  difficultyReason: string;
}

export interface ProgressAnalysis {
  noteCount: number;
  pieceCount: number;
  practiceRate: number;
  pieces: PieceInsight[];
}

function normalizedText(entry: ProgressEntry) {
  return [entry.practiced, entry.note, entry.homework, entry.nextGoal]
    .filter(Boolean)
    .join(" ")
    .toLocaleLowerCase("tr-TR");
}

const ADVANCED_DIFFICULTY_SIGNALS = [
  "arpej", "akor geçiş", "entonasyon", "kromatik", "konçerto", "oktav", "polifoni", "pozisyon", "sonat", "hız",
];
const FOUNDATION_DIFFICULTY_SIGNALS = [
  "açık tel", "başlangıç", "kolay", "ilk", "tek el", "temel", "yavaş",
];

function suggestPieceDifficulty(title: string, contexts: string[], appearances: number) {
  const text = [title, ...contexts].join(" ").toLocaleLowerCase("tr-TR");
  const advancedSignals = ADVANCED_DIFFICULTY_SIGNALS.filter((signal) => text.includes(signal));
  const foundationSignals = FOUNDATION_DIFFICULTY_SIGNALS.filter((signal) => text.includes(signal));
  const repetitionAdjustment = appearances >= 4 ? 0.5 : appearances >= 2 ? 0.25 : 0;
  const rawDifficulty = 2.5 + Math.min(1.5, advancedSignals.length * 0.5) - Math.min(1, foundationSignals.length * 0.5) + repetitionAdjustment;
  const value = Math.max(1, Math.min(5, Math.round(rawDifficulty * 2) / 2));

  if (advancedSignals.length) {
    return { value, reason: `Ders notlarındaki “${advancedSignals.slice(0, 2).join("” ve “")}” odağına göre` };
  }
  if (foundationSignals.length) {
    return { value, reason: `Temel çalışma adımlarına göre` };
  }
  return { value, reason: appearances > 1 ? `${appearances} ders kaydındaki çalışma yoğunluğuna göre` : "Eser ve ders notu bağlamına göre" };
}

export function buildProgressAnalysis(entries: ProgressEntry[]): ProgressAnalysis {
  const sorted = [...entries].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const pieces = new Map<string, { difficulties: number[]; appearances: number; latestAt: string; contexts: string[] }>();

  for (const entry of sorted) {
    if (!entry.pieceTitle) continue;
    const existing = pieces.get(entry.pieceTitle) ?? { difficulties: [], appearances: 0, latestAt: entry.createdAt, contexts: [] };
    existing.appearances += 1;
    if (entry.pieceDifficulty) existing.difficulties.push(entry.pieceDifficulty);
    existing.contexts.push(normalizedText(entry));
    if (entry.createdAt > existing.latestAt) existing.latestAt = entry.createdAt;
    pieces.set(entry.pieceTitle, existing);
  }

  const pieceInsights = [...pieces.entries()]
    .map(([title, value]) => {
      const teacherDifficulty = value.difficulties.length
        ? value.difficulties.reduce((sum, difficulty) => sum + difficulty, 0) / value.difficulties.length
        : null;
      const assistantSuggestion = suggestPieceDifficulty(title, value.contexts, value.appearances);
      return {
        title,
        averageDifficulty: teacherDifficulty ?? assistantSuggestion.value,
        appearances: value.appearances,
        latestAt: value.latestAt,
        difficultySource: teacherDifficulty ? "teacher" as const : "assistant" as const,
        difficultyReason: teacherDifficulty
          ? `${value.difficulties.length} öğretmen değerlendirmesinin ortalaması`
          : assistantSuggestion.reason,
      };
    })
    .sort((a, b) => b.latestAt.localeCompare(a.latestAt));

  const practiceRate = sorted.length ? Math.round((sorted.filter((entry) => entry.practiced || entry.note).length / sorted.length) * 100) : 0;

  return {
    noteCount: sorted.length,
    pieceCount: pieceInsights.length,
    practiceRate,
    pieces: pieceInsights,
  };
}
