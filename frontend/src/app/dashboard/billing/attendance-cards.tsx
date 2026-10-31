"use client";

import Link from "next/link";
import { useMemo, useState } from "react";
import { EmptyState, Modal } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { ATTENDANCE_LABELS, useAddCardEntry, useAttendanceCards, useChangeCardEntry, useMarkAttendance, useRemoveCardEntry, type AttendanceCard, type AttendanceStatus, type CardCycle, type CardLesson } from "@/lib/attendance";
import { PAYMENT_REMINDER_OUTCOME_TEXT, useCorrectPaymentDates, useSendPaymentReminder } from "@/lib/billing";
import { cycleKey, formatMoney, formatPeriod, isCyclePeriod, todayInput } from "@/lib/billing-format";
import { QuickCollect } from "./collect-sheet";

// Aidatlar ekranı: birebir öğrencinin delikli kartı (docs/10-decisions.md AD, AD18). 4 Geldi bir
// kartı doldurur, kartın sonunda para yuvası o dönemin aidatıdır. Kartı ve durumu sunucu kurar
// (AttendanceCards.cs); bu ekran yalnızca çizer. Tek durum dili, dört kelime: Ödenmedi ·
// Yaklaşıyor · Tamam · Başlamadı. "Ödeme al" kartta yalnızca borçluda durur; karta dokununca
// öğrencinin tüm kartları aynı biçimde, tarihleriyle açılır ve tarihler oradan düzeltilir.
// Tahsilat sade pencereyle (QuickCollect: tarih + tutar) aynı tek tahsilat yolundan alınır.

type Status = "owed" | "soon" | "ok" | "new";

const STATUS_LABEL: Record<Status, string> = { owed: "Ödenmedi", soon: "Yaklaşıyor", ok: "Tamam", new: "Başlamadı" };
const STATUS_ORDER: Record<Status, number> = { owed: 0, soon: 1, ok: 2, new: 3 };
const PILL_CLASS: Record<Status, string> = {
  owed: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  soon: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  ok: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  new: "bg-[var(--surface-muted)] text-[var(--muted)]",
};

function statusOf(card: AttendanceCard): Status {
  switch (card.state) {
    case "Unpaid": return "owed";
    case "LastLesson":
    case "AwaitingPayment": return "soon";
    case "InProgress":
    case "NextPrepaid": return "ok";
    case "NotStarted": return "new";
  }
}

const shortDate = new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "2-digit" });
const dayOf = (iso: string) => new Date(`${iso}T00:00:00`);
const short = (iso: string) => shortDate.format(dayOf(iso));
const daysSince = (iso: string) => Math.floor((Date.now() - dayOf(iso).getTime()) / 86_400_000);

const unpaidCycles = (card: AttendanceCard) =>
  card.cycles.filter((cycle) => cycle.receivable?.accruedOn && !["Paid", "Cancelled"].includes(cycle.receivable.status));
const currentFilled = (card: AttendanceCard) => card.cycles.find((cycle) => cycle.number === card.currentCycle)?.filled.length ?? 0;

// Kartın altındaki tek satır: satırın neden o durumda olduğu.
function noteOf(card: AttendanceCard, status: Status) {
  if (status === "owed") {
    const receivable = unpaidCycles(card).at(0)?.receivable;
    if (!receivable) return null;
    // Gecikme vadeden hesaplanır: kayıttaki durum her zaman Overdue'ya çevrilmiş olmayabilir.
    const late = daysSince(receivable.dueDate);
    return late > 0 ? `${late} gün gecikti` : late === 0 ? "vadesi bugün" : `vade ${short(receivable.dueDate)}`;
  }
  if (card.state === "AwaitingPayment") return "yeni kart başlıyor";
  if (card.state === "LastLesson") return "1 ders kaldı";
  if (card.state === "NextPrepaid") return "sonraki kart peşin";
  if (card.state === "InProgress") return `${4 - card.filledInCurrent} ders hakkı`;
  return "ilk ders yazılmadı";
}

function StatusPill({ status, children }: { status: Status; children?: React.ReactNode }) {
  return <span className={`inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-bold ${PILL_CLASS[status]}`}>
    <span aria-hidden className="size-1.5 rounded-full bg-current" />{children ?? STATUS_LABEL[status]}
  </span>;
}

