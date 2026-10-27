"use client";

import { Fragment, useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Icon, type IconName } from "@/components/icons";
import { errorMessage } from "@/lib/library";
import { meDisplayName, useMe } from "@/lib/use-auth";
import { useAssistantChat, type AssistantCardData, type ChatHistoryItem } from "@/lib/assistant";
import type { AssistantRequest } from "./assistant-provider";
import { DuesCard } from "./dues-card";
import { LessonsCard } from "./lessons-card";
import { MakeupFlow } from "./makeup-flow";
import { NavigateCard } from "./navigate-card";
import { ProgressCard } from "./progress-card";
import { RescheduleFlow } from "./reschedule-flow";
import { MentionList, resolveMention, useMentionCandidates, type MentionCandidate } from "./student-mention";
import { WelcomeSummary } from "./welcome-summary";

// Sohbet (docs/10-decisions.md AE9): mesaj sunucuda dil modeline gider, model okulun verisini
// araçlarla okuyup doğal bir cevap yazar ve gerektiğinde kart açar. Kartlar kendi verisini
// mevcut uçlardan çeker; kayıt değiştiren her iş kartta kullanıcının onayıyla yapılır.
type Entry =
  | { id: number; role: "user"; text: string }
  | { id: number; role: "assistant"; text: string; cards: AssistantCardData[]; retry?: { text: string; studentId?: string } };

type NewEntry = Entry extends infer E ? (E extends unknown ? Omit<E, "id"> : never) : never;

// Açılıştaki görevler. Öğrenci isteyenler kutuya cümlenin başını yazar ve öğrenci listesini açar;
// ad seçilince istek tamamlanır, kullanıcı gün/saat ekleyebilir.
interface Task {
  icon: IconName;
  title: string;
  hint: string;
  prefix?: string;
  send?: string;
}

const ADMIN_TASKS: Task[] = [
  { icon: "clock", title: "Ders saatini değiştir", hint: "Tek ders ya da her hafta", prefix: "Ders saatini değiştir: " },
  { icon: "calendar", title: "Telafi planla", hint: "Hakkın süresindeki boş saatler", prefix: "Telafi aç: " },
  { icon: "activity", title: "Gelişimi göster", hint: "Devam, notlar, çalışılan eserler", prefix: "Gelişimini göster: " },
  { icon: "wallet", title: "Aidat günü gelenler", hint: "Vadesi geçen ve yaklaşan", send: "Aidat günü gelenleri listele" },
];
const TEACHER_TASKS: Task[] = ADMIN_TASKS.filter((task) => task.icon !== "wallet");
const QUESTIONS = ["Yoklamayı nereden alırım?", "Fiyat politikası nerede?", "Yeni öğrenci nasıl kaydedilir?"];

