"use client";

import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Icon } from "@/components/icons";
import { Modal } from "@/components/ui";
import { errorMessage } from "@/lib/library";
import { useMe } from "@/lib/use-auth";
import { STUDENT_INTENTS, useResolveAssistant, type AssistantResolution, type AssistantStudent } from "@/lib/assistant";
import type { AssistantRequest } from "./assistant-provider";
import { DuesCard } from "./dues-card";
import { LessonsCard } from "./lessons-card";
import { MakeupFlow } from "./makeup-flow";
import { NavigateCard } from "./navigate-card";
import { ProgressCard } from "./progress-card";
import { RescheduleFlow } from "./reschedule-flow";
import { Choice } from "./shared";
import { WelcomeSummary } from "./welcome-summary";

// Sohbet: kullanıcının cümlesi sunucuda çözülür, yanıt bir kart olarak çizilir. Kartlar
// kendi verisini mevcut uçlardan çeker ve yazma işlemini onaydan sonra yapar.
type Entry =
  | { id: number; role: "user"; text: string }
  | { id: number; role: "assistant"; text: string; examples?: string[] }
  | { id: number; role: "assistant"; pick: AssistantStudent[]; resolution: AssistantResolution }
  | { id: number; role: "assistant"; card: AssistantResolution; student: AssistantStudent | null };

type NewEntry = Entry extends infer E ? (E extends unknown ? Omit<E, "id"> : never) : never;

const ADMIN_EXAMPLES = ["Aidat günü gelenleri listele", "Yoklamayı nereden alırım?"];
const TEACHER_EXAMPLES = ["Yoklamayı nereden alırım?", "Ders notu nasıl yazılır"];
const FALLBACK_TEXT = "Bunu anlayamadım. Öğrencinin gelişimini getirebilir, aidat günü gelenleri listeleyebilir, ders saatini değiştirebilir, telafi planlayabilir ya da seni bir ekrana götürebilirim. Öğrencinin adını yazmayı unutma.";

