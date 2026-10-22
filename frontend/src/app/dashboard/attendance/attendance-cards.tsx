"use client";

import { useMemo, useState, useSyncExternalStore } from "react";
import { Icon } from "@/components/icons";
import { EmptyState, FilterField, Modal, SearchInput, StatStrip, type Tone } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useAddCardEntry, useAttendanceCards, useChangeCardEntry, useRemoveCardEntry, type AttendanceCard, type CardCycle, type CardState } from "@/lib/attendance";
import { useBillingBoard, useSendPaymentReminder } from "@/lib/billing";
import { cycleKey, formatMoney, todayInput } from "@/lib/billing-format";
import { CollectSheet } from "../billing/collect-sheet";

// Yönetici yoklama ekranı (docs/10-decisions.md AD): okulun kâğıt "Yoklama Listesi"nin dijital
// hali. Birebir kayıt başına bir kart; 4 satır bir dönemdir. Geldi ve Mazeretli satır doldurur,
// Gelmedi kartın altına not düşer. 4. satır dolunca "ödeme bekleniyor" uyarısı çıkar; sonraki
// derste yeni dönemin aidatı tahakkuk eder. Kartı ve durumu sunucu kurar (AttendanceCards.cs).
// Tahsilat buradan da aidat ekranının tek ödeme penceresiyle (CollectSheet) alınır.

type Filter = "all" | "AwaitingPayment" | "LastLesson" | "Unpaid";

const STATE_TONE: Record<CardState, Tone> = {
  NotStarted: "muted",
  InProgress: "success",
  LastLesson: "brand",
  AwaitingPayment: "warning",
  NextPrepaid: "success",
  Unpaid: "danger",
};

const PILL_CLASS: Record<Tone, string> = {
  success: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  warning: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  danger: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  brand: "bg-[var(--brand-soft)] text-[var(--brand-strong)]",
  muted: "bg-[var(--surface-muted)] text-[var(--muted)]",
};

function stateLabel(card: AttendanceCard) {
  switch (card.state) {
    case "NotStarted": return !card.countingFrom || !card.hasOwnCycleStart && dayOf(card.countingFrom) > new Date() ? "İlk ders yazılmadı" : dayOf(card.countingFrom) > new Date() ? `Başlangıç ${startLabel.format(dayOf(card.countingFrom))}` : "Henüz ders yok";
    case "InProgress": return `Devam ediyor · ${card.filledInCurrent}/4`;
    case "LastLesson": return "Son derse 1 kaldı";
    case "AwaitingPayment": return "Ödeme bekleniyor";
    case "NextPrepaid": return "Sonraki dönem peşin ödendi";
    case "Unpaid": return `Ödenmedi · ${formatMoney(card.unpaidAmount, card.currency)}`;
  }
}

const shortDate = new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "2-digit" });
const longDate = new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "2-digit", year: "numeric" });
const startLabel = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long" });
const dayOf = (iso: string) => new Date(`${iso}T00:00:00`);

const MISS_CLASS = {
  Present: "bg-[var(--surface-muted)] text-[var(--foreground)]",
  Excused: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  Absent: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
} as const;

function StatePill({ card }: { card: AttendanceCard }) {
  return <span className={`inline-flex items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-bold ${PILL_CLASS[STATE_TONE[card.state]]}`}>
    <span aria-hidden className="size-1.5 rounded-full bg-current" />{stateLabel(card)}
  </span>;
}

// Kart yalnızca çok geniş ekranda tablonun yanında durur; daha darda uzun listenin en altına
// düşüyordu (77 öğrencide görünmez oluyordu), orada satıra basınca açılır pencerede gelir.
const WIDE_QUERY = "(min-width: 1536px)";
function subscribeWide(onChange: () => void) {
  const media = window.matchMedia(WIDE_QUERY);
  media.addEventListener("change", onChange);
  return () => media.removeEventListener("change", onChange);
}
function useWideScreen() {
  return useSyncExternalStore(subscribeWide, () => window.matchMedia(WIDE_QUERY).matches, () => false);
}