function Hole({ filled, label, size = "md" }: { filled: boolean; label: string; size?: "md" | "lg" }) {
  const box = size === "lg" ? "size-10 text-base" : "size-8 text-sm";
  return <span aria-hidden className={`grid shrink-0 place-items-center rounded-full font-bold ${box} ${filled ? "bg-[var(--success)] text-white" : "border-2 border-dashed border-[var(--line)] text-[.7rem] text-[var(--muted)]"}`}>{filled ? "✓" : label}</span>;
}

function Coin({ tone, size = "md" }: { tone: "paid" | "owed" | "none"; size?: "md" | "lg" }) {
  const box = size === "lg" ? "size-11" : "size-9";
  const look = tone === "paid" ? "border-2 border-[var(--success)] bg-[var(--success-soft)] text-[var(--success-strong)]"
    : tone === "owed" ? "bg-[var(--danger)] text-white"
    : "border-2 border-dashed border-[var(--muted)] text-[var(--muted)]";
  return <span aria-hidden className={`grid shrink-0 place-items-center rounded-full font-extrabold ${box} ${look}`}>₺</span>;
}

// Ödeme penceresinde dönemin adı: "2. kart · 05.10 – …" ve karşılayan aylık aidat (AD14).
function cycleRange(cycle: CardCycle) {
  if (!cycle.filled.length) return null;
  const first = short(cycle.filled[0].date);
  return cycle.filled.length === 4 ? `${first} – ${short(cycle.filled[3].date)}` : `${first} – …`;
}
function cycleTitle(cycle: CardCycle) {
  const period = cycle.receivable?.period;
  return [`${cycle.number}. kart`, cycleRange(cycle), period && !isCyclePeriod(period) ? `${formatPeriod(period)} aidatı` : null].filter(Boolean).join(" · ");
}

// Tahsil edilecek dönemler: borç varsa ödenmemiş başlamış dönemler, yoksa sıradaki dönem peşin.
// Tahsilat dönemin gerçek aidatıyla yapılır: aylık aidatın karşıladığı dönemde o ay (AD14), yoksa "D00n".
function collectTarget(card: AttendanceCard) {
  const unpaid = unpaidCycles(card);
  const nextCycle = card.cycles.find((cycle) => cycle.number === card.currentCycle + 1);
  const nextPeriod = nextCycle?.receivable && nextCycle.receivable.status !== "Cancelled" ? nextCycle.receivable.period : cycleKey(card.currentCycle + 1);
  const periods = unpaid.length && card.state !== "AwaitingPayment" ? unpaid.map((cycle) => cycle.receivable!.period) : [nextPeriod];
  const labels: Record<string, string> = Object.fromEntries(card.cycles
    .filter((cycle) => cycle.receivable)
    .map((cycle) => [cycle.receivable!.period, cycleTitle(cycle)]));
  if (!labels[nextPeriod]) labels[nextPeriod] = `${card.currentCycle + 1}. kart · sıradaki 4 ders`;
  return { periods, labels };
}

type Collecting = { card: AttendanceCard; periods: string[]; labels: Record<string, string> };

