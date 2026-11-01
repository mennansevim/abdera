"use client";

import { Fragment, useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { usePathname } from "next/navigation";
import { createPortal } from "react-dom";
import { Icon, type IconName } from "@/components/icons";
import { errorMessage } from "@/lib/library";
import { meDisplayName, useMe } from "@/lib/use-auth";
import { useAssistantChat, type AssistantCardData, type ChatHistoryItem } from "@/lib/assistant";
import { AssistantIntro, introSeen, markIntroSeen, type IntroExample } from "./assistant-intro";
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

const MOVE: Task = { icon: "clock", title: "Ders saatini değiştir", hint: "Tek ders ya da her hafta", prefix: "Ders saatini değiştir: " };
const MAKEUP: Task = { icon: "calendar", title: "Telafi planla", hint: "Hakkın süresindeki boş saatler", prefix: "Telafi aç: " };
const PROGRESS: Task = { icon: "activity", title: "Gelişimi göster", hint: "Devam, notlar, çalışılan eserler", prefix: "Gelişimini göster: " };
const LESSONS: Task = { icon: "students", title: "Derslerini göster", hint: "Yaklaşan ve geçmiş dersler", prefix: "Derslerini göster: " };
const DUES_LIST: Task = { icon: "wallet", title: "Aidat günü gelenler", hint: "Vadesi geçen ve yaklaşan", send: "Aidat günü gelenleri listele" };
const STUDENT_DUES: Task = { icon: "wallet", title: "Aidatını göster", hint: "Öğrencinin ödenmemiş aidatları", prefix: "Aidatını göster: " };
const ALL_TASKS = [MOVE, MAKEUP, PROGRESS, LESSONS, DUES_LIST, STUDENT_DUES];
const QUESTIONS = ["Yoklamayı nereden alırım?", "Fiyat politikası nerede?", "Yeni öğrenci nasıl kaydedilir?"];

// Açılışta bulunulan ekrana göre üç öneri. Yalnızca asistanın araçlarıyla cevaplanabilen işler
// önerilir: toplu analiz aracı yok, "bu ayın tahsilatı" gibi bir soru burada yer alamaz.
function pageTasks(pathname: string, isAdmin: boolean): Task[] {
  if (isAdmin && pathname.startsWith("/dashboard/billing")) return [STUDENT_DUES, PROGRESS, MAKEUP];
  if (pathname.startsWith("/dashboard/calendar")) return [MOVE, MAKEUP, LESSONS];
  if (pathname.startsWith("/dashboard/students")) return [PROGRESS, LESSONS, MAKEUP];
  return [MOVE, MAKEUP, PROGRESS];
}

// Tanıtımın son adımındaki "Hemen dene" örnekleri. Asistanın araçları öğrenci başına çalışır;
// okulun gerçek bir öğrencisiyle kurulan örnek her zaman çalışan bir cevap gösterir. Ad ek almaz
// ("Ayşe Kaya'nın" değil) ki ünlü uyumu hatası olmasın; kimlik de bağlam olarak gider.
function introExamples(isAdmin: boolean, student: MentionCandidate | undefined): IntroExample[] {
  const general: IntroExample = isAdmin ? { text: "Aidat günü gelenleri listele" } : { text: "Yoklamayı nereden alırım?" };
  if (!student) return [general, { text: "Yeni öğrenci nasıl kaydedilir?" }];
  return [
    { text: `${student.fullName} nasıl gidiyor?`, studentId: student.id },
    { text: `${student.fullName} ne zaman derse geliyor?`, studentId: student.id },
    general,
  ];
}

export function AssistantPanel({ open, modal, onClose, request }: { open: boolean; modal: boolean; onClose: () => void; request: AssistantRequest | null }) {
  const { data: me } = useMe();
  const pathname = usePathname();
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
  // İlk açılışta kısa tanıtım; görüldüyse doğrudan sohbet.
  const [showIntro, setShowIntro] = useState(() => typeof window !== "undefined" && !introSeen());
  const chat = useAssistantChat();
  const candidates = useMentionCandidates(open);
  const nextId = useRef(1);
  const handledRequest = useRef(0);
  const scrollRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const pendingCaret = useRef<number | null>(null);
  // Kullanıcı eski bir mesajı okurken yeni cevap onu alta atmasın.
  const followLatest = useRef(true);
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
      onSettled: () => {
        // Panel sayfanın yanındayken kullanıcı sayfada bir alana yazıyor olabilir; odağı çalma.
        const focused = document.activeElement;
        if (focused === document.body || inputRef.current?.closest("[role=dialog]")?.contains(focused)) inputRef.current?.focus();
      },
    });
  }

  function send(text: string, studentId?: string) {
    const trimmed = text.trim();
    if (!trimmed || chat.isPending) return;
    const picked = [...new Map(mentions.filter((item) => trimmed.includes(item.fullName)).map((item) => [item.id, item])).values()];
    push({ role: "user", text: trimmed });
    followLatest.current = true;
    // Sayfadan gelen kısayol kutudaki yarım mesajı silmesin.
    if (text === input) {
      setInput("");
      setCaret(0);
      setMentions([]);
    }
    run(trimmed, studentId ?? (picked.length === 1 ? picked[0]?.id : undefined));
  }

  function retry(entryId: number, again: { text: string; studentId?: string }) {
    if (chat.isPending) return;
    setEntries((current) => current.filter((entry) => entry.id !== entryId));
    run(again.text, again.studentId);
  }

  // Kısayoldan gelen istek (öğrenci satırı, kart düğmesi) bir kez gönderilir. Asistan cevap
  // beklerken sayfa açık kaldığı için istek gelebilir; cevap bitince sıra ona gelir.
  useEffect(() => {
    if (!request || request.id === handledRequest.current || chat.isPending) return;
    handledRequest.current = request.id;
    send(request.text, request.studentId);
  });

  useEffect(() => {
    const element = scrollRef.current;
    if (element && followLatest.current) element.scrollTo({ top: element.scrollHeight, behavior: "smooth" });
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

  function closeIntro() {
    markIntroSeen();
    setShowIntro(false);
    inputRef.current?.focus();
  }

  const firstName = me ? meDisplayName(me).split(/\s+/)[0] : undefined;
  const empty = entries.length === 0 && !chat.isPending;
  const suggested = pageTasks(pathname, isAdmin);
  const otherTasks = ALL_TASKS.filter((task) => !suggested.includes(task) && (isAdmin || task.icon !== "wallet"));

  return (
    <AssistantSheet open={open} modal={modal} onClose={onClose}>
      <header className="flex items-center gap-3 border-b border-[var(--line)] px-4 py-3">
        <AssistantMark />
        <div className="min-w-0 flex-1">
          <h2 className="flex items-center gap-2 text-[.95rem] font-extrabold">Abdera Asistanı <span className="rounded-full bg-[var(--brand-soft)] px-2 py-0.5 text-[.65rem] font-bold uppercase tracking-wide text-[var(--brand-strong)]">Beta</span></h2>
          <p className="text-meta truncate text-xs">Okulunun verisiyle çalışır · değişiklikler senin onayınla</p>
        </div>
        {!showIntro && (
          <button type="button" onClick={() => setShowIntro(true)} aria-label="Nasıl kullanılır" title="Nasıl kullanılır" className="icon-btn icon-btn-quiet shrink-0 text-sm font-extrabold">?</button>
        )}
        {!empty && !showIntro && (
          <button type="button" onClick={reset} disabled={chat.isPending} aria-label="Yeni sohbet" title="Yeni sohbet" className="icon-btn icon-btn-quiet shrink-0">
            <Icon name="plus" className="h-4 w-4" />
          </button>
        )}
        <button type="button" onClick={onClose} aria-label="Kapat" title="Kapat (Esc)" className="icon-btn icon-btn-quiet shrink-0">
          <Icon name="close" className="h-4 w-4" />
        </button>
      </header>

      <div
        ref={scrollRef}
        onScroll={(event) => {
          const element = event.currentTarget;
          followLatest.current = element.scrollHeight - element.scrollTop - element.clientHeight < 120;
        }}
        className="min-h-0 flex-1 overflow-y-auto px-4 py-5"
      >
        {showIntro ? (
          <AssistantIntro
            examples={introExamples(isAdmin, candidates.find((candidate) => !candidate.inactive))}
            onDone={closeIntro}
            onExample={(example) => { closeIntro(); send(example.text, example.studentId); }}
          />
        ) : empty ? (
          <div className="assistant-rise mx-auto flex max-w-md flex-col gap-4">
            <p className="text-lg font-extrabold tracking-tight">Merhaba{firstName ? ` ${firstName}` : ""}, ne yapalım?</p>
            {me && <WelcomeSummary isAdmin={isAdmin} />}
            <div>
              {suggested.map((task) => <TaskRow key={task.title} task={task} onPick={startTask} />)}
            </div>
            <details className="group">
              <summary className="text-meta flex min-h-11 cursor-pointer list-none items-center gap-1.5 px-2 text-xs font-semibold [&::-webkit-details-marker]:hidden">
                Tüm yapabileceklerim<Icon name="chevron" className="h-3.5 w-3.5 transition-transform group-open:rotate-90" />
              </summary>
              {otherTasks.map((task) => <TaskRow key={task.title} task={task} onPick={startTask} />)}
              {QUESTIONS.map((question) => (
                <button key={question} type="button" onClick={() => send(question)} className="pressable flex min-h-11 w-full items-center justify-between gap-2 rounded-xl px-2 py-2 text-left text-sm hover:bg-[var(--surface-muted)]">
                  {question}<Icon name="arrow-right" className="h-3.5 w-3.5 shrink-0 text-[var(--muted)]" />
                </button>
              ))}
            </details>
          </div>
        ) : null}
        {/* Konuşma günlüğü baştan yerinde durur: ekran okuyucu yalnız yeni eklenen mesajı okur. */}
        <div role="log" aria-label="Konuşma" aria-relevant="additions" aria-busy={chat.isPending} className={`mx-auto max-w-2xl flex-col gap-4 ${showIntro ? "hidden" : "flex"}`}>
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
        <p role="status" className="sr-only">{chat.isPending ? "Asistan cevap hazırlıyor" : ""}</p>
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
                role="combobox"
                aria-expanded={showMentions}
                aria-controls={showMentions ? listId : undefined}
                aria-activedescendant={showMentions && mention?.matches.length ? `${listId}-${active}` : undefined}
                aria-autocomplete="list"
                className="assistant-input block max-h-40 w-full resize-none bg-transparent py-1.5 text-base leading-6 outline-none placeholder:text-[var(--muted)] sm:text-sm"
              />
            </label>
            <button type="button" onClick={() => { const position = inputRef.current?.selectionStart ?? input.length; const gap = position > 0 && !/\s$/.test(input.slice(0, position)) ? " " : ""; place(`${input.slice(0, position)}${gap}@${input.slice(position)}`, position + gap.length + 1); }} aria-label="Öğrenci ekle" title="Öğrenci ekle (@)" className="grid h-11 w-11 shrink-0 place-items-center rounded-xl text-[var(--muted)] sm:h-9 sm:w-9 hover:bg-[var(--surface-muted)] hover:text-[var(--brand-strong)]">
              <Icon name="students" className="h-4 w-4" />
            </button>
            <button type="submit" disabled={!input.trim() || chat.isPending} aria-label="Gönder" className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-[var(--brand-strong)] text-white transition sm:h-9 sm:w-9 disabled:bg-[var(--line)] disabled:text-[var(--muted)]">
              <Icon name="arrow-right" className="h-4 w-4" />
            </button>
          </div>
          <p className="text-meta mt-1.5 px-1 text-center text-[.75rem]">Asistan hata yapabilir. Ders ve telafi değişiklikleri yalnızca onay kutusundan yapılır.</p>
        </form>
      </footer>
    </AssistantSheet>
  );
}

// Masaüstünde sayfanın yanında duran panel: arka plan kullanılmaya devam eder, Esc yalnızca odak
// paneldeyken kapatır (sayfadaki pencerenin Esc'i asistanı kapatmasın) ve sayfanın pencereleri
// panelin üstünde açılır. Telefon ve tablette pencere: açıkken sayfa kaymaz, arka plan klavyeye
// kapalıdır (AssistantProvider inert). Kapanınca odak, başka bir yere geçmediyse açan düğmeye döner.
function AssistantSheet({ open, modal, onClose, children }: { open: boolean; modal: boolean; onClose: () => void; children: ReactNode }) {
  const panelRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  });

  useEffect(() => {
    if (!open) return;
    const previouslyFocused = document.activeElement as HTMLElement | null;
    // autoFocus kullanılmaz: açan düğmeyi okumadan odak mesaj kutusuna geçmiş olurdu.
    (panelRef.current?.querySelector("textarea") ?? panelRef.current)?.focus();
    return () => {
      if (document.activeElement === document.body) previouslyFocused?.focus();
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    function onKeyDown(event: globalThis.KeyboardEvent) {
      if (event.key === "Escape" && (modal || panelRef.current?.contains(document.activeElement))) onCloseRef.current();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, [open, modal]);

  useEffect(() => {
    if (!open || !modal) return;
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = overflow;
    };
  }, [open, modal]);

  if (!open || typeof document === "undefined") return null;

  return createPortal(
    <div className={`fixed inset-0 flex justify-end ${modal ? "z-50" : "pointer-events-none z-40"}`}>
      {modal && <button type="button" onClick={onClose} tabIndex={-1} aria-hidden="true" className="absolute inset-0 bg-[#2a1c14]/25 backdrop-blur-[1px]" />}
      <div
        ref={panelRef}
        tabIndex={-1}
        role="dialog"
        aria-modal={modal || undefined}
        aria-label="Abdera Asistanı"
        className="assistant-sheet-in pointer-events-auto relative z-10 flex h-dvh w-full flex-col bg-[var(--surface)] focus:outline-none sm:m-3 sm:h-[calc(100dvh-1.5rem)] sm:w-[30rem] sm:overflow-hidden sm:rounded-3xl sm:border sm:border-[var(--line)] sm:shadow-[0_24px_64px_rgba(84,43,28,.22)]"
      >
        {children}
      </div>
    </div>,
    document.body,
  );
}

function AssistantMark() {
  return (
    <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-gradient-to-br from-[var(--brand)] to-[var(--brand-strong)] text-white shadow-[0_8px_20px_rgba(168,78,31,.28)]">
      <Icon name="sparkles" className="h-[1.1rem] w-[1.1rem]" />
    </span>
  );
}

// Açılıştaki iş satırı: kart değil, çerçevesiz bir liste öğesi. Güçlü çerçeve onay kutusunundur.
function TaskRow({ task, onPick }: { task: Task; onPick: (task: Task) => void }) {
  return (
    <button type="button" onClick={() => onPick(task)} className="pressable flex min-h-11 w-full items-center gap-3 rounded-xl px-2 py-1.5 text-left hover:bg-[var(--surface-muted)]">
      <Icon name={task.icon} className="h-4 w-4 shrink-0 text-[var(--brand-strong)]" />
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-bold leading-snug">{task.title}</span>
        <span className="text-meta block truncate text-xs">{task.hint}</span>
      </span>
      <Icon name="arrow-right" className="h-3.5 w-3.5 shrink-0 text-[var(--muted)]" />
    </button>
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
    <span aria-hidden className="inline-flex h-7 items-center gap-1 text-[var(--muted)]">
      <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current [animation-delay:-240ms]" />
      <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current [animation-delay:-120ms]" />
      <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current" />
    </span>
  );
}
