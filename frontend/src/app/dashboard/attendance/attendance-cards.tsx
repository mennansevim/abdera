"use client";

import { useMemo, useState, useSyncExternalStore } from "react";
import { Icon } from "@/components/icons";
import { EmptyState, FilterField, Modal, SearchInput, StatStrip, type Tone } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useAttendanceCards, useSetCycleStart, type AttendanceCard, type CardCycle, type CardState } from "@/lib/attendance";
import { useBillingBoard, useSendPaymentReminder } from "@/lib/billing";
import { cycleKey, formatMoney } from "@/lib/billing-format";
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
    case "NotStarted": return !card.countingFrom ? "İlk giriş girilmedi" : dayOf(card.countingFrom) > new Date() ? `Başlangıç ${startLabel.format(dayOf(card.countingFrom))}` : "Henüz ders yok";
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
                  <td className="px-3 py-2" onClick={(event) => event.stopPropagation()} onKeyDown={(event) => event.stopPropagation()}>
                    <FirstEntryCell card={card} />
                  </td>
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

      {selected && wide && <AttendanceSheet key={selected.enrollmentId} card={selected} />}
    </div>

    {selected && !wide && <Modal open size="lg" title={selected.studentName} description={`${selected.instrumentName} · ${selected.teacherName}`} onClose={() => setSelectedId(null)}>
      <AttendanceSheet key={selected.enrollmentId} card={selected} />
    </Modal>}
  </div>;
}

// Tablodaki "İlk giriş" hücresi: 77 öğrencinin günü kartı tek tek açmadan buradan girilir. Gün
// seçilip alandan çıkınca (ya da Enter) kaydedilir; okul geneli gün soluk görünür.
function FirstEntryCell({ card }: { card: AttendanceCard }) {
  const setCycleStart = useSetCycleStart();
  const [value, setValue] = useState(card.countingFrom ?? "");
  const [state, setState] = useState<"idle" | "saved" | "error">("idle");
  const own = card.hasOwnCycleStart;

  async function commit() {
    if (!value || value === card.countingFrom && own) return;
    try {
      await setCycleStart.mutateAsync({ enrollmentId: card.enrollmentId, cycleStartsOn: value });
      setState("saved");
    } catch {
      setState("error");
    }
  }

  return <span className="flex items-center gap-1.5">
    <input
      type="date"
      value={value}
      onChange={(event) => { setValue(event.target.value); setState("idle"); }}
      onBlur={commit}
      onKeyDown={(event) => { if (event.key === "Enter") void commit(); }}
      aria-label={`${card.studentName} ilk giriş günü`}
      title={own ? "Öğrenciye girilen ilk giriş günü" : "Okul geneli geçiş günü - öğrencinin ilk giriş gününü seç"}
      className={`field !min-h-8 w-[9.5rem] px-2 text-xs tabular-nums ${own ? "" : "text-[var(--muted)]"}`}
    />
    <span aria-live="polite" className="w-4 text-xs font-bold">
      {setCycleStart.isPending ? "…" : state === "saved" ? <span className="text-[var(--success-strong)]" title="Kaydedildi">✓</span> : state === "error" ? <span className="text-[var(--danger-strong)]" title="Kaydedilemedi">!</span> : null}
    </span>
  </span>;
}

// ---- Kart ----------------------------------------------------------------------