// onFullCollect: tam ödeme penceresi (indirim, ödeme şekli, peşin kademe) - CollectSheet Aidatlar'da açılır.
export function AttendanceCards({ query, onFullCollect }: { query: string; onFullCollect: (enrollmentId: string) => void }) {
  const { data, isLoading, isError, refetch } = useAttendanceCards();
  const [filter, setFilter] = useState<Status | null>(null);
  const [instrument, setInstrument] = useState("");
  const [teacher, setTeacher] = useState("");
  const [openId, setOpenId] = useState<string | null>(null);
  const [collecting, setCollecting] = useState<Collecting | null>(null);
  const [toast, setToast] = useState<string | null>(null);

  const cards = useMemo(
    () => (data?.cards ?? [])
      .map((card) => ({ card, status: statusOf(card) }))
      .sort((a, b) => STATUS_ORDER[a.status] - STATUS_ORDER[b.status]
        || a.card.studentName.localeCompare(b.card.studentName, "tr-TR")
        || a.card.instrumentName.localeCompare(b.card.instrumentName, "tr-TR")),
    [data],
  );
  const instruments = useMemo(() => [...new Set(cards.map(({ card }) => card.instrumentName))].sort((a, b) => a.localeCompare(b, "tr-TR")), [cards]);
  const teachers = useMemo(() => [...new Set(cards.map(({ card }) => card.teacherName))].sort((a, b) => a.localeCompare(b, "tr-TR")), [cards]);

  const matching = useMemo(() => {
    const q = query.trim().toLocaleLowerCase("tr-TR");
    return cards.filter(({ card }) =>
      (!q || [card.studentName, card.teacherName, card.instrumentName].some((name) => name.toLocaleLowerCase("tr-TR").includes(q)))
      && (!instrument || card.instrumentName === instrument)
      && (!teacher || card.teacherName === teacher));
  }, [cards, query, instrument, teacher]);
  const visible = filter ? matching.filter((item) => item.status === filter) : matching;
  const owed = matching.filter((item) => item.status === "owed");
  const owedAmount = owed.reduce((sum, { card }) => sum + card.unpaidAmount, 0);
  const currency = owed[0]?.card.currency ?? "TRY";
  const count = (status: Status) => matching.filter((item) => item.status === status).length;

  const open = cards.find(({ card }) => card.enrollmentId === openId)?.card ?? null;

  if (isLoading) return <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{Array.from({ length: 6 }, (_, index) => <div key={index} className="skeleton h-32 rounded-2xl" />)}</div>;
  if (isError || !data) return <div className="app-card p-6 text-center"><p className="text-sm font-bold">Kartlar yüklenemedi.</p><button type="button" onClick={() => refetch()} className="btn btn-quiet mt-2">Tekrar dene</button></div>;

  const tiles: { status: Status; value: string; label: string; tone: string }[] = [
    { status: "owed", value: formatMoney(owedAmount, currency), label: `${owed.length} öğrenciden alınacak`, tone: "text-[var(--danger-strong)]" },
    { status: "soon", value: String(count("soon")), label: "ödemesi yaklaşıyor", tone: "text-[var(--warning-strong)]" },
    { status: "ok", value: String(count("ok")), label: "tamam", tone: "text-[var(--success-strong)]" },
  ];

  return <div className="space-y-3">
    <div className="grid grid-cols-3 gap-2">
      {tiles.map((tile) => <button
        key={tile.status}
        type="button"
        aria-pressed={filter === tile.status}
        onClick={() => setFilter((current) => (current === tile.status ? null : tile.status))}
        className={`pressable min-w-0 rounded-2xl border bg-[var(--surface)] px-3 py-2 text-left sm:px-4 sm:py-2.5 ${filter === tile.status ? "border-[var(--foreground)] shadow-[inset_0_0_0_1px_var(--foreground)]" : "border-[var(--line)]"}`}
      >
        <strong className={`block truncate text-base tabular-nums sm:text-xl ${tile.tone}`}>{tile.value}</strong>
        <span className="text-meta block text-xs leading-tight">{tile.label}</span>
      </button>)}
    </div>

    <div className="flex flex-wrap items-center gap-1.5">
      {filter && <button type="button" onClick={() => setFilter(null)} className="inline-flex items-center gap-1.5 rounded-full bg-[var(--foreground)] px-3 py-1 text-xs font-bold text-white">{STATUS_LABEL[filter]} ✕</button>}
      {count("new") > 0 && filter !== "new" && <button type="button" onClick={() => setFilter("new")} className="rounded-full bg-[var(--surface-muted)] px-3 py-1 text-xs font-bold text-[var(--muted)]">Başlamadı {count("new")}</button>}
      <span className="flex-1" />
      <select value={instrument} onChange={(event) => setInstrument(event.target.value)} aria-label="Branş" className="field !min-h-8 w-auto py-1 text-xs">
        <option value="">Tüm branşlar</option>
        {instruments.map((name) => <option key={name} value={name}>{name}</option>)}
      </select>
      <select value={teacher} onChange={(event) => setTeacher(event.target.value)} aria-label="Öğretmen" className="field !min-h-8 w-auto py-1 text-xs">
        <option value="">Tüm öğretmenler</option>
        {teachers.map((name) => <option key={name} value={name}>{name}</option>)}
      </select>
    </div>

    {!visible.length && <div className="app-card"><EmptyState title="Bu filtrede öğrenci yok" description="Filtreyi kaldırmayı ya da aramayı temizlemeyi dene." /></div>}
    <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
      {visible.map(({ card, status }) => <li key={card.enrollmentId}>
        <Ticket card={card} status={status} onOpen={() => setOpenId(card.enrollmentId)} onCollect={() => setCollecting({ card, ...collectTarget(card) })} />
      </li>)}
    </ul>

    {open && <HistoryModal
      key={open.enrollmentId}
      card={open}
      entriesFrom={data.entriesFrom}
      onClose={() => setOpenId(null)}
      onCollect={() => setCollecting({ card: open, ...collectTarget(open) })}
      onFullCollect={() => { setOpenId(null); onFullCollect(open.enrollmentId); }}
    />}

    {collecting && <QuickCollect
      enrollmentId={collecting.card.enrollmentId}
      studentName={collecting.card.studentName}
      periods={collecting.periods}
      periodLabels={collecting.labels}
      onClose={() => setCollecting(null)}
      onCollected={(text) => { setCollecting(null); setToast(text); }}
    />}

    {toast && <div role="status" className="fixed inset-x-3 bottom-[calc(env(safe-area-inset-bottom,0px)+5rem)] z-[60] mx-auto flex max-w-md items-center gap-3 rounded-2xl bg-[var(--foreground)] px-4 py-3 text-sm font-semibold text-white shadow-lg lg:bottom-6">
      <span className="min-w-0 flex-1">{toast}</span>
      <button type="button" onClick={() => setToast(null)} aria-label="Kapat" className="shrink-0 opacity-70">✕</button>
    </div>}
  </div>;
}