export function AttendanceCards() {
  const { data, isLoading, isError, refetch } = useAttendanceCards();
  const [filter, setFilter] = useState<Filter>("all");
  const [query, setQuery] = useState("");
  const [instrument, setInstrument] = useState("");
  const [teacher, setTeacher] = useState("");
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const cards = useMemo(
    () => [...(data?.cards ?? [])].sort((a, b) => a.studentName.localeCompare(b.studentName, "tr-TR") || a.instrumentName.localeCompare(b.instrumentName, "tr-TR")),
    [data],
  );
  const counts = useMemo(() => ({
    awaiting: cards.filter((card) => card.state === "AwaitingPayment"),
    last: cards.filter((card) => card.state === "LastLesson").length,
    unpaid: cards.filter((card) => card.state === "Unpaid").length,
  }), [cards]);
  const instruments = useMemo(() => [...new Set(cards.map((card) => card.instrumentName))].sort((a, b) => a.localeCompare(b, "tr-TR")), [cards]);
  const teachers = useMemo(() => [...new Set(cards.map((card) => card.teacherName))].sort((a, b) => a.localeCompare(b, "tr-TR")), [cards]);

  const visible = useMemo(() => {
    const q = query.trim().toLocaleLowerCase("tr-TR");
    return cards.filter((card) =>
      (!q || card.studentName.toLocaleLowerCase("tr-TR").includes(q))
      && (!instrument || card.instrumentName === instrument)
      && (!teacher || card.teacherName === teacher)
      && (filter === "all" || card.state === filter));
  }, [cards, query, instrument, teacher, filter]);

  const wide = useWideScreen();
  const selected = cards.find((card) => card.enrollmentId === selectedId) ?? (wide ? visible[0] ?? null : null);

  if (isLoading) return <div className="space-y-3">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-20 rounded-2xl" />)}</div>;
  if (isError || !data) return <div className="app-card p-6 text-center"><p className="text-sm font-bold">Yoklama kartları yüklenemedi.</p><button type="button" onClick={() => refetch()} className="btn btn-quiet mt-2">Tekrar dene</button></div>;

  const toggle = (next: Filter) => setFilter((current) => (current === next ? "all" : next));

  return <div className="space-y-3">
    {counts.awaiting.length > 0 && <section aria-live="polite" className="flex flex-wrap items-center gap-x-3.5 gap-y-2 rounded-2xl border border-[color-mix(in_srgb,var(--warning)_35%,transparent)] bg-[var(--warning-soft)] px-3.5 py-3 text-[var(--warning-strong)]">
      <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-[var(--surface)]"><Icon name="bell" className="h-4 w-4" /></span>
      <span className="min-w-0 flex-[1_1_16rem]">
        <strong className="block text-sm">{counts.awaiting.length} öğrenci 4 dersini tamamladı, ödeme bekleniyor</strong>
        <span className="block text-xs">{counts.awaiting.map((card) => card.studentName).join(", ")} · bir sonraki derslerinde yeni dönem aidatı tahakkuk edecek.</span>
      </span>
      <button type="button" onClick={() => toggle("AwaitingPayment")} className="btn shrink-0 border-transparent bg-[var(--warning)] text-white hover:opacity-90">{filter === "AwaitingPayment" ? "Tümünü göster" : "Listele"}</button>
    </section>}

    <StatStrip
      label="Duruma göre filtrele"
      items={[
        { key: "all", label: "Birebir öğrenci", value: cards.length, active: filter === "all", onClick: () => setFilter("all") },
        { key: "AwaitingPayment", label: "Ödeme bekleniyor", value: counts.awaiting.length, tone: "warning", active: filter === "AwaitingPayment", onClick: () => toggle("AwaitingPayment") },
        { key: "LastLesson", label: "Son derse 1 kaldı", value: counts.last, active: filter === "LastLesson", onClick: () => toggle("LastLesson") },
        { key: "Unpaid", label: "Ödenmemiş dönem", value: counts.unpaid, tone: "danger", active: filter === "Unpaid", onClick: () => toggle("Unpaid") },
      ]}
    />

    <section className="app-card flex flex-wrap items-end gap-x-3 gap-y-2 p-3">
      <FilterField label="Öğrenci" className="min-w-[11rem] flex-1"><SearchInput value={query} onChange={setQuery} label="Öğrenci ara" placeholder="Öğrenci ara…" /></FilterField>
      <FilterField label="Branş" className="min-w-[9rem] flex-1">
        <select value={instrument} onChange={(event) => setInstrument(event.target.value)} className="field text-sm">
          <option value="">Tüm branşlar</option>
          {instruments.map((name) => <option key={name} value={name}>{name}</option>)}
        </select>
      </FilterField>
      <FilterField label="Öğretmen" className="min-w-[9rem] flex-1">
        <select value={teacher} onChange={(event) => setTeacher(event.target.value)} className="field text-sm">
          <option value="">Tüm öğretmenler</option>
          {teachers.map((name) => <option key={name} value={name}>{name}</option>)}
        </select>
      </FilterField>
    </section>

    <div className="grid items-start gap-3 2xl:grid-cols-[minmax(0,1fr)_31rem]">
      <section className="app-card min-w-0 overflow-hidden">
        {!visible.length && <EmptyState title="Bu filtrede öğrenci yok" description="Filtreyi kaldırmayı ya da aramayı temizlemeyi dene." />}
        {!!visible.length && <div className="overflow-x-auto">
          <table className="w-full min-w-[40rem] text-sm">
            <thead>
              <tr className="text-micro border-b border-[var(--line)] text-left text-[var(--muted)]">
                <th className="px-3 py-2.5">Öğrenci</th>
                <th className="px-3 py-2.5">İlk giriş</th>
                <th className="px-3 py-2.5">Dönem</th>
                <th className="px-3 py-2.5">Bu dönem (4 ders)</th>
                <th className="px-3 py-2.5">Durum</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--line)]">
              {visible.map((card) => {
                const current = card.cycles.find((cycle) => cycle.number === card.currentCycle);
                const isSelected = selected?.enrollmentId === card.enrollmentId;
                const full = card.filledInCurrent === 4;
                return <tr
                  key={card.enrollmentId}
                  tabIndex={0}
                  aria-selected={isSelected}
                  onClick={() => setSelectedId(card.enrollmentId)}
                  onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); setSelectedId(card.enrollmentId); } }}
                  className={`cursor-pointer ${isSelected ? "bg-[var(--brand-soft)]" : "hover:bg-[var(--surface-muted)]"}`}
                >
                  <td className="px-3 py-2.5">
                    <span className="block font-bold">{card.studentName}</span>
                    <span className="text-meta block text-xs">{card.instrumentName} · {card.teacherName}</span>
                    {card.currentCycle === 0 && card.earlierLessons.length > 0 && <span className="text-meta block text-[.7rem]">İlk giriş öncesi {card.earlierLessons.filter((lesson) => lesson.status !== "Absent").length} ders işlendi</span>}
                  </td>
                  <td className="px-3 py-2.5 tabular-nums whitespace-nowrap">{card.hasOwnCycleStart && card.countingFrom ? longDate.format(dayOf(card.countingFrom)) : <span className="text-[var(--muted)]">—</span>}</td>
                  <td className="px-3 py-2.5 tabular-nums whitespace-nowrap">{card.currentCycle ? `${card.currentCycle}. dönem` : "—"}</td>
                  <td className="px-3 py-2.5">
                    <span className="inline-grid grid-cols-4 gap-1">
                      {[0, 1, 2, 3].map((index) => {
                        const lesson = current?.filled[index];
                        return lesson
                          ? <span key={index} title={lesson.status === "Excused" ? "Mazeretli" : "Geldi"} className={`grid h-7 w-11 place-items-center rounded-lg text-[.68rem] font-bold tabular-nums ${full ? "bg-[var(--warning-soft)] text-[var(--warning-strong)]" : "bg-[var(--success-soft)] text-[var(--success-strong)]"} ${lesson.status === "Excused" ? "ring-[1.5px] ring-inset ring-[var(--warning)]" : ""}`}>{shortDate.format(dayOf(lesson.date))}</span>
                          : <span key={index} className="grid h-7 w-11 place-items-center rounded-lg border-[1.5px] border-dashed border-[var(--line)] text-[.68rem] font-bold text-[var(--muted)]">{index + 1}</span>;
                      })}
                    </span>
                  </td>
                  <td className="px-3 py-2.5 whitespace-nowrap"><StatePill card={card} /></td>
                </tr>;
              })}
            </tbody>
          </table>
        </div>}
      </section>

      {selected && wide && <AttendanceSheet key={selected.enrollmentId} card={selected} entriesFrom={data.entriesFrom} />}
    </div>

    {selected && !wide && <Modal open size="lg" title={selected.studentName} description={`${selected.instrumentName} · ${selected.teacherName}`} onClose={() => setSelectedId(null)}>
      <AttendanceSheet key={selected.enrollmentId} card={selected} entriesFrom={data.entriesFrom} />
    </Modal>}
  </div>;
}