function AttendanceSheet({ card }: { card: AttendanceCard }) {
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

  const cycles = [...card.cycles].reverse();
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

    {!cycles.length && <p className="text-meta px-4 py-6 text-center">{card.countingFrom
      ? `${longDate.format(dayOf(card.countingFrom))} itibarıyla henüz işlenmiş ders yok. O günden itibaren ilk ders 1. dönemi açar.`
      : "Öğrencinin ilk giriş gününü gir: o günden itibaren her 4 derste bir aidat açılır."}</p>}
    {!!cycles.length && <div className="grid max-h-[34rem] grid-cols-[repeat(auto-fill,minmax(13rem,1fr))] overflow-y-auto">
      {cycles.map((cycle) => <CycleBlock key={cycle.number} cycle={cycle} current={cycle.number === card.currentCycle} awaiting={card.state === "AwaitingPayment" && cycle.number === card.currentCycle} />)}
    </div>}
    {card.earlierLessons.length > 0 && <section aria-label="İlk giriş öncesi dersler" className="border-t border-[var(--line)] px-4 py-3">
      <p className="text-micro text-[var(--muted)]">{card.countingFrom ? `İlk giriş öncesi · ${startLabel.format(dayOf(card.countingFrom))} öncesi, karta sayılmaz ve aidat açmaz` : "İşlenen dersler · ilk giriş günü girilince karta sayılır"}</p>
      <ul className="mt-2 flex flex-wrap gap-1">
        {[...card.earlierLessons].reverse().map((lesson) => <li key={lesson.lessonId} title={lesson.status === "Present" ? "Geldi" : lesson.status === "Excused" ? "Mazeretli" : "Gelmedi"} className={`rounded-md px-1.5 py-0.5 text-[.7rem] font-semibold tabular-nums ${MISS_CLASS[lesson.status]}`}>
          {shortDate.format(dayOf(lesson.date))}{lesson.status === "Present" ? " ✓" : lesson.status === "Excused" ? " M" : " ✗"}
        </li>)}
      </ul>
    </section>}
    <p className="text-meta flex flex-wrap gap-x-3 gap-y-1 border-t border-[var(--line)] px-4 py-2.5 text-[.72rem]">
      <span>✓ Geldi ve <span className="font-bold text-[var(--warning-strong)]">Mazeretli</span> satırı doldurur</span>
      <span><span className="font-bold text-[var(--danger-strong)]">Gelmedi</span> satır doldurmaz</span>
      <span>En yeni dönem en üstte{card.countingFrom ? ` · ${longDate.format(dayOf(card.countingFrom))} itibarıyla` : ""}</span>
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

function CycleBlock({ cycle, current, awaiting }: { cycle: CardCycle; current: boolean; awaiting: boolean }) {
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
      return <div key={index} className={`grid min-h-8 grid-cols-[1.2rem_1fr_1.8rem] items-center border-t border-[var(--line)] px-2.5 ${next ? "bg-[var(--brand-soft)]" : ""}`}>
        <span className="text-[.66rem] font-bold text-[var(--muted)]">{index + 1}</span>
        {lesson
          ? <span className="flex min-w-0 items-center gap-1.5 font-serif italic tabular-nums">{longDate.format(dayOf(lesson.date)).replace(/\./g, " / ")}{lesson.status === "Excused" && <span className="rounded-md bg-[var(--warning-soft)] px-1.5 font-sans text-[.66rem] font-semibold not-italic text-[var(--warning-strong)]">Mazeretli</span>}</span>
          : <span className={`text-xs ${next ? "font-semibold text-[var(--brand-strong)]" : "tracking-[.3em] text-[var(--line)]"}`}>{next ? "Sıradaki ders" : "__ / __ / ____"}</span>}
        <span className={`grid h-full place-items-center border-l border-[var(--line)] text-base font-bold ${lesson?.status === "Excused" ? "text-[var(--warning-strong)]" : "text-[var(--success-strong)]"}`}>{lesson ? "✓" : ""}</span>
      </div>;
    })}
    {cycle.misses.length > 0 && <div className="flex flex-wrap gap-1 border-t border-dashed border-[var(--line)] px-2.5 py-1.5">
      {cycle.misses.map((lesson) => <span key={lesson.lessonId} className="rounded-md bg-[var(--danger-soft)] px-1.5 text-[.68rem] font-semibold text-[var(--danger-strong)]">Gelmedi {shortDate.format(dayOf(lesson.date))}</span>)}
    </div>}
  </section>;
}

// Kartın "İlk giriş" alanı: öğrencinin dönem sayacının başladığı gün (ay ortası olabilir). Boşsa okul
// geneli geçiş günü kullanılır. Değişince kart baştan sayılır, dönem aidatları yeni güne göre eşitlenir.
function CycleStartField({ card }: { card: AttendanceCard }) {
  const setCycleStart = useSetCycleStart();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(card.countingFrom ?? "");
  const [error, setError] = useState<string | null>(null);

  async function save(next: string | null) {
    setError(null);
    try {
      await setCycleStart.mutateAsync({ enrollmentId: card.enrollmentId, cycleStartsOn: next });
      setEditing(false);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "İlk giriş günü kaydedilemedi.");
    }
  }

  return <div className="px-4 py-2">
    <dt className="flex items-center justify-between gap-2 text-[.66rem] font-extrabold uppercase tracking-[.08em] text-[var(--muted)]">
      İlk giriş
      {!editing && <button type="button" onClick={() => { setValue(card.countingFrom ?? ""); setEditing(true); }} className="text-[.7rem] font-bold normal-case tracking-normal text-[var(--brand-strong)] underline underline-offset-2">Değiştir</button>}
    </dt>
    {!editing && <dd className="font-serif text-lg italic tabular-nums">
      {card.countingFrom ? longDate.format(dayOf(card.countingFrom)) : "—"}
      {card.countingFrom && !card.hasOwnCycleStart && <span className="text-meta block font-sans text-[.68rem] not-italic">okul geneli geçiş günü</span>}
    </dd>}
    {editing && <dd className="mt-1 space-y-1.5">
      <input type="date" value={value} onChange={(event) => setValue(event.target.value)} aria-label="İlk giriş günü" className="field !min-h-9 w-full text-sm" />
      <span className="flex flex-wrap gap-1.5">
        <button type="button" disabled={!value || setCycleStart.isPending} onClick={() => save(value)} className="btn btn-primary !min-h-8 text-xs">{setCycleStart.isPending ? "Kaydediliyor…" : "Kaydet"}</button>
        <button type="button" onClick={() => setEditing(false)} className="btn btn-quiet !min-h-8 text-xs">Vazgeç</button>
        {card.hasOwnCycleStart && <button type="button" disabled={setCycleStart.isPending} onClick={() => save(null)} className="btn btn-quiet !min-h-8 text-xs">Okul genelini kullan</button>}
      </span>
      <span className="text-meta block text-[.7rem]">Bu günden itibaren her 4 derste bir aidat açılır. O ayın aylık aidatı varsa 1. dönem sayılır.</span>
      {error && <span role="alert" className="block text-xs font-semibold text-[var(--danger-strong)]">{error}</span>}
    </dd>}
  </div>;
}
