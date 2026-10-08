"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { QuickNoteBar, type QuickNotePatch } from "@/components/quick-note-bar";
import { ApiError } from "@/lib/api";
import { useCreateLessonNote } from "@/lib/attendance";
import { usePendingLessonNotes, type PendingLessonNote } from "@/lib/progress";

// Yorum bekleyen dersler: yoklaması "geldi" girilmiş ama notu yazılmamış, bitmiş dersler.
// Kullanıcı isteği: "öğretmenlere tamamlanan dersler ile ilgili yorum girmelerini
// hatırlatmalara ekleyelim eğer girmedilerse." Kart Gelişim ekranının başında durur; ana
// ekrandan kaldırıldı ("bildirimleri ana ekrandan kaldır"). Zildeki "n dersin yorumu bekliyor"
// hatırlatması buraya (/dashboard/progress#yorum-bekleyen-dersler) getirir; not buradan yazılır
// ve ders listeden düşer. Bekleyen ders yoksa kart hiç görünmez.
export function PendingLessonNotes() {
  const { data } = usePendingLessonNotes();
  const [openId, setOpenId] = useState<string | null>(null);
  const items = data?.items ?? [];
  if (!items.length) return null;

  return (
    <section id="yorum-bekleyen-dersler" aria-labelledby="yorum-bekleyen-dersler-baslik" className="app-card mb-3 scroll-mt-4 overflow-hidden">
      <header className="flex items-start gap-3 border-b border-[var(--line)] p-3 sm:p-4">
        <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--warning-soft)] text-[var(--warning-strong)]"><Icon name="note" className="h-4 w-4" /></span>
        <div className="min-w-0">
          <h2 id="yorum-bekleyen-dersler-baslik" className="text-sm font-bold">Yorum bekleyen dersler · {items.length}</h2>
          <p className="text-meta mt-0.5">Tamamlanan bu derslerin notu henüz girilmedi. Son {data?.lookbackDays ?? 14} günün dersleri listelenir.</p>
        </div>
      </header>
      <ul className="divide-y divide-[var(--line)]">
        {items.map((lesson) => (
          <PendingLessonRow
            key={lesson.lessonId}
            lesson={lesson}
            open={openId === lesson.lessonId}
            onToggle={() => setOpenId(openId === lesson.lessonId ? null : lesson.lessonId)}
          />
        ))}
      </ul>
    </section>
  );
}

function PendingLessonRow({ lesson, open, onToggle }: { lesson: PendingLessonNote; open: boolean; onToggle: () => void }) {
  const start = new Date(lesson.startAt);
  const when = `${start.toLocaleDateString("tr-TR", { day: "numeric", month: "long", weekday: "long" })} · ${start.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" })}`;

  return (
    <li>
      <div className="flex items-center gap-3 px-3 py-2.5 sm:px-4">
        <div className="min-w-0 flex-1">
          <p className="truncate text-xs font-bold">{lesson.studentName} <span className="font-medium text-[var(--muted)]">· {lesson.instrumentName}</span></p>
          <p className="text-meta mt-0.5">{when}</p>
        </div>
        <button type="button" onClick={onToggle} aria-expanded={open} className="btn btn-quiet shrink-0">
          {open ? "Kapat" : "Not ekle"}
        </button>
      </div>
      {open && <PendingLessonNoteForm lessonId={lesson.lessonId} />}
    </li>
  );
}

// Alanlar ve etiketler bugünkü ders kartındaki not formuyla aynı (teacher-today-lessons.tsx).
// Kaydedilince liste yeniden çekilir ve ders buradan kendiliğinden düşer.
function PendingLessonNoteForm({ lessonId }: { lessonId: string }) {
  const createNote = useCreateLessonNote(lessonId);
  const [note, setNote] = useState("");
  const [practiced, setPracticed] = useState("");
  const [homework, setHomework] = useState("");
  const [nextGoal, setNextGoal] = useState("");
  const [error, setError] = useState<string | null>(null);

  function applyQuick(patch: QuickNotePatch) {
    if (patch.note !== undefined) setNote(patch.note);
    if (patch.practiced !== undefined) setPracticed(patch.practiced);
    if (patch.homework !== undefined) setHomework(patch.homework);
    if (patch.nextGoal !== undefined) setNextGoal(patch.nextGoal);
  }

  async function handleSave() {
    if (!note.trim() && !practiced.trim() && !homework.trim() && !nextGoal.trim()) {
      setError("Kaydetmek için kısa bir not eklemelisin.");
      return;
    }
    setError(null);
    try {
      await createNote.mutateAsync({
        note: note.trim() || undefined,
        practiced: practiced.trim() || undefined,
        homework: homework.trim() || undefined,
        nextGoal: nextGoal.trim() || undefined,
      });
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Kaydedilemedi.");
    }
  }

  return (
    <div className="space-y-3 border-t border-[var(--line)] bg-[var(--surface-muted)] p-3 sm:p-4">
      <QuickNoteBar lessonId={lessonId} draft={{ note, practiced, homework, nextGoal }} onApply={applyQuick} />
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="sm:col-span-2"><span className="text-meta mb-1.5 block font-bold">Öğretmen notu <span className="font-medium">· yalnızca ekip görür</span></span><textarea value={note} onChange={(event) => setNote(event.target.value)} rows={2} placeholder="Dersteki ilerleme, dikkat edilmesi gerekenler…" className="field resize-y text-xs" /></label>
        <label><span className="text-meta mb-1.5 block font-bold">Ne çalışıldı?</span><input value={practiced} onChange={(event) => setPracticed(event.target.value)} className="field text-xs" placeholder="Örn. Gam ve etüt" /></label>
        <label><span className="text-meta mb-1.5 block font-bold">Ödev</span><input value={homework} onChange={(event) => setHomework(event.target.value)} className="field text-xs" placeholder="Bir sonraki derse kadar" /></label>
        <label className="sm:col-span-2"><span className="text-meta mb-1.5 block font-bold">Sonraki hedef</span><input value={nextGoal} onChange={(event) => setNextGoal(event.target.value)} className="field text-xs" placeholder="Bir sonraki dersin odağı" /></label>
      </div>
      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] p-3 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
      <button type="button" onClick={handleSave} disabled={createNote.isPending} className="btn btn-primary">{createNote.isPending ? "Kaydediliyor…" : "Notu kaydet"}</button>
    </div>
  );
}
