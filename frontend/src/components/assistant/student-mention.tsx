"use client";

import { useMemo } from "react";
import { initials } from "@/lib/assistant";
import { useStudentOverviews } from "@/lib/people";

// Sohbet kutusunda öğrenci adı tamamlama. İki yol: "@" yazınca liste açıkça açılır; ad yazılırken
// (en az 2 harf) eşleşen öğrenci varsa liste kendiliğinden görünür. Seçilen ad tam haliyle yazılır
// ve öğrencinin kimliği mesajla birlikte bağlam olarak gider; aynı soyadlı iki öğrencide model
// tahmin etmek zorunda kalmaz.
export interface MentionCandidate {
  id: string;
  fullName: string;
  meta: string;
  inactive: boolean;
  initials: string;
  firstKey: string;
  lastKey: string;
}

export interface MentionQuery {
  // Değiştirilecek aralık (explicit ise "@" dahil).
  start: number;
  end: number;
  text: string;
  explicit: boolean;
}

const MAX_SUGGESTIONS = 6;
const FOLD: Record<string, string> = { ç: "c", ğ: "g", ı: "i", ö: "o", ş: "s", ü: "u", â: "a", î: "i", û: "u" };

// Büyük/küçük harf ve Türkçe işaret farkı gözetilmez: "sahin" → "Şahin".
export function foldTr(text: string) {
  return text.toLocaleLowerCase("tr-TR").replace(/[çğıöşüâîû]/g, (char) => FOLD[char] ?? char);
}

export function useMentionCandidates(enabled: boolean): MentionCandidate[] {
  const overviews = useStudentOverviews({ enabled });
  return useMemo(() => (overviews.data ?? [])
    .map(({ student, instruments }) => {
      const fullName = `${student.firstName} ${student.lastName}`;
      return {
        id: student.id,
        fullName,
        meta: instruments.map((item) => item.instrumentName).join(", "),
        inactive: student.status !== "Active",
        initials: initials(fullName),
        firstKey: foldTr(student.firstName),
        lastKey: foldTr(student.lastName),
      };
    })
    // Aktifler önce, sonra Türkçe alfabe.
    .sort((a, b) => Number(a.inactive) - Number(b.inactive) || a.fullName.localeCompare(b.fullName, "tr-TR")), [overviews.data]);
}

// İmlecin hemen solundaki ad parçası. Kesmeden sonra gelen ek ("Lara'nın") ad sayılmaz.
export function mentionQueryAt(value: string, caret: number): MentionQuery | null {
  if (/^\p{L}/u.test(value.slice(caret))) return null;
  const before = value.slice(0, caret);
  const explicit = before.match(/(?:^|\s)@(\p{L}*(?: \p{L}*)?)$/u);
  if (explicit) {
    const text = explicit[1] ?? "";
    return { start: caret - text.length - 1, end: caret, text, explicit: true };
  }
  const word = before.match(/(?:^|[^\p{L}'’])(\p{L}{2,}(?: \p{L}+)?)$/u);
  if (!word) return null;
  const text = word[1] ?? "";
  return { start: caret - text.length, end: caret, text, explicit: false };
}

export function matchCandidates(candidates: MentionCandidate[], query: MentionQuery): MentionCandidate[] {
  const folded = foldTr(query.text).trim();
  if (!folded) return query.explicit ? candidates.slice(0, MAX_SUGGESTIONS) : [];
  const [first = "", second] = folded.split(" ");
  const matches = candidates.filter((candidate) => second !== undefined
    // İki kelime: ad + soyadın başı ("lara ar"); ikinci kelime boşsa yalnız adın tamamı.
    ? candidate.firstKey.split(" ").some((part) => part === first) && candidate.lastKey.startsWith(second)
    : candidate.firstKey.split(" ").some((part) => part.startsWith(first)) || candidate.lastKey.startsWith(first));
  return matches.slice(0, MAX_SUGGESTIONS);
}

// İki kelimelik sorgu eşleşmezse ("Ece dersini") yalnız son kelime denenir.
export function resolveMention(candidates: MentionCandidate[], value: string, caret: number) {
  const query = mentionQueryAt(value, caret);
  if (!query) return null;
  const matches = matchCandidates(candidates, query);
  if (matches.length || query.explicit) return { query, matches };
  const space = query.text.lastIndexOf(" ");
  if (space < 0) return null;
  const single = { ...query, start: query.start + space + 1, text: query.text.slice(space + 1) };
  if (single.text.length < 2) return null;
  const singleMatches = matchCandidates(candidates, single);
  return singleMatches.length ? { query: single, matches: singleMatches } : null;
}

export function MentionList({ id, items, activeIndex, explicit, onPick, onHover }: {
  id: string;
  items: MentionCandidate[];
  activeIndex: number;
  explicit: boolean;
  onPick: (candidate: MentionCandidate) => void;
  onHover: (index: number) => void;
}) {
  return (
    <div className="assistant-rise absolute inset-x-0 bottom-full z-20 mb-2 overflow-hidden rounded-2xl border border-[var(--line)] bg-white shadow-[0_18px_40px_rgba(84,43,28,.16)]">
      <p className="flex items-center justify-between border-b border-[var(--line)] px-3 py-1.5 text-[.7rem] font-semibold text-[var(--muted)]">
        <span>Öğrenciler</span>
        <span><kbd className="font-sans">↑↓</kbd> gez · <kbd className="font-sans">{explicit ? "Enter" : "Tab"}</kbd> seç · <kbd className="font-sans">Esc</kbd> kapat</span>
      </p>
      {items.length === 0 ? (
        <p className="px-3 py-3 text-sm text-[var(--muted)]">Bu adla öğrenci yok.</p>
      ) : (
        <ul id={id} role="listbox" aria-label="Öğrenci önerileri" className="max-h-64 overflow-y-auto py-1">
          {items.map((candidate, index) => (
            <li
              key={candidate.id}
              id={`${id}-${index}`}
              role="option"
              aria-selected={index === activeIndex}
              // Tıklama metin kutusundan odağı almasın; seçimden sonra yazmaya devam edilir.
              onMouseDown={(event) => { event.preventDefault(); onPick(candidate); }}
              onMouseEnter={() => onHover(index)}
              className={`flex cursor-pointer items-center gap-2.5 px-3 py-2 ${index === activeIndex ? "bg-[var(--brand-soft)]" : ""}`}
            >
              <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-[var(--surface-muted)] text-[.7rem] font-extrabold text-[var(--brand-strong)]">{candidate.initials}</span>
              <span className="min-w-0">
                <span className="block truncate text-sm font-bold">{candidate.fullName}{candidate.inactive ? <span className="font-medium text-[var(--muted)]"> · pasif</span> : null}</span>
                {candidate.meta && <span className="block truncate text-xs text-[var(--muted)]">{candidate.meta}</span>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