export function AssistantPanel({ open, onClose, request }: { open: boolean; onClose: () => void; request: AssistantRequest | null }) {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const [entries, setEntries] = useState<Entry[]>([]);
  const [input, setInput] = useState("");
  const [caret, setCaret] = useState(0);
  // Sunucunun döndürdüğü maskeli geçmiş; bir sonraki mesajla aynen geri gider.
  const [history, setHistory] = useState<ChatHistoryItem[]>([]);
  // Listeden seçilen öğrenciler: gönderilen metinde tek bir seçilmiş öğrenci varsa kimliği bağlam olur.
  const [mentions, setMentions] = useState<MentionCandidate[]>([]);
  const [activeIndex, setActiveIndex] = useState(0);
  const [navigated, setNavigated] = useState(false);
  const [dismissedAt, setDismissedAt] = useState<number | null>(null);
  const chat = useAssistantChat();
  const candidates = useMentionCandidates(open);
  const nextId = useRef(1);
  const handledRequest = useRef(0);
  const scrollRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const pendingCaret = useRef<number | null>(null);
  const listId = useId();

  const mention = open ? resolveMention(candidates, input, caret) : null;
  const showMentions = mention !== null && mention.query.start !== dismissedAt;
  const active = Math.min(activeIndex, Math.max(0, (mention?.matches.length ?? 1) - 1));

  function push(entry: NewEntry) {
    const id = nextId.current++;
    setEntries((current) => [...current, { ...entry, id } as Entry]);
  }

  function run(text: string, studentId?: string) {
    chat.mutate({ message: text, history, studentId }, {
      onSuccess: (response) => {
        setHistory(response.history);
        push({ role: "assistant", text: response.reply, cards: response.cards });
      },
      onError: (error) => push({ role: "assistant", text: errorMessage(error, "Asistan şu an yanıt veremiyor; biraz sonra tekrar dene."), cards: [], retry: { text, studentId } }),
      onSettled: () => inputRef.current?.focus(),
    });
  }

  function send(text: string, studentId?: string) {
    const trimmed = text.trim();
    if (!trimmed || chat.isPending) return;
    const picked = [...new Map(mentions.filter((item) => trimmed.includes(item.fullName)).map((item) => [item.id, item])).values()];
    push({ role: "user", text: trimmed });
    setInput("");
    setCaret(0);
    setMentions([]);
    run(trimmed, studentId ?? (picked.length === 1 ? picked[0]?.id : undefined));
  }

  function retry(entryId: number, again: { text: string; studentId?: string }) {
    if (chat.isPending) return;
    setEntries((current) => current.filter((entry) => entry.id !== entryId));
    run(again.text, again.studentId);
  }

  // Kısayoldan gelen istek (öğrenci satırı, kart düğmesi) bir kez gönderilir.
  useEffect(() => {
    if (!request || request.id === handledRequest.current) return;
    handledRequest.current = request.id;
    send(request.text, request.studentId);
  });

  useEffect(() => {
    const element = scrollRef.current;
    if (element) element.scrollTo({ top: element.scrollHeight, behavior: "smooth" });
  }, [entries.length, chat.isPending]);

  // Kutu yazdıkça büyür (en çok ~6 satır).
  useLayoutEffect(() => {
    const element = inputRef.current;
    if (!element) return;
    if (pendingCaret.current !== null) {
      element.focus();
      element.setSelectionRange(pendingCaret.current, pendingCaret.current);
      pendingCaret.current = null;
    }
    element.style.height = "auto";
    element.style.height = `${Math.min(element.scrollHeight, 160)}px`;
  }, [input, open]);

  function edit(value: string, position: number) {
    setInput(value);
    setCaret(position);
    setActiveIndex(0);
    setNavigated(false);
    setDismissedAt(null);
  }

  // Programla yazılan metinde (ad seçimi, görev) imleç metin değiştiği anda yerine konur;
  // bir sonraki kareyi beklemek hızlı yazışta imleci geri sıçratıyordu.
  function place(value: string, position: number) {
    pendingCaret.current = position;
    edit(value, position);
  }

  function pick(candidate: MentionCandidate) {
    if (!mention) return;
    const before = input.slice(0, mention.query.start);
    const after = input.slice(mention.query.end).replace(/^ /, "");
    const value = `${before}${candidate.fullName} ${after}`;
    setMentions((current) => [...current, candidate]);
    place(value, before.length + candidate.fullName.length + 1);
  }

  function startTask(task: Task) {
    if (task.send) send(task.send);
    else if (task.prefix) place(`${task.prefix}@`, task.prefix.length + 1);
  }

  function onKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (event.nativeEvent.isComposing) return;
    const items = mention?.matches ?? [];
    if (showMentions && mention) {
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        if (!items.length) return;
        event.preventDefault();
        setNavigated(true);
        setActiveIndex((active + (event.key === "ArrowDown" ? 1 : items.length - 1)) % items.length);
        return;
      }
      // Kendiliğinden açılan listede Enter mesajı gönderir; seçmek için Tab ya da ok tuşu.
      const selects = event.key === "Tab" || (event.key === "Enter" && !event.shiftKey && (mention.query.explicit || navigated));
      if (selects && items[active]) {
        event.preventDefault();
        pick(items[active]);
        return;
      }
      if (event.key === "Escape") {
        event.preventDefault();
        event.nativeEvent.stopPropagation();
        setDismissedAt(mention.query.start);
        return;
      }
    }
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      send(input);
    }
  }

  function reset() {
    setEntries([]);
    setHistory([]);
    setMentions([]);
    place("", 0);
  }

  const firstName = me ? meDisplayName(me).split(/\s+/)[0] : undefined;
  const empty = entries.length === 0 && !chat.isPending;

  return (
    <AssistantSheet open={open} onClose={onClose}>
      <header className="flex items-center gap-3 border-b border-[var(--line)] px-4 py-3">
        <AssistantMark size="sm" />
        <div className="min-w-0 flex-1">
          <h2 className="flex items-center gap-2 text-[.95rem] font-extrabold">Abdera Asistanı <span className="rounded-full bg-[var(--brand-soft)] px-2 py-0.5 text-[.65rem] font-bold uppercase tracking-wide text-[var(--brand-strong)]">Beta</span></h2>
          <p className="text-meta truncate text-xs">Okulunun verisiyle çalışır · değişiklikler senin onayınla</p>
        </div>
        {!empty && (
          <button type="button" onClick={reset} disabled={chat.isPending} aria-label="Yeni sohbet" title="Yeni sohbet" className="icon-btn icon-btn-quiet shrink-0">
            <Icon name="plus" className="h-4 w-4" />
          </button>
        )}
        <button type="button" onClick={onClose} aria-label="Kapat" title="Kapat (Esc)" className="icon-btn icon-btn-quiet shrink-0">
          <Icon name="close" className="h-4 w-4" />
        </button>
      </header>

      <div ref={scrollRef} aria-live="polite" className="min-h-0 flex-1 overflow-y-auto px-4 py-5">
        {empty ? (
          <div className="assistant-rise mx-auto flex max-w-md flex-col gap-5">
            <div className="flex flex-col items-center gap-3 pt-2 text-center">
              <AssistantMark size="lg" />
              <div>
                <p className="text-xl font-extrabold tracking-tight">Merhaba{firstName ? ` ${firstName}` : ""}, ne yapalım?</p>
                <p className="text-meta mt-1 text-sm">Öğrencinin adını yazmaya başla, ben tamamlarım.</p>
              </div>
            </div>
            {me && <WelcomeSummary isAdmin={isAdmin} />}
            <div className="grid grid-cols-2 gap-2">
              {(isAdmin ? ADMIN_TASKS : TEACHER_TASKS).map((task) => (
                <button key={task.title} type="button" onClick={() => startTask(task)} className="pressable group flex flex-col items-start gap-2 rounded-2xl border border-[var(--line)] bg-white p-3 text-left hover:border-[var(--brand)] hover:shadow-[0_8px_24px_rgba(84,43,28,.08)]">
                  <span className="grid h-8 w-8 place-items-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name={task.icon} className="h-4 w-4" /></span>
                  <span>
                    <span className="block text-sm font-bold leading-snug">{task.title}</span>
                    <span className="text-meta block text-xs">{task.hint}</span>
                  </span>
                </button>
              ))}
            </div>
            <div className="space-y-1.5">
              <p className="text-meta px-1 text-xs font-semibold">Ya da sor</p>
              {QUESTIONS.map((question) => (
                <button key={question} type="button" onClick={() => send(question)} className="pressable flex w-full items-center justify-between gap-2 rounded-xl px-3 py-2 text-left text-sm hover:bg-[var(--surface-muted)]">
                  {question}<Icon name="arrow-right" className="h-3.5 w-3.5 shrink-0 text-[var(--muted)]" />
                </button>
              ))}
            </div>
          </div>
        ) : (
          <div className="mx-auto flex max-w-2xl flex-col gap-4">
            {entries.map((entry) => entry.role === "user" ? (
              <div key={entry.id} className="assistant-rise flex justify-end">
                <p className="max-w-[85%] whitespace-pre-wrap rounded-2xl rounded-br-md bg-[var(--brand-strong)] px-3.5 py-2 text-sm leading-relaxed text-white">{entry.text}</p>
              </div>
            ) : (
              <AssistantTurn key={entry.id}>
                {entry.retry ? (
                  <div className="space-y-2 rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-sm text-[var(--danger-strong)]" role="alert">
                    <p>{entry.text}</p>
                    <button type="button" onClick={() => entry.retry && retry(entry.id, entry.retry)} disabled={chat.isPending} className="btn btn-quiet text-xs">Tekrar dene</button>
                  </div>
                ) : (
                  <>
                    {entry.text && <div className="space-y-2 text-sm leading-relaxed"><RichText text={entry.text} /></div>}
                    {entry.cards.map((card, index) => <Fragment key={index}>{renderCard(card)}</Fragment>)}
                  </>
                )}
              </AssistantTurn>
            ))}
            {chat.isPending && <AssistantTurn><Thinking /></AssistantTurn>}
          </div>
        )}
      </div>

      <footer className="border-t border-[var(--line)] bg-[var(--surface)] px-3 pb-[max(.75rem,env(safe-area-inset-bottom))] pt-3 sm:px-4">
        <form onSubmit={(event) => { event.preventDefault(); send(input); }} className="relative">
          {showMentions && mention && (
            <MentionList id={listId} items={mention.matches} activeIndex={active} explicit={mention.query.explicit} onPick={pick} onHover={setActiveIndex} />
          )}
          <div className="flex items-end gap-1.5 rounded-2xl border border-[var(--line)] bg-white p-1.5 pl-3 focus-within:border-[var(--brand)] focus-within:shadow-[0_0_0_3px_rgba(217,102,42,.14)]">
            <label className="min-w-0 flex-1">
              <span className="sr-only">Asistana yaz</span>
              <textarea
                ref={inputRef}
                rows={1}
                value={input}
                onChange={(event) => edit(event.target.value, event.target.selectionStart)}
                onSelect={(event) => setCaret(event.currentTarget.selectionStart)}
                onKeyDown={onKeyDown}
                maxLength={1000}
                placeholder="Sor ya da iste… (öğrenci için @)"
                autoFocus
                role="combobox"
                aria-expanded={showMentions}
                aria-controls={showMentions ? listId : undefined}
                aria-activedescendant={showMentions && mention?.matches.length ? `${listId}-${active}` : undefined}
                aria-autocomplete="list"
                className="assistant-input block max-h-40 w-full resize-none bg-transparent py-1.5 text-base leading-6 outline-none placeholder:text-[var(--muted)] sm:text-sm"
              />
            </label>
            <button type="button" onClick={() => { const position = inputRef.current?.selectionStart ?? input.length; const gap = position > 0 && !/\s$/.test(input.slice(0, position)) ? " " : ""; place(`${input.slice(0, position)}${gap}@${input.slice(position)}`, position + gap.length + 1); }} aria-label="Öğrenci ekle" title="Öğrenci ekle (@)" className="grid h-9 w-9 shrink-0 place-items-center rounded-xl text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--brand-strong)]">
              <Icon name="students" className="h-4 w-4" />
            </button>
            <button type="submit" disabled={!input.trim() || chat.isPending} aria-label="Gönder" className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--brand-strong)] text-white transition disabled:bg-[var(--line)] disabled:text-[var(--muted)]">
              <Icon name="arrow-right" className="h-4 w-4" />
            </button>
          </div>
          <p className="text-meta mt-1.5 px-1 text-center text-[.7rem]">Asistan hata yapabilir. Ders ve telafi değişiklikleri yalnızca onay kutusundan yapılır.</p>
        </form>
      </footer>
    </AssistantSheet>
  );
}