// ---- Kart ----------------------------------------------------------------------

function AttendanceSheet({ card, entriesFrom }: { card: AttendanceCard; entriesFrom: string }) {
  const year = new Date().getFullYear();
  const { data: board } = useBillingBoard(year);
  const reminder = useSendPaymentReminder();
  const [collecting, setCollecting] = useState<string[] | null>(null);
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const row = board?.find((item) => item.enrollmentId === card.enrollmentId);

  // Ödenmemiş başlamış dönemler; "ödeme bekleniyor"da sıradaki dönem peşin alınır.
  const unpaid = card.cycles.filter((cycle) => cycle.receivable?.accruedOn && !["Paid", "Cancelled"].includes(cycle.receivable.status));
  const collectPeriods = card.state === "AwaitingPayment" ? [cycleKey(card.currentCycle + 1)] : unpaid.map((cycle) => cycleKey(cycle.number));
  const remindTarget = unpaid.at(-1)?.receivable ?? null;

  async function remind() {
    if (!remindTarget) return;
    setMessage(null);
    try {
      const result = await reminder.mutateAsync(remindTarget.id);
      setMessage(result.scheduled
        ? { tone: "success", text: "Veliye WhatsApp hatırlatması sıraya alındı." }
        : { tone: "error", text: "Hatırlatma gönderilemedi: öğrencinin birincil velisi ya da mesaj izni yok." });
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? (err.detail ?? err.title) : "Hatırlatma gönderilemedi." });
    }
  }

  // Kâğıt şablonu: her öğrencide en az 2 tablo (4 + 4 satır) açık. Gösterilen son tablo bir sonraki
  // boş satırın düştüğü dönemdir; ondan öncekiler "Önceki dönemler"de durur.
  const [showOlder, setShowOlder] = useState(false);
  const filledTotal = card.cycles.reduce((sum, cycle) => sum + cycle.filled.length, 0);
  const nextBlock = Math.floor(filledTotal / 4) + 1;
  const lastShown = Math.max(nextBlock, 2, ...card.cycles.map((cycle) => cycle.number));
  const blocks: CardCycle[] = Array.from({ length: lastShown }, (_, index) =>
    card.cycles.find((cycle) => cycle.number === index + 1) ?? { number: index + 1, filled: [], misses: [], receivable: null });
  const firstShown = Math.max(1, Math.min(nextBlock, lastShown) - 1);
  const older = blocks.filter((cycle) => cycle.number < firstShown);
  const recent = blocks.filter((cycle) => cycle.number >= firstShown);
  return <article aria-label={`${card.studentName} yoklama kartı`} className="min-w-0 overflow-hidden rounded-2xl border border-[var(--line)] bg-[var(--surface)] shadow-[var(--shadow-card)] 2xl:sticky 2xl:top-4">
    <div className="flex items-baseline justify-between gap-2 border-b-2 border-[var(--foreground)] px-4 py-3">
      <span className="text-xs font-extrabold uppercase tracking-[.08em]">Abdera Sanat Akademi</span>
      <span className="text-xs font-extrabold uppercase tracking-[.08em] text-[var(--muted)]">Yoklama listesi</span>
    </div>
    <dl className="grid grid-cols-2 border-b-2 border-[var(--foreground)]">
      <div className="col-span-2 border-b border-[var(--line)] px-4 py-2">
        <dt className="text-[.66rem] font-extrabold uppercase tracking-[.08em] text-[var(--muted)]">Adı soyadı</dt>
        <dd className="font-serif text-lg italic">{card.studentName}</dd>
      </div>
      <div className="border-r border-[var(--line)] px-4 py-2">
        <dt className="text-[.66rem] font-extrabold uppercase tracking-[.08em] text-[var(--muted)]">Branşı</dt>
        <dd className="font-serif text-lg italic">{card.instrumentName}</dd>
      </div>
      <CycleStartField card={card} />
    </dl>

    <div className="flex flex-wrap items-center gap-2 border-b border-[var(--line)] px-4 py-2.5">
      <StatePill card={card} />
      <span className="flex-1" />
      {remindTarget && <button type="button" onClick={remind} disabled={reminder.isPending} className="btn btn-quiet text-xs">{reminder.isPending ? "Gönderiliyor…" : "Hatırlatma gönder"}</button>}
      {collectPeriods.length > 0 && <button type="button" onClick={() => setCollecting(collectPeriods)} disabled={!row} className="btn btn-primary text-xs">Tahsil et</button>}
    </div>
    {message && <p role="status" className={`border-b border-[var(--line)] px-4 py-2 text-xs font-semibold ${message.tone === "success" ? "text-[var(--success-strong)]" : "text-[var(--danger-strong)]"}`}>{message.text}</p>}

    {card.state === "AwaitingPayment" && <p className="border-b border-[var(--line)] bg-[var(--warning-soft)] px-4 py-2.5 text-xs text-[var(--warning-strong)]">
      <strong className="block text-sm">Bu dönemin 4 dersi tamamlandı.</strong>
      Sıradaki derste {card.currentCycle + 1}. dönem aidatı tahakkuk eder. Ödemeyi şimdiden almak için &quot;Tahsil et&quot;.
    </p>}
    {card.state === "Unpaid" && <p className="border-b border-[var(--line)] bg-[var(--danger-soft)] px-4 py-2.5 text-xs text-[var(--danger-strong)]">
      <strong className="block text-sm">Başlamış dönemin ödemesi alınmadı.</strong>
      Kalan {formatMoney(card.unpaidAmount, card.currency)}.
    </p>}

    {older.length > 0 && <button type="button" onClick={() => setShowOlder((value) => !value)} className="w-full border-b border-[var(--line)] px-4 py-2 text-left text-xs font-bold text-[var(--brand-strong)]">
      {showOlder ? "Önceki dönemleri gizle" : `Önceki ${older.length} dönemi göster`}
    </button>}
    <div className="grid sm:grid-cols-2">
      {(showOlder ? [...older, ...recent] : recent).map((cycle) => <CycleBlock
        key={cycle.number}
        card={card}
        entriesFrom={entriesFrom}
        cycle={cycle}
        current={cycle.number === nextBlock}
        awaiting={card.state === "AwaitingPayment" && cycle.number === card.currentCycle}
      />)}
    </div>
    {card.earlierLessons.length > 0 && <section aria-label="İlk giriş öncesi dersler" className="border-t border-[var(--line)] px-4 py-3">
      <p className="text-micro text-[var(--muted)]">{card.countingFrom ? `İlk giriş öncesi · ${startLabel.format(dayOf(card.countingFrom))} öncesi, karta sayılmaz ve aidat açmaz` : "İşlenen dersler · tarihini 1. satıra yazınca kart o dersten başlar"}</p>
      <ul className="mt-2 flex flex-wrap gap-1">
        {[...card.earlierLessons].reverse().map((lesson) => <li key={lesson.lessonId ?? lesson.entryId} title={lesson.status === "Present" ? "Geldi" : lesson.status === "Excused" ? "Mazeretli" : "Gelmedi"} className={`rounded-md px-1.5 py-0.5 text-[.7rem] font-semibold tabular-nums ${MISS_CLASS[lesson.status]}`}>
          {shortDate.format(dayOf(lesson.date))}{lesson.status === "Present" ? " ✓" : lesson.status === "Excused" ? " M" : " ✗"}
        </li>)}
      </ul>
    </section>}
    <p className="text-meta flex flex-wrap gap-x-3 gap-y-1 border-t border-[var(--line)] px-4 py-2.5 text-[.72rem]">
      <span>✓ Geldi ve <span className="font-bold text-[var(--warning-strong)]">Mazeretli</span> satırı doldurur</span>
      <span><span className="font-bold text-[var(--danger-strong)]">Gelmedi</span> satır doldurmaz</span>
      <span>Boş satıra tarih yazarak geçmiş dersi ekle (en erken {longDate.format(dayOf(entriesFrom))}); ilk satır kartın başlangıcıdır, sonrası yoklamadan gelir</span>
    </p>

    {collecting && row && <CollectSheet
      row={row}
      year={year}
      initialPeriods={collecting}
      onClose={() => setCollecting(null)}
      onCollected={(text) => { setCollecting(null); setMessage({ tone: "success", text }); }}
    />}
  </article>;
}