// ---- Delikli kart ---------------------------------------------------------------

function Ticket({ card, status, onOpen, onCollect }: { card: AttendanceCard; status: Status; onOpen: () => void; onCollect: () => void }) {
  const filled = currentFilled(card);
  const coin = status === "owed" ? "owed" : status === "new" ? "none" : "paid";
  return <article className={`relative grid h-full gap-2.5 rounded-2xl border bg-[var(--surface)] px-3.5 py-3 transition hover:-translate-y-px hover:shadow-[var(--shadow-card)] ${status === "owed" ? "border-[color-mix(in_srgb,var(--danger)_45%,var(--line))]" : "border-[var(--line)]"}`}>
    {/* Kartın tamamı geçmişi açar; "Ödeme al" bu katmanın üstünde ayrı düğmedir. */}
    <button type="button" onClick={onOpen} aria-label={`${card.studentName} kartlarını aç`} className="absolute inset-0 rounded-2xl" />
    <div className="flex items-baseline justify-between gap-2">
      <span className="min-w-0">
        <strong className="block truncate text-sm">{card.studentName}</strong>
        <span className="text-meta block truncate text-xs">{card.instrumentName} · {card.teacherName}</span>
      </span>
      {card.currentCycle > 0 && <span className="shrink-0 text-xs font-semibold tabular-nums text-[var(--muted)]">{card.currentCycle}. kart</span>}
    </div>
    <div className="flex items-center gap-2" aria-label={`Bu kartta ${filled}/4 ders`}>
      {[0, 1, 2, 3].map((index) => <Hole key={index} filled={index < filled} label={String(index + 1)} />)}
      <span aria-hidden className="flex-1 border-t-2 border-dotted border-[var(--line)]" />
      <Coin tone={coin} />
    </div>
    <div className="flex min-h-8 items-center justify-between gap-2">
      <StatusPill status={status} />
      {status === "owed"
        ? <button type="button" onClick={onCollect} className="btn btn-primary relative z-10">Ödeme al · {formatMoney(card.unpaidAmount, card.currency)}</button>
        : <span className="text-meta text-xs">{noteOf(card, status)}</span>}
    </div>
    {status === "owed" && <span className="text-meta -mt-1 text-xs">{noteOf(card, status)}</span>}
  </article>;
}

// ---- Geçmiş: tüm kartlar, tarihleriyle ------------------------------------------

type EditTarget =
  | { kind: "entry"; entryId: string; date: string }
  | { kind: "add" }
  | { kind: "system"; lesson: CardLesson }
  | { kind: "payment"; paymentIds: string[]; date: string };
type Editing = { cycle: number; slot: string; target: EditTarget };