export function AssistantPanel({ open, onClose, request }: { open: boolean; onClose: () => void; request: AssistantRequest | null }) {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const [entries, setEntries] = useState<Entry[]>([]);
  const [input, setInput] = useState("");
  // Öğrencisiz gelen istek ("gelişimini getir"): sıradaki mesaj yalnızca ad olabilir.
  const [pending, setPending] = useState<AssistantResolution | null>(null);
  const resolve = useResolveAssistant();
  const nextId = useRef(1);
  const handledRequest = useRef(0);
  const endRef = useRef<HTMLDivElement>(null);

  function push(entry: NewEntry) {
    const id = nextId.current++;
    setEntries((current) => [...current, { ...entry, id } as Entry]);
  }

  function show(resolution: AssistantResolution) {
    if (!resolution.allowed) {
      push({ role: "assistant", text: resolution.kind === "Navigate" && resolution.navigation
        ? `${resolution.navigation.label} ekranı yalnızca yöneticiye açık.`
        : "Aidat bilgileri yalnızca yöneticiye açık." });
      return;
    }

    let effective = resolution;
    if (resolution.kind === "Unknown" && pending && resolution.students.length > 0) effective = { ...pending, students: resolution.students };
    setPending(null);

    if (effective.kind === "Unknown") {
      push({ role: "assistant", text: FALLBACK_TEXT, examples: isAdmin ? ["Aidat günü gelenleri listele"] : ["Yoklamayı nereden alırım?"] });
      return;
    }

    if (STUDENT_INTENTS.includes(effective.kind)) {
      if (effective.students.length === 0) {
        setPending(effective);
        push({ role: "assistant", text: "Hangi öğrenci için? Adını ve soyadını yazabilirsin." });
        return;
      }
      if (effective.students.length > 1) {
        push({ role: "assistant", pick: effective.students, resolution: effective });
        return;
      }
    }

    push({ role: "assistant", card: effective, student: effective.students.length === 1 ? effective.students[0]! : null });
  }

  function send(text: string, studentId?: string) {
    const trimmed = text.trim();
    if (!trimmed || resolve.isPending) return;
    push({ role: "user", text: trimmed });
    setInput("");
    resolve.mutate({ text: trimmed, studentId }, {
      onSuccess: show,
      onError: (error) => push({ role: "assistant", text: errorMessage(error, "Şu an yanıt veremiyorum; bağlantını kontrol edip tekrar dene.") }),
    });
  }

  function pick(student: AssistantStudent, resolution: AssistantResolution) {
    push({ role: "user", text: student.fullName });
    push({ role: "assistant", card: { ...resolution, students: [student] }, student });
  }

  // Kısayoldan gelen istek (öğrenci satırı, kart düğmesi) bir kez gönderilir.
  useEffect(() => {
    if (!request || request.id === handledRequest.current) return;
    handledRequest.current = request.id;
    send(request.text, request.studentId);
  });

  useEffect(() => {
    endRef.current?.scrollIntoView({ block: "end", behavior: "smooth" });
  }, [entries.length, resolve.isPending]);

  function reset() {
    setEntries([]);
    setPending(null);
    setInput("");
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    send(input);
  }

  return (
    <Modal open={open} title="Abdera Asistanı" description="Beta · Bilgi getirir, ekrana götürür; değişiklikleri senin onayınla yapar." onClose={onClose} size="md">
      <div className="flex min-h-[min(34rem,70dvh)] flex-col">
        <div aria-live="polite" className="flex-1 space-y-3 pb-4">
          <Bubble role="assistant">
            <p>Merhaba{me?.name ? ` ${me.name.split(" ")[0]}` : ""}. Öğrenci bilgisini getirebilir, ders saatini değiştirebilir, telafi planlayabilir ya da seni doğru ekrana götürebilirim.</p>
            {open && me && <WelcomeSummary isAdmin={isAdmin} />}
            {entries.length === 0 && <Examples items={isAdmin ? ADMIN_EXAMPLES : TEACHER_EXAMPLES} onPick={send} />}
          </Bubble>

          {entries.map((entry) => {
            if (entry.role === "user") return <Bubble key={entry.id} role="user"><p>{entry.text}</p></Bubble>;
            if ("text" in entry) return <Bubble key={entry.id} role="assistant"><p>{entry.text}</p>{entry.examples && <Examples items={entry.examples} onPick={send} />}</Bubble>;
            if ("pick" in entry) {
              return (
                <Bubble key={entry.id} role="assistant">
                  <p>Birden fazla öğrenci eşleşti. Hangisi?</p>
                  <div className="flex flex-wrap gap-2" role="group" aria-label="Öğrenci seç">
                    {entry.pick.map((student) => (
                      <Choice key={student.studentId} onClick={() => pick(student, entry.resolution)}>
                        {student.fullName}{student.courses[0] ? ` · ${student.courses[0].instrumentName}` : ""}{student.status === "Inactive" ? " · pasif" : ""}
                      </Choice>
                    ))}
                  </div>
                </Bubble>
              );
            }
            return <Bubble key={entry.id} role="assistant" wide>{renderCard(entry.card, entry.student)}</Bubble>;
          })}

          {resolve.isPending && <Bubble role="assistant"><p className="text-meta">Bakıyorum…</p></Bubble>}
          <div ref={endRef} />
        </div>

        <form onSubmit={handleSubmit} className="sticky bottom-[-1rem] -mx-4 mt-auto flex gap-2 border-t border-[var(--line)] bg-[var(--surface)] px-4 pb-[max(1rem,env(safe-area-inset-bottom))] pt-3 sm:-mx-5 sm:px-5">
          <label className="min-w-0 flex-1">
            <span className="sr-only">Asistana yaz</span>
            <input
              value={input}
              onChange={(event) => setInput(event.target.value)}
              maxLength={300}
              placeholder="Örn. Mennan Sevim'in gelişimini getir"
              autoFocus
              className="field text-sm"
            />
          </label>
          <button type="submit" disabled={!input.trim() || resolve.isPending} aria-label="Gönder" className="icon-btn icon-btn-brand shrink-0 disabled:opacity-50">
            <Icon name="arrow-right" className="h-4 w-4" />
          </button>
          <button type="button" onClick={reset} aria-label="Sohbeti temizle" title="Sohbeti temizle" className="icon-btn icon-btn-quiet shrink-0">
            <Icon name="x" className="h-4 w-4" />
          </button>
        </form>
      </div>
    </Modal>
  );
}

function renderCard(resolution: AssistantResolution, student: AssistantStudent | null) {
  switch (resolution.kind) {
    case "Progress": return student && <ProgressCard student={student} />;
    case "Dues": return <DuesCard student={student} />;
    case "Lessons": return student && <LessonsCard student={student} />;
    case "Reschedule": return student && <RescheduleFlow student={student} move={resolution.move} />;
    case "Makeup": return student && <MakeupFlow student={student} />;
    case "Navigate": return resolution.navigation && <NavigateCard navigation={resolution.navigation} />;
    default: return null;
  }
}

function Bubble({ role, wide = false, children }: { role: "assistant" | "user"; wide?: boolean; children: ReactNode }) {
  return (
    <div className={`flex ${role === "user" ? "justify-end" : "justify-start"}`}>
      <div className={`${wide ? "w-full" : "max-w-[88%]"} space-y-2.5 rounded-2xl px-3.5 py-2.5 text-sm leading-relaxed ${role === "user" ? "rounded-br-md bg-[var(--brand)] text-white" : "rounded-bl-md bg-[var(--surface-muted)] text-[var(--foreground)]"}`}>
        {children}
      </div>
    </div>
  );
}

function Examples({ items, onPick }: { items: string[]; onPick: (text: string) => void }) {
  return (
    <div className="flex flex-wrap gap-2" aria-label="Örnek istekler">
      {items.map((item) => <Choice key={item} onClick={() => onPick(item)}>{item}</Choice>)}
    </div>
  );
}