function CycleBlock({ card, entriesFrom, cycle, current, awaiting }: { card: AttendanceCard; entriesFrom: string; cycle: CardCycle; current: boolean; awaiting: boolean }) {
  const [error, setError] = useState<string | null>(null);
  const receivable = cycle.receivable;
  const tag = awaiting
    ? { tone: "warning" as const, text: "Ödeme bekleniyor" }
    : !receivable ? (cycle.filled.length ? { tone: "muted" as const, text: "Aidat açılmadı" } : { tone: "muted" as const, text: "Başlamadı" })
    : receivable.status === "Paid" ? { tone: "success" as const, text: receivable.accruedOn ? "Ödendi" : "Peşin ödendi" }
    : receivable.status === "Partial" || receivable.paid > 0 ? { tone: "warning" as const, text: "Kısmi" }
    : receivable.status === "Overdue" ? { tone: "danger" as const, text: "Gecikti" }
    : { tone: "danger" as const, text: "Ödenmedi" };

  return <section className={`border-r border-b-2 border-[var(--line)] border-b-[var(--foreground)] ${awaiting ? "shadow-[inset_0_0_0_2px_var(--warning)]" : ""}`}>
    <header className="flex items-center justify-between gap-1.5 bg-[var(--surface-muted)] px-2.5 py-1.5 text-[.72rem] font-bold">
      <span className="uppercase tracking-[.04em] text-[var(--muted)]">{cycle.number}. dönem</span>
      <span className={`rounded-full px-2 py-0.5 ${PILL_CLASS[tag.tone]}`}>{tag.text}</span>
    </header>
    {[0, 1, 2, 3].map((index) => {
      const lesson = cycle.filled[index];
      const next = current && !lesson && index === cycle.filled.length;
      return <div key={lesson ? lesson.lessonId ?? lesson.entryId : `empty-${index}`} className={`grid min-h-9 grid-cols-[1.2rem_1fr_1.8rem] items-center border-t border-[var(--line)] px-2.5 ${next ? "bg-[var(--brand-soft)]" : ""}`}>
        <span className="text-[.66rem] font-bold text-[var(--muted)]">{index + 1}</span>
        {lesson && !lesson.entryId && <span className="flex min-w-0 items-center gap-1.5 font-serif italic tabular-nums" title="Sistemdeki yoklamadan">
          {longDate.format(dayOf(lesson.date)).replace(/\./g, " / ")}
          {lesson.status === "Excused" && <span className="rounded-md bg-[var(--warning-soft)] px-1.5 font-sans text-[.66rem] font-semibold not-italic text-[var(--warning-strong)]">Mazeretli</span>}
        </span>}
        {lesson?.entryId && <EntryInput key={lesson.entryId} card={card} entriesFrom={entriesFrom} entryId={lesson.entryId} date={lesson.date} onError={setError} />}
        {!lesson && <EntryInput card={card} entriesFrom={entriesFrom} entryId={null} date={null} onError={setError} />}
        <span className={`grid h-full place-items-center border-l border-[var(--line)] text-base font-bold ${lesson?.status === "Excused" ? "text-[var(--warning-strong)]" : "text-[var(--success-strong)]"}`}>{lesson ? "✓" : ""}</span>
      </div>;
    })}
    {cycle.misses.length > 0 && <div className="flex flex-wrap gap-1 border-t border-dashed border-[var(--line)] px-2.5 py-1.5">
      {cycle.misses.map((lesson) => <span key={lesson.lessonId ?? lesson.entryId} className="rounded-md bg-[var(--danger-soft)] px-1.5 text-[.68rem] font-semibold text-[var(--danger-strong)]">Gelmedi {shortDate.format(dayOf(lesson.date))}</span>)}
    </div>}
    {error && <p role="alert" className="border-t border-[var(--line)] px-2.5 py-1.5 text-[.72rem] font-semibold text-[var(--danger-strong)]">{error}</p>}
  </section>;
}