// Masaüstünde sağda duran panel, telefonda tam ekran. Modal ile aynı erişilebilirlik kuralları:
// Esc kapatır, açıkken sayfa kaymaz, kapanınca odak düğmeye döner.
function AssistantSheet({ open, onClose, children }: { open: boolean; onClose: () => void; children: ReactNode }) {
  const panelRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  });

  useEffect(() => {
    if (!open) return;
    const previouslyFocused = document.activeElement as HTMLElement | null;
    if (!panelRef.current?.contains(document.activeElement)) panelRef.current?.focus();
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key === "Escape") onCloseRef.current();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.body.style.overflow = overflow;
      previouslyFocused?.focus();
    };
  }, [open]);

  if (!open || typeof document === "undefined") return null;

  return createPortal(
    <div className="fixed inset-0 z-50 flex justify-end">
      <button type="button" onClick={onClose} aria-label="Asistanı kapat" className="absolute inset-0 bg-[#2a1c14]/25 backdrop-blur-[1px]" />
      <div
        ref={panelRef}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label="Abdera Asistanı"
        className="assistant-sheet-in relative z-10 flex h-dvh w-full flex-col bg-[var(--surface)] focus:outline-none sm:m-3 sm:h-[calc(100dvh-1.5rem)] sm:w-[30rem] sm:overflow-hidden sm:rounded-3xl sm:border sm:border-[var(--line)] sm:shadow-[0_24px_64px_rgba(84,43,28,.22)]"
      >
        {children}
      </div>
    </div>,
    document.body,
  );
}