function HistoryModal({ card, entriesFrom, onClose, onCollect, onFullCollect }: { card: AttendanceCard; entriesFrom: string; onClose: () => void; onCollect: () => void; onFullCollect: () => void }) {
  const reminder = useSendPaymentReminder();
  const [editing, setEditing] = useState<Editing | null>(null);
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const status = statusOf(card);
  const remindTarget = unpaidCycles(card).at(-1)?.receivable ?? null;

  // Tüm kartlar en yenisi üstte; son kart sıradaki boş deliğin düştüğü karttır.
  const filledTotal = card.cycles.reduce((sum, cycle) => sum + cycle.filled.length, 0);
  const nextBlock = Math.floor(filledTotal / 4) + 1;
  const lastShown = Math.max(nextBlock, ...card.cycles.map((cycle) => cycle.number));
  const blocks: CardCycle[] = Array.from({ length: lastShown }, (_, index) =>
    card.cycles.find((cycle) => cycle.number === index + 1) ?? { number: index + 1, filled: [], misses: [], receivable: null }).reverse();
  const payments = card.cycles.filter((cycle) => cycle.receivable && cycle.receivable.paid > 0);
  const paidTotal = payments.reduce((sum, cycle) => sum + cycle.receivable!.paid, 0);

  async function remind() {
    if (!remindTarget) return;
    setMessage(null);
    try {
      const result = await reminder.mutateAsync(remindTarget.id);
      setMessage({ tone: result.scheduled ? "success" : "error", text: PAYMENT_REMINDER_OUTCOME_TEXT[result.outcome] });
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? (err.detail ?? err.title) : "Hatırlatma gönderilemedi." });
    }
  }

  const toggle = (next: Editing) => setEditing((current) => (current?.cycle === next.cycle && current.slot === next.slot ? null : next));

  return <Modal open size="lg" title={card.studentName} description={`${card.instrumentName} · ${card.teacherName}`} onClose={onClose}>
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <span className="text-meta text-sm"><strong className="text-[var(--foreground)] tabular-nums">{filledTotal}</strong> ders</span>
        <span className="text-meta text-sm"><strong className="text-[var(--foreground)] tabular-nums">{payments.length}</strong> ödeme · <strong className="text-[var(--foreground)] tabular-nums">{formatMoney(paidTotal, card.currency)}</strong></span>
        {card.unpaidAmount > 0 && <span className="text-sm font-bold tabular-nums text-[var(--danger-strong)]">{formatMoney(card.unpaidAmount, card.currency)} açık</span>}
        <span className="flex-1" />
        {remindTarget && <button type="button" onClick={remind} disabled={reminder.isPending} className="btn btn-quiet text-xs">{reminder.isPending ? "Gönderiliyor…" : "Hatırlat"}</button>}
        <button type="button" onClick={onFullCollect} className="btn btn-quiet text-xs" title="İndirim, ödeme şekli, peşin kampanya">Ayrıntılı ödeme</button>
        <button type="button" onClick={onCollect} className={`btn ${status === "owed" ? "btn-primary" : "btn-quiet"}`}>{status === "owed" ? `Ödeme al · ${formatMoney(card.unpaidAmount, card.currency)}` : "Peşin ödeme al"}</button>
      </div>
      <p className="text-meta text-xs">Düzeltmek için derse ya da ödemeye dokun.</p>
      {message && <p role="status" className={`text-xs font-semibold ${message.tone === "success" ? "text-[var(--success-strong)]" : "text-[var(--danger-strong)]"}`}>{message.text}</p>}

      {blocks.map((cycle) => <HistoryCard
        key={cycle.number}
        card={card}
        cycle={cycle}
        current={cycle.number === nextBlock}
        awaiting={card.state === "AwaitingPayment" && cycle.number === card.currentCycle}
        editing={editing?.cycle === cycle.number ? editing : null}
        entriesFrom={entriesFrom}
        onEdit={(slot, target) => toggle({ cycle: cycle.number, slot, target })}
        onDone={() => setEditing(null)}
      />)}

      {card.earlierLessons.length > 0 && <section aria-label="İlk giriş öncesi dersler" className="rounded-2xl border border-dashed border-[var(--line)] px-3.5 py-3">
        <p className="text-micro text-[var(--muted)]">Karta sayılmayan önceki dersler</p>
        <ul className="mt-2 flex flex-wrap gap-1">
          {[...card.earlierLessons].reverse().map((lesson) => <li key={lesson.lessonId ?? lesson.entryId} className="rounded-md bg-[var(--surface-muted)] px-1.5 py-0.5 text-[.7rem] font-semibold tabular-nums">
            {short(lesson.date)}{lesson.status === "Present" ? " ✓" : lesson.status === "Excused" ? " M" : " ✗"}
          </li>)}
        </ul>
      </section>}
    </div>
  </Modal>;
}

