"use client";

import { Fragment, useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { Icon } from "@/components/icons";
import { Modal } from "@/components/ui";
import { errorMessage } from "@/lib/library";
import { useMe } from "@/lib/use-auth";
import { useAssistantChat, type AssistantCardData, type ChatHistoryItem } from "@/lib/assistant";
import type { AssistantRequest } from "./assistant-provider";
import { DuesCard } from "./dues-card";
import { LessonsCard } from "./lessons-card";
import { MakeupFlow } from "./makeup-flow";
import { NavigateCard } from "./navigate-card";
import { ProgressCard } from "./progress-card";
import { RescheduleFlow } from "./reschedule-flow";
import { Choice } from "./shared";
import { WelcomeSummary } from "./welcome-summary";

// Sohbet (docs/10-decisions.md AE9): mesaj sunucuda dil modeline gider, model okulun verisini
// araçlarla okuyup doğal bir cevap yazar ve gerektiğinde kart açar. Kartlar kendi verisini
// mevcut uçlardan çeker; kayıt değiştiren her iş kartta kullanıcının onayıyla yapılır.
type Entry =
  | { id: number; role: "user"; text: string }
  | { id: number; role: "assistant"; text: string; cards: AssistantCardData[]; error?: boolean };

type NewEntry = Entry extends infer E ? (E extends unknown ? Omit<E, "id"> : never) : never;

const ADMIN_EXAMPLES = ["Aidat günü gelenleri listele", "Bu hafta hangi derslerde değişiklik var?", "Yoklamayı nereden alırım?"];
const TEACHER_EXAMPLES = ["Bugünkü öğrencilerimin durumu", "Yoklamayı nereden alırım?"];

export function AssistantPanel({ open, onClose, request }: { open: boolean; onClose: () => void; request: AssistantRequest | null }) {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const [entries, setEntries] = useState<Entry[]>([]);
  const [input, setInput] = useState("");
  // Sunucunun döndürdüğü maskeli geçmiş; bir sonraki mesajla aynen geri gider.
  const [history, setHistory] = useState<ChatHistoryItem[]>([]);
  const chat = useAssistantChat();
  const nextId = useRef(1);
  const handledRequest = useRef(0);
  const endRef = useRef<HTMLDivElement>(null);

  function push(entry: NewEntry) {
    const id = nextId.current++;
    setEntries((current) => [...current, { ...entry, id } as Entry]);
  }

  function send(text: string, studentId?: string) {
    const trimmed = text.trim();
    if (!trimmed || chat.isPending) return;
    push({ role: "user", text: trimmed });
    setInput("");
    chat.mutate({ message: trimmed, history, studentId }, {
      onSuccess: (response) => {
        setHistory(response.history);
        push({ role: "assistant", text: response.reply, cards: response.cards });
      },
      onError: (error) => push({ role: "assistant", text: errorMessage(error, "Asistan şu an yanıt veremiyor; biraz sonra tekrar dene."), cards: [], error: true }),
    });
  }

  // Kısayoldan gelen istek (öğrenci satırı, kart düğmesi) bir kez gönderilir.
  useEffect(() => {
    if (!request || request.id === handledRequest.current) return;
    handledRequest.current = request.id;
    send(request.text, request.studentId);
  });

  useEffect(() => {
    endRef.current?.scrollIntoView({ block: "end", behavior: "smooth" });
  }, [entries.length, chat.isPending]);

  function reset() {
    setEntries([]);
    setHistory([]);
    setInput("");
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    send(input);
  }

  return (
    <Modal open={open} title="Abdera Asistanı" description="Beta · Sorularını yanıtlar, ekrana götürür; değişiklikleri senin onayınla yapar." onClose={onClose} size="md">
      <div className="flex min-h-[min(34rem,70dvh)] flex-col">
        <div aria-live="polite" className="flex-1 space-y-3 pb-4">
          <Bubble role="assistant">
            <p>Merhaba{me?.name ? ` ${me.name.split(" ")[0]}` : ""}. Bana okulla ilgili her şeyi sorabilirsin: bir öğrencinin gelişimi, aidatlar, ders saatini değiştirmek, telafi planlamak ya da bir işi nerede yapacağın.</p>
            {open && me && <WelcomeSummary isAdmin={isAdmin} />}
            {entries.length === 0 && <Examples items={isAdmin ? ADMIN_EXAMPLES : TEACHER_EXAMPLES} onPick={send} />}
          </Bubble>

          {entries.map((entry) => entry.role === "user"
            ? <Bubble key={entry.id} role="user"><p className="whitespace-pre-wrap">{entry.text}</p></Bubble>
            : (
              <Fragment key={entry.id}>
                {entry.text && (
                  <Bubble role="assistant" tone={entry.error ? "error" : "default"}>
                    <RichText text={entry.text} />
                  </Bubble>
                )}
                {entry.cards.map((card, index) => (
                  <Bubble key={`${entry.id}-${index}`} role="assistant" wide>{renderCard(card)}</Bubble>
                ))}
              </Fragment>
            ))}

          {chat.isPending && <Bubble role="assistant"><Thinking /></Bubble>}
          <div ref={endRef} />
        </div>

        <form onSubmit={handleSubmit} className="sticky bottom-[-1rem] -mx-4 mt-auto flex gap-2 border-t border-[var(--line)] bg-[var(--surface)] px-4 pb-[max(1rem,env(safe-area-inset-bottom))] pt-3 sm:-mx-5 sm:px-5">
          <label className="min-w-0 flex-1">
            <span className="sr-only">Asistana yaz</span>
            <input
              value={input}
              onChange={(event) => setInput(event.target.value)}
              maxLength={1000}
              placeholder="Bir şey sor ya da iste…"
              autoFocus
              className="field text-sm"
            />
          </label>
          <button type="submit" disabled={!input.trim() || chat.isPending} aria-label="Gönder" className="icon-btn icon-btn-brand shrink-0 disabled:opacity-50">
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

function renderCard(card: AssistantCardData) {
  const student = card.student;
  switch (card.kind) {
    case "progress": return student && <ProgressCard student={student} />;
    case "dues": return <DuesCard student={student} />;
    case "lessons": return student && <LessonsCard student={student} />;
    case "reschedule": return student && <RescheduleFlow student={student} move={card.move} />;
    case "makeup": return student && <MakeupFlow student={student} />;
    case "navigate": return card.navigation && <NavigateCard navigation={card.navigation} />;
    default: return null;
  }
}

// Modelin yazdığı sade metin: paragraflar, "-"/"•"/"1." ile başlayan maddeler ve **kalın**.
// Markdown kütüphanesi eklenmez; model başlık ve tablo kullanmaması için yönlendirilir.
function RichText({ text }: { text: string }) {
  const blocks: ReactNode[] = [];
  let list: string[] = [];
  const flush = () => {
    if (list.length) blocks.push(<ul key={`list-${blocks.length}`} className="list-disc space-y-0.5 pl-5">{list.map((item, index) => <li key={index}>{inline(item)}</li>)}</ul>);
    list = [];
  };
  for (const raw of text.split("\n")) {
    const line = raw.trim();
    const bullet = line.match(/^(?:[-•*]|\d+[.)])\s+(.*)$/);
    if (bullet) list.push(bullet[1] ?? "");
    else {
      flush();
      if (line) blocks.push(<p key={`p-${blocks.length}`}>{inline(line)}</p>);
    }
  }
  flush();
  return <>{blocks}</>;
}

function inline(text: string) {
  return text.split(/(\*\*[^*]+\*\*)/g).map((part, index) =>
    part.startsWith("**") && part.endsWith("**") ? <strong key={index}>{part.slice(2, -2)}</strong> : <Fragment key={index}>{part.replace(/^#+\s*/, "")}</Fragment>);
}

function Thinking() {
  return (
    <span className="inline-flex items-center gap-1 text-[var(--muted)]" role="status" aria-label="Asistan düşünüyor">
      <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-current" />
      <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-current [animation-delay:150ms]" />
      <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-current [animation-delay:300ms]" />
    </span>
  );
}

function Bubble({ role, wide = false, tone = "default", children }: { role: "assistant" | "user"; wide?: boolean; tone?: "default" | "error"; children: ReactNode }) {
  const color = role === "user"
    ? "rounded-br-md bg-[var(--brand)] text-white"
    : tone === "error"
      ? "rounded-bl-md bg-[var(--danger-soft)] text-[var(--danger-strong)]"
      : "rounded-bl-md bg-[var(--surface-muted)] text-[var(--foreground)]";
  return (
    <div className={`flex ${role === "user" ? "justify-end" : "justify-start"}`}>
      <div className={`${wide ? "w-full" : "max-w-[88%]"} space-y-2 rounded-2xl px-3.5 py-2.5 text-sm leading-relaxed ${color}`}>
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
