"use client";

import { useState, type ReactNode } from "react";
import { Icon } from "@/components/icons";
import { usePreviousLessonNote, type PreviousLessonNote } from "@/lib/attendance";

// Not formlarının üstündeki kısayollar. Kullanıcı isteği: "bir önceki şarkıya devam
// ettiklerinde bunu seçemiyorlar, notlarda ders devam ediyor butonu gibi kısa yollar olabilir.
// hızlıca butonlarla not ekleme olsun." Üç not formu da (bugünkü ders kartı, yorum bekleyen
// dersler, gelişim sayfası) aynı çubuğu kullanır; form durumunu kendisi tutmaz, yalnızca
// alanlara yazılacak değeri onApply ile bildirir.
//
// "Çalınan eser" alanı not formlarından kaldırıldı (kullanıcı isteği: "çalışılan eser kısmını
// not girişinden kaldır, gerek yok"). Önceki dersin eseri artık ayrı bir alana değil,
// "Ne çalışıldı?" metnine "<eser> (devam)" olarak taşınır.
export interface QuickNoteDraft {
  note: string;
  practiced: string;
  homework: string;
  nextGoal: string;
}

export type QuickNotePatch = Partial<QuickNoteDraft>;

// Öğretmen notuna cümle olarak eklenir; ikinci dokunuş cümleyi geri alır.
const NOTE_PHRASES = [
  "Verimli bir ders oldu.",
  "Ödevini yapmış.",
  "Ödevini yapmamış.",
  "Evde daha çok çalışmalı.",
  "Belirgin ilerleme var.",
  "Dikkati dağınıktı.",
];

// "Ne çalışıldı?" alanına virgülle eklenir.
const PRACTICE_TOPICS = ["Gam", "Etüt", "Ritim", "Nota okuma", "Teknik", "Eser"];

export function QuickNoteBar({ lessonId, draft, onApply }: { lessonId: string; draft: QuickNoteDraft; onApply: (patch: QuickNotePatch) => void }) {
  const { data } = usePreviousLessonNote(lessonId);
  const previous = data?.previous ?? null;
  const [continued, setContinued] = useState<string | null>(null);

  return (
    <div className="space-y-2.5">
      {previous && (
        <ContinueButton
          previous={previous}
          applied={continued === lessonId}
          onClick={() => {
            onApply(continuePatch(previous, draft));
            setContinued(lessonId);
          }}
        />
      )}
      <ChipRow label="Hızlı not">
        {NOTE_PHRASES.map((phrase) => {
          const active = draft.note.includes(phrase);
          return <Chip key={phrase} active={active} onClick={() => onApply({ note: toggleSentence(draft.note, phrase) })}>{phrase.replace(/\.$/, "")}</Chip>;
        })}
      </ChipRow>
      <ChipRow label="Çalışılan">
        {PRACTICE_TOPICS.map((topic) => {
          const active = splitList(draft.practiced).some((item) => sameText(item, topic));
          return <Chip key={topic} active={active} onClick={() => onApply({ practiced: toggleListItem(draft.practiced, topic) })}>{topic}</Chip>;
        })}
      </ChipRow>
    </div>
  );
}

function ContinueButton({ previous, applied, onClick }: { previous: PreviousLessonNote; applied: boolean; onClick: () => void }) {
  const when = new Date(previous.lessonStartAt).toLocaleDateString("tr-TR", { day: "numeric", month: "long" });
  const carried = [
    previousPracticed(previous),
    previous.homework && `Ödev: ${previous.homework}`,
    previous.nextGoal && `Hedef: ${previous.nextGoal}`,
  ].filter(Boolean).join(" · ") || "Önceki dersin notu";

  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={applied}
      className={`pressable flex w-full items-center gap-3 rounded-xl border p-2.5 text-left ${applied ? "border-[color:var(--success)] bg-[var(--success-soft)]" : "border-[var(--brand)] bg-[var(--brand-soft)]"}`}
    >
      <span className={`grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-white ${applied ? "text-[var(--success-strong)]" : "text-[var(--brand)]"}`}>
        <Icon name={applied ? "check" : "music"} className="h-4 w-4" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-xs font-bold">{applied ? "Önceki dersten aktarıldı" : "Ders devam ediyor"}</span>
        <span className="text-meta mt-0.5 block truncate">{when} · {carried}</span>
      </span>
    </button>
  );
}

function ChipRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-1.5">
      <span className="text-meta mr-0.5 font-bold">{label}</span>
      {children}
    </div>
  );
}

function Chip({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={`pressable min-h-8 rounded-full border px-2.5 text-[.75rem] font-bold ${active ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand)]" : "border-[var(--line)] bg-white text-[var(--muted)]"}`}
    >
      {children}
    </button>
  );
}

// Önceki dersin "Ne çalışıldı?"sı, ödevi ve hedefi taşınır - ama öğretmenin bu forma zaten
// yazdığı bir alan ezilmez. "Ne çalışıldı?" boş bırakılmış eski bir notta yalnızca eser adı
// varsa (eser alanı kaldırılmadan önce yazılmış notlar) "<eser> (devam)" kullanılır. Sunucu
// eseri yalnızca son nottan verir (LessonNotes.PreviousAsync); metin olduğu gibi kopyalandığı
// için "(devam)" dersten derse zincirlenmez.
function previousPracticed(previous: PreviousLessonNote) {
  return previous.practiced || (previous.pieceTitle ? `${previous.pieceTitle} (devam)` : null);
}

function continuePatch(previous: PreviousLessonNote, draft: QuickNoteDraft): QuickNotePatch {
  const patch: QuickNotePatch = {};
  const practiced = previousPracticed(previous);
  if (practiced && !draft.practiced.trim()) patch.practiced = practiced;
  if (previous.homework && !draft.homework.trim()) patch.homework = previous.homework;
  if (previous.nextGoal && !draft.nextGoal.trim()) patch.nextGoal = previous.nextGoal;
  return patch;
}

function toggleSentence(text: string, sentence: string) {
  if (text.includes(sentence)) return text.replace(sentence, "").replace(/\s{2,}/g, " ").trim();
  const trimmed = text.trim();
  return trimmed ? `${trimmed} ${sentence}` : sentence;
}

function splitList(text: string) {
  return text.split(",").map((item) => item.trim()).filter(Boolean);
}

function sameText(a: string, b: string) {
  return a.toLocaleLowerCase("tr-TR") === b.toLocaleLowerCase("tr-TR");
}

function toggleListItem(text: string, item: string) {
  const items = splitList(text);
  const index = items.findIndex((existing) => sameText(existing, item));
  if (index >= 0) items.splice(index, 1);
  else items.push(item);
  return items.join(", ");
}