function cycleTag(cycle: CardCycle, awaiting: boolean): { status: Status; text: string } {
  const receivable = cycle.receivable;
  if (awaiting) return { status: "soon", text: "Yeni kart başlıyor" };
  if (!receivable) return { status: "new", text: cycle.filled.length ? "Aidat açılmadı" : "Başlamadı" };
  if (receivable.status === "Paid") return { status: "ok", text: receivable.accruedOn ? "Ödendi" : "Peşin ödendi" };
  if (receivable.status === "Partial" || receivable.paid > 0) return { status: "soon", text: "Kısmi ödendi" };
  if (receivable.status === "Overdue") return { status: "owed", text: "Gecikti" };
  return { status: "owed", text: "Ödenmedi" };
}

function HistoryCard({ card, cycle, current, awaiting, editing, entriesFrom, onEdit, onDone }: {
  card: AttendanceCard;
  cycle: CardCycle;
  current: boolean;
  awaiting: boolean;
  editing: Editing | null;
  entriesFrom: string;
  onEdit: (slot: string, target: EditTarget) => void;
  onDone: () => void;
}) {
  const receivable = cycle.receivable;
  const tag = cycleTag(cycle, awaiting);
  const owed = !!receivable?.accruedOn && receivable.status !== "Paid" && receivable.status !== "Cancelled";
  const prepaidEmpty = !cycle.filled.length && receivable?.status === "Paid";
  const range = cycleRange(cycle);
  const period = receivable?.period;
  const covering = period && !isCyclePeriod(period) ? `${formatPeriod(period)} aidatı` : null;
  const slotClass = (slot: string) => `grid justify-items-center gap-1 rounded-xl px-1 pb-1.5 pt-1 text-center text-xs hover:bg-[var(--brand-soft)] ${editing?.slot === slot ? "bg-[var(--brand-soft)]" : ""}`;

  return <section className={`grid gap-3 rounded-2xl border px-3.5 py-3 ${owed ? "border-[color-mix(in_srgb,var(--danger)_45%,var(--line))] bg-[var(--surface)]" : prepaidEmpty || !cycle.filled.length ? "border-dashed border-[var(--line)]" : "border-[var(--line)] bg-[var(--surface)]"}`}>
    <header className="flex flex-wrap items-center justify-between gap-2">
      <span className="text-sm font-bold">{cycle.number}. kart{range && <span className="ml-1.5 text-xs font-semibold tabular-nums text-[var(--muted)]">{range}</span>}</span>
      <StatusPill status={tag.status}>{tag.text}</StatusPill>
    </header>

    <div className="grid grid-cols-[repeat(4,minmax(0,1fr))_0.75rem_minmax(0,1.3fr)] items-start gap-1">
      {[0, 1, 2, 3].map((index) => {
        const lesson = cycle.filled[index];
        const slot = String(index);
        if (lesson) {
          const target: EditTarget = lesson.entryId ? { kind: "entry", entryId: lesson.entryId, date: lesson.date } : { kind: "system", lesson };
          return <button key={slot} type="button" onClick={() => onEdit(slot, target)} aria-expanded={editing?.slot === slot} aria-label={`${index + 1}. ders ${short(lesson.date)}, düzelt`} className={slotClass(slot)}>
            <Hole filled label="" size="lg" />
            <span className="font-bold tabular-nums underline decoration-[var(--muted)] decoration-dotted underline-offset-4">{short(lesson.date)}</span>
          </button>;
        }
        if (current && index === cycle.filled.length) return <button key={slot} type="button" onClick={() => onEdit(slot, { kind: "add" })} aria-expanded={editing?.slot === slot} aria-label="Geçmiş ders ekle" className={slotClass(slot)}>
          <span aria-hidden className="grid size-10 place-items-center rounded-full border-2 border-dashed border-[var(--brand)] text-base font-bold text-[var(--brand-strong)]">+</span>
          <span className="text-[var(--muted)]">tarih ekle</span>
        </button>;
        return <div key={slot} className="grid justify-items-center gap-1 px-1 pb-1.5 pt-1 text-xs text-[var(--muted)]"><Hole filled={false} label={String(index + 1)} size="lg" />—</div>;
      })}
      <span aria-hidden className="mt-5 border-t-2 border-dotted border-[var(--line)]" />
      {receivable?.paidOn
        ? <button type="button" disabled={!receivable.paymentIds?.length} onClick={() => onEdit("payment", { kind: "payment", paymentIds: receivable.paymentIds ?? [], date: receivable.paidOn! })} aria-expanded={editing?.slot === "payment"} aria-label={`Ödeme ${short(receivable.paidOn)}, tarihi düzelt`} title={covering ?? undefined} className={slotClass("payment")}>
          <Coin tone={receivable.status === "Paid" ? "paid" : "owed"} size="lg" />
          <span className="font-bold tabular-nums underline decoration-[var(--muted)] decoration-dotted underline-offset-4">{short(receivable.paidOn)}</span>
          <span className="text-meta tabular-nums">{formatMoney(receivable.paid, receivable.currency)}{receivable.status !== "Paid" ? ` · ${formatMoney(receivable.amount - receivable.paid, receivable.currency)} kaldı` : ""}</span>
        </button>
        : <div className="grid justify-items-center gap-1 px-1 pb-1.5 pt-1 text-center text-xs" title={covering ?? undefined}>
          <Coin tone={owed ? "owed" : "none"} size="lg" />
          {owed ? <><span className="font-bold tabular-nums text-[var(--danger-strong)]">vade {short(receivable!.dueDate)}</span><span className="text-meta tabular-nums">{formatMoney(receivable!.amount, receivable!.currency)}</span></> : <span className="text-[var(--muted)]">—</span>}
        </div>}
    </div>

    {cycle.misses.length > 0 && <div className="flex flex-wrap gap-1">
      {cycle.misses.map((lesson) => {
        const slot = `miss-${lesson.lessonId}`;
        const label = `${ATTENDANCE_LABELS[lesson.status]} ${short(lesson.date)}`;
        return <button key={slot} type="button" onClick={() => onEdit(slot, { kind: "system", lesson })} aria-expanded={editing?.slot === slot} aria-label={`${label}, düzelt`} className={`pressable min-h-7 rounded-md px-2 text-[.68rem] font-semibold underline decoration-dotted underline-offset-2 ${lesson.status === "Excused" ? "bg-[var(--warning-soft)] text-[var(--warning-strong)]" : "bg-[var(--danger-soft)] text-[var(--danger-strong)]"}`}>{label}</button>;
      })}
    </div>}

    {editing && (editing.target.kind === "system"
      ? <AttendanceEditor key={editing.slot} lesson={editing.target.lesson} onDone={onDone} />
      : <DateEditor key={editing.slot} card={card} target={editing.target} entriesFrom={entriesFrom} onDone={onDone} />)}
  </section>;
}