// Kâğıttaki gibi elle yazılan satır (AD10): boş satıra tarih seçilince geçmiş ders eklenir; elle
// yazılmış satırın tarihi değiştirilebilir ya da × ile kaldırılır. Sistemdeki yoklama satırları
// salt okunur. Tarih geçen ayın 1'i ile bugün arasında olmalı; karta yazılan en erken gün kartın
// başlangıcıdır (AD11). Kuralları sunucu da denetler.
function EntryInput({ card, entriesFrom, entryId, date, onError }: { card: AttendanceCard; entriesFrom: string; entryId: string | null; date: string | null; onError: (message: string | null) => void }) {
  const add = useAddCardEntry();
  const change = useChangeCardEntry();
  const remove = useRemoveCardEntry();
  const [value, setValue] = useState(date ?? "");
  const busy = add.isPending || change.isPending || remove.isPending;

  async function run(action: () => Promise<unknown>) {
    onError(null);
    try {
      await action();
    } catch (err) {
      onError(err instanceof ApiError ? (err.errors?.date?.[0] ?? err.detail ?? err.title) : "Kaydedilemedi.");
      setValue(date ?? "");
    }
  }

  function commit() {
    if (!value || value === date) return;
    void run(() => entryId
      ? change.mutateAsync({ entryId, date: value })
      : add.mutateAsync({ enrollmentId: card.enrollmentId, date: value }).then(() => setValue("")));
  }

  return <span className="flex min-w-0 items-center gap-1">
    <input
      type="date"
      value={value}
      min={entriesFrom}
      max={todayInput()}
      disabled={busy}
      onChange={(event) => setValue(event.target.value)}
      onBlur={commit}
      onKeyDown={(event) => { if (event.key === "Enter") commit(); }}
      aria-label={entryId ? "Elle yazılmış ders tarihi" : "Geçmiş ders tarihi ekle"}
      className={`min-w-0 flex-1 rounded-md border bg-transparent px-1.5 py-0.5 font-serif text-sm italic tabular-nums ${entryId ? "border-transparent text-[var(--foreground)] hover:border-[var(--line)]" : "border-dashed border-[var(--line)] text-[var(--muted)]"}`}
    />
    {entryId && <button type="button" disabled={busy} onClick={() => void run(() => remove.mutateAsync(entryId))} aria-label="Elle yazılmış dersi kaldır" title="Kaldır" className="shrink-0 rounded px-1 text-sm font-bold text-[var(--muted)] hover:text-[var(--danger-strong)]">×</button>}
  </span>;
}

// Kartın "İlk giriş" alanı salt okunur: ayrı seçilmez, karta yazılan ilk satırın günüdür (AD11).
function CycleStartField({ card }: { card: AttendanceCard }) {
  return <div className="px-4 py-2">
    <dt className="text-[.66rem] font-extrabold uppercase tracking-[.08em] text-[var(--muted)]">İlk giriş</dt>
    <dd className="font-serif text-lg italic tabular-nums">
      {card.hasOwnCycleStart && card.countingFrom ? longDate.format(dayOf(card.countingFrom)) : <span className="text-meta block font-sans text-sm not-italic">1. satıra yazılan ilk ders</span>}
    </dd>
  </div>;
}