function AssistantMark({ size }: { size: "sm" | "lg" }) {
  const box = size === "lg" ? "h-14 w-14 rounded-2xl" : "h-9 w-9 rounded-xl";
  return (
    <span aria-hidden className={`grid shrink-0 place-items-center bg-gradient-to-br from-[var(--brand)] to-[var(--brand-strong)] text-white shadow-[0_8px_20px_rgba(168,78,31,.28)] ${box}`}>
      <Icon name="sparkles" className={size === "lg" ? "h-7 w-7" : "h-[1.1rem] w-[1.1rem]"} />
    </span>
  );
}

// Asistanın bir cevabı: solda küçük işaret, metin balonsuz, kartlar tam genişlikte.
function AssistantTurn({ children }: { children: ReactNode }) {
  return (
    <div className="assistant-rise flex gap-2.5">
      <span aria-hidden className="mt-0.5 grid h-7 w-7 shrink-0 place-items-center rounded-lg bg-[var(--brand-soft)] text-[var(--brand-strong)]">
        <Icon name="sparkles" className="h-3.5 w-3.5" />
      </span>
      <div className="min-w-0 flex-1 space-y-3">{children}</div>
    </div>
  );
}

function renderCard(card: AssistantCardData) {
  const student = card.student;
  switch (card.kind) {
    case "progress": return student && <ProgressCard student={student} />;
    case "dues": return <DuesCard student={student} />;
    case "lessons": return student && <LessonsCard student={student} />;
    case "reschedule": return student && <RescheduleFlow student={student} move={card.move} />;
    case "makeup": return student && <MakeupFlow student={student} move={card.move} />;
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
    if (list.length) blocks.push(<ul key={`list-${blocks.length}`} className="list-disc space-y-0.5 pl-5 marker:text-[var(--muted)]">{list.map((item, index) => <li key={index}>{inline(item)}</li>)}</ul>);
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
    <span className="inline-flex h-7 items-center gap-1 text-[var(--muted)]" role="status" aria-label="Asistan düşünüyor">
      <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current [animation-delay:-240ms]" />
      <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current [animation-delay:-120ms]" />
      <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current" />
    </span>
  );
}