// Yoklamadan gelen dersin durumu kartta düzeltilir (AD19); takvimdeki düzeltmeyle aynı uç nokta,
// kartı ve dönem aidatını sunucu baştan sayar. Dersin günü dersin kendisidir, takvimde değişir.
function AttendanceEditor({ lesson, onDone }: { lesson: CardLesson; onDone: () => void }) {
  const mark = useMarkAttendance(lesson.lessonId!);
  const [error, setError] = useState<string | null>(null);

  async function choose(status: AttendanceStatus) {
    setError(null);
    try {
      await mark.mutateAsync({ status });
      onDone();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Kaydedilemedi.");
    }
  }

  return <div className="grid gap-2 rounded-xl bg-[var(--brand-soft)] px-3 py-2.5 text-sm">
    <p><strong>{short(lesson.date)}</strong> dersinin yoklaması</p>
    <div className="grid grid-cols-3 gap-1.5">
      {(["Present", "Absent", "Excused"] as const).map((status) => {
        const active = lesson.status === status;
        return <button key={status} type="button" aria-pressed={active} disabled={active || mark.isPending} onClick={() => void choose(status)} className={`pressable min-h-11 rounded-xl border px-2 text-xs font-bold ${active ? "border-[color:var(--brand)] bg-[var(--surface)] text-[var(--brand-strong)]" : "border-[var(--line)] bg-[var(--surface)] text-[var(--muted)]"}`}>{ATTENDANCE_LABELS[status]}</button>;
      })}
    </div>
    <p className="text-meta text-xs">Yalnızca Geldi kartı doldurur; değişince kart ve aidat yeniden sayılır.</p>
    <div className="flex flex-wrap gap-1.5">
      {lesson.lessonId && <Link href={`/dashboard/calendar?lesson=${lesson.lessonId}`} className="btn btn-quiet">Takvimde aç</Link>}
      <button type="button" onClick={onDone} className="btn btn-quiet">Kapat</button>
    </div>
    {error && <p role="alert" className="text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
  </div>;
}

// Kartın altında açılan tarih kutusu. Elle yazılmış ders (AD10) değişir ya da kaldırılır, boş deliğe
// geçmiş ders eklenir; tarih geçen ayın 1'i ile bugün arasında olmalı (AD11). Ödeme tarihi düzeltmesi
// ödemeyi değiştirmez, ayrı kayıt olarak yazılır (PaymentCorrections.cs). Kuralları sunucu da denetler.
function DateEditor({ card, target, entriesFrom, onDone }: { card: AttendanceCard; target: Exclude<EditTarget, { kind: "system" }>; entriesFrom: string; onDone: () => void }) {
  const add = useAddCardEntry();
  const change = useChangeCardEntry();
  const remove = useRemoveCardEntry();
  const correct = useCorrectPaymentDates();
  const initial = target.kind === "entry" || target.kind === "payment" ? target.date : "";
  const [value, setValue] = useState(initial);
  const [error, setError] = useState<string | null>(null);
  const busy = add.isPending || change.isPending || remove.isPending || correct.isPending;

  async function run(action: () => Promise<unknown>) {
    setError(null);
    try {
      await action();
      onDone();
    } catch (err) {
      setError(err instanceof ApiError ? (err.errors?.date?.[0] ?? err.errors?.paymentDate?.[0] ?? err.detail ?? err.title) : "Kaydedilemedi.");
    }
  }

  function save() {
    if (!value || value === initial) { onDone(); return; }
    if (target.kind === "payment")
      void run(() => correct.mutateAsync({ paymentIds: target.paymentIds, paymentDate: value, reason: "Yoklama kartında ödeme tarihi düzeltildi" }));
    else if (target.kind === "entry")
      void run(() => change.mutateAsync({ entryId: target.entryId, date: value }));
    else
      void run(() => add.mutateAsync({ enrollmentId: card.enrollmentId, date: value }));
  }

  const label = target.kind === "payment" ? "Ödeme tarihi" : target.kind === "entry" ? "Ders tarihi" : "Geçmiş ders ekle";
  return <form
    className="grid gap-2 rounded-xl bg-[var(--brand-soft)] px-3 py-2.5"
    onSubmit={(event) => { event.preventDefault(); save(); }}
    onKeyDown={(event) => { if (event.key === "Escape") { event.stopPropagation(); onDone(); } }}
  >
    <label className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm font-bold">
      {label}
      <input type="date" autoFocus required value={value} min={target.kind === "payment" ? undefined : entriesFrom} max={todayInput()} disabled={busy} onChange={(event) => setValue(event.target.value)} className="field !min-h-9 w-auto py-1 font-medium" />
    </label>
    <div className="flex flex-wrap gap-1.5">
      <button type="submit" disabled={busy} className="btn btn-primary">{busy ? "Kaydediliyor…" : "Kaydet"}</button>
      {target.kind === "entry" && <button type="button" disabled={busy} onClick={() => void run(() => remove.mutateAsync(target.entryId))} className="btn btn-quiet">Dersi kaldır</button>}
      <button type="button" onClick={onDone} className="btn btn-quiet">Vazgeç</button>
    </div>
    {target.kind !== "payment" && <p className="text-meta text-xs">Geçen ayın 1&apos;inden bugüne kadar bir gün seçilebilir.</p>}
    {error && <p role="alert" className="text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
  </form>;
}
