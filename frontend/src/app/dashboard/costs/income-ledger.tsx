"use client";

import { useMemo, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { FormMessage, Modal } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { COURSE_KIND_LABEL, useCorrectPaymentDates } from "@/lib/billing";
import { formatPeriod, todayInput } from "@/lib/billing-format";
import { methodLabel } from "../billing/collect-sheet";
import { BUCKET_COLOR, BUCKET_LABEL, bucketOf, type IncomeBucket, type IncomeEntry } from "./income-breakdown";

// Tahsilat defteri (Gelir ve gider > Gelirler). Gün (yıllık görünümde ay) başına katlanabilir
// gruplar: başlık o günün toplamını, ödeme sayısını ve içindeki gecikmiş/peşin ödemeleri
// söyler, satırlar ancak açılınca görünür. "Tarih düzelt" modu satırları seçilebilir yapar:
// geçen ay alınıp sonradan girilen paralar (tahsilat penceresi tarihi "bugün" ile doldurur)
// tek seferde gerçek tarihine çekilir - ödeme satırı değişmez, düzeltme ayrı kayıttır.

const MONTHS = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];
const MONTHS_SHORT = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];
const WEEKDAYS = ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"];
const FILTERS: (IncomeBucket | "all")[] = ["all", "current", "late", "advance"];
const FILTER_LABEL: Record<IncomeBucket | "all", string> = { all: "Tümü", current: "Kendi ayı", late: "Gecikmiş", advance: "Peşin" };

const money = (value: number) => `₺${value.toLocaleString("tr-TR", { maximumFractionDigits: 2 })}`;
const shortPeriod = (period: string) => `${MONTHS_SHORT[Number(period.slice(5, 7)) - 1]} ${period.slice(0, 4)}`;
const shortDate = (iso: string) => `${Number(iso.slice(8, 10))} ${MONTHS_SHORT[Number(iso.slice(5, 7)) - 1]}`;

function groupHeading(key: string, scope: "month" | "year") {
  const [year, month, day] = key.split("-").map(Number);
  if (scope === "year") return { title: `${MONTHS[month - 1]} ${year}`, sub: null };
  return { title: `${day} ${MONTHS[month - 1]}`, sub: WEEKDAYS[(new Date(year, month - 1, day).getDay() + 6) % 7] };
}

export function IncomeLedger({ entries, scope, loading, search }: { entries: IncomeEntry[]; scope: "month" | "year"; loading: boolean; search: string }) {
  const [filter, setFilter] = useState<IncomeBucket | "all">("all");
  // Açık/kapalı yalnızca kullanıcı dokunduğunda kaydedilir; dokunulmamış grup varsayılanı izler.
  const [toggled, setToggled] = useState<Record<string, boolean>>({});
  const [selecting, setSelecting] = useState(false);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [correcting, setCorrecting] = useState(false);

  const counts = useMemo(() => {
    const result: Record<IncomeBucket | "all", number> = { all: entries.length, current: 0, late: 0, advance: 0 };
    for (const entry of entries) result[bucketOf(entry)] += 1;
    return result;
  }, [entries]);
  const visible = useMemo(() => filter === "all" ? entries : entries.filter((entry) => bucketOf(entry) === filter), [entries, filter]);
  const groups = useMemo(() => {
    const map = new Map<string, IncomeEntry[]>();
    for (const entry of visible) {
      const key = scope === "year" ? entry.date.slice(0, 7) : entry.date;
      map.set(key, [...(map.get(key) ?? []), entry]);
    }
    return Array.from(map.entries());
  }, [visible, scope]);

  // Varsayılan: en yeni grup açık, diğerleri kapalı. Arama ya da filtre varken aranan satır
  // kapalı bir grubun içinde kaybolmasın diye hepsi açık başlar.
  const defaultOpen = (index: number) => search.trim() !== "" || filter !== "all" || groups.length <= 2 || index === 0;
  const isOpen = (key: string, index: number) => toggled[key] ?? defaultOpen(index);
  const allOpen = groups.every(([key], index) => isOpen(key, index));
  const setAll = (open: boolean) => setToggled(Object.fromEntries(groups.map(([key]) => [key, open])));

  const selectedEntries = entries.filter((entry) => selected.has(entry.id));
  const toggleSelect = (ids: string[], on: boolean) => setSelected((current) => {
    const next = new Set(current);
    for (const id of ids) {
      if (on) next.add(id);
      else next.delete(id);
    }
    return next;
  });
  const stopSelecting = () => { setSelecting(false); setSelected(new Set()); };

  if (loading) return <p className="text-meta px-4 py-6 text-center">Yükleniyor…</p>;
  if (!entries.length) return <p className="text-meta px-4 py-6 text-center">Bu dönemde tahsilat yok. Aidat tahsilatı Aidatlar ekranından yapılır.</p>;

  return (
    <div>
      <div className="flex flex-wrap items-center gap-2 border-b border-[var(--line)] px-4 py-2.5">
        <div className="flex flex-wrap gap-1.5" role="group" aria-label="Gelir türüne göre süz">
          {FILTERS.map((value) => counts[value] > 0 && (
            <button
              key={value}
              type="button"
              onClick={() => setFilter(value)}
              aria-pressed={filter === value}
              className={`pressable inline-flex min-h-8 items-center gap-1.5 rounded-full border px-3 text-xs font-bold ${filter === value ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "border-[var(--line)] text-[var(--muted)] hover:bg-[var(--surface-muted)]"}`}
            >
              {value !== "all" && <span className="h-2 w-2 rounded-full" style={{ background: BUCKET_COLOR[value] }} aria-hidden="true" />}
              {FILTER_LABEL[value]}
              <span className="tabular-nums opacity-70">{counts[value]}</span>
            </button>
          ))}
        </div>
        <div className="ml-auto flex items-center gap-1.5">
          {groups.length > 1 && (
            <button type="button" onClick={() => setAll(!allOpen)} className="btn btn-quiet !min-h-8 !px-2.5 text-xs">
              <Icon name="chevron" className={`h-3.5 w-3.5 transition-transform ${allOpen ? "-rotate-90" : "rotate-90"}`} />{allOpen ? "Tümünü kapat" : "Tümünü aç"}
            </button>
          )}
          <button
            type="button"
            onClick={() => selecting ? stopSelecting() : setSelecting(true)}
            aria-pressed={selecting}
            className={`btn !min-h-8 !px-2.5 text-xs ${selecting ? "btn-primary" : "btn-quiet"}`}
            title="Yanlış tarihle girilmiş ödemeleri seçip gerçek tarihine taşı"
          >
            <Icon name="calendar" className="h-3.5 w-3.5" />{selecting ? "Seçimi bitir" : "Tarih düzelt"}
          </button>
        </div>
      </div>

      {selecting && (
        <p className="text-meta border-b border-[var(--line)] bg-[var(--warning-soft)] px-4 py-2 text-[.75rem]">
          Tarihini düzelteceğin ödemeleri işaretle. Bir günün başlığındaki kutu o günün tamamını seçer. Ödeme kaydı silinmez; yeni tarih ayrı bir düzeltme olarak saklanır.
        </p>
      )}

      {groups.length === 0 && <p className="text-meta px-4 py-6 text-center">Bu süzgece uyan tahsilat yok.</p>}

      {groups.map(([key, rows], index) => {
        const open = isOpen(key, index);
        const heading = groupHeading(key, scope);
        const total = rows.reduce((sum, row) => sum + row.amount, 0);
        const late = rows.filter((row) => bucketOf(row) === "late").length;
        const advance = rows.filter((row) => bucketOf(row) === "advance").length;
        const ids = rows.map((row) => row.id);
        const picked = ids.filter((id) => selected.has(id)).length;
        return (
          <section key={key} className="border-b border-[var(--line)] last:border-b-0">
            <div className={`flex items-center gap-2 px-4 ${open ? "bg-[var(--surface-muted)]" : "hover:bg-[var(--surface-muted)]"}`}>
              {selecting && (
                <input
                  type="checkbox"
                  aria-label={`${heading.title} tarihli ödemelerin tümünü seç`}
                  checked={picked === ids.length}
                  ref={(element) => { if (element) element.indeterminate = picked > 0 && picked < ids.length; }}
                  onChange={(event) => toggleSelect(ids, event.target.checked)}
                  className="h-4 w-4 shrink-0 accent-[var(--brand)]"
                />
              )}
              <button
                type="button"
                onClick={() => setToggled((current) => ({ ...current, [key]: !open }))}
                aria-expanded={open}
                className="flex min-h-12 min-w-0 flex-1 items-center gap-3 py-2 text-left"
              >
                <Icon name="chevron" className={`h-4 w-4 shrink-0 text-[var(--muted)] transition-transform ${open ? "rotate-90" : ""}`} />
                <span className="min-w-0 flex-1">
                  <span className="flex flex-wrap items-baseline gap-x-2">
                    <strong className="text-sm">{heading.title}</strong>
                    {heading.sub && <span className="text-meta text-xs">{heading.sub}</span>}
                  </span>
                  <span className="mt-0.5 flex flex-wrap items-center gap-x-2.5 gap-y-0.5 text-[.72rem] text-[var(--muted)]">
                    <span className="tabular-nums">{rows.length} ödeme</span>
                    {late > 0 && <BucketTag bucket="late" count={late} />}
                    {advance > 0 && <BucketTag bucket="advance" count={advance} />}
                  </span>
                </span>
                <strong className="shrink-0 text-sm tabular-nums text-[var(--success-strong)]">{money(total)}</strong>
              </button>
            </div>

            {open && (
              <ul className="divide-y divide-[var(--line)]">
                {rows.map((entry) => <LedgerLine key={entry.id} entry={entry} scope={scope} selecting={selecting} checked={selected.has(entry.id)} onCheck={(on) => toggleSelect([entry.id], on)} />)}
              </ul>
            )}
          </section>
        );
      })}

      {selecting && selected.size > 0 && (
        <div className="sticky bottom-3 z-10 mx-3 mb-3 mt-2 flex flex-wrap items-center gap-3 rounded-2xl bg-[var(--foreground)] px-4 py-2.5 text-sm text-[var(--surface)] shadow-[var(--shadow-card)]">
          <span><strong className="tabular-nums">{selected.size}</strong> ödeme · <strong className="tabular-nums">{money(selectedEntries.reduce((sum, entry) => sum + entry.amount, 0))}</strong></span>
          <button type="button" onClick={() => setSelected(new Set())} className="text-xs font-bold underline-offset-2 opacity-80 hover:underline">Temizle</button>
          <button type="button" onClick={() => setCorrecting(true)} className="btn btn-primary ml-auto !min-h-9">
            <Icon name="calendar" className="h-4 w-4" />Tarihi düzelt
          </button>
        </div>
      )}

      {correcting && (
        <DateCorrectionDialog
          entries={selectedEntries}
          onClose={() => setCorrecting(false)}
          onDone={() => { setCorrecting(false); stopSelecting(); }}
        />
      )}
    </div>
  );
}

function BucketTag({ bucket, count }: { bucket: IncomeBucket; count: number }) {
  return (
    <span className="inline-flex items-center gap-1 font-semibold" style={{ color: BUCKET_COLOR[bucket] }}>
      <span className="h-1.5 w-1.5 rounded-full" style={{ background: BUCKET_COLOR[bucket] }} aria-hidden="true" />
      <span className="tabular-nums">{count}</span> {BUCKET_LABEL[bucket].split(" ")[0].toLocaleLowerCase("tr-TR")}
    </span>
  );
}

// Tek satır: öğrenci + enstrüman solda, aidat dönemi ortada (kendi ayı dışındaysa renkli
// rozetle), tutar sağda. Masaüstünde sütunlar hizalı durur, telefonda iki satıra iner.
function LedgerLine({ entry, scope, selecting, checked, onCheck }: { entry: IncomeEntry; scope: "month" | "year"; selecting: boolean; checked: boolean; onCheck: (on: boolean) => void }) {
  const bucket = bucketOf(entry);
  const body = (
    <>
      {selecting && <input type="checkbox" checked={checked} onChange={(event) => onCheck(event.target.checked)} aria-label={`${entry.studentName} ödemesini seç`} className="h-4 w-4 shrink-0 accent-[var(--brand)]" />}
      <span className="min-w-0 flex-1 sm:grid sm:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)_minmax(0,.9fr)] sm:items-center sm:gap-4">
        <span className="block min-w-0">
          <strong className="block truncate text-sm">{entry.studentName}</strong>
          <span className="text-meta block truncate text-[.72rem]">{entry.instrumentName} · {COURSE_KIND_LABEL[entry.courseKind]}{scope === "year" ? ` · ${shortDate(entry.date)}` : ""}</span>
        </span>
        <span className="mt-1 flex flex-wrap items-center gap-1.5 sm:mt-0">
          {bucket === "current"
            ? <span className="text-xs text-[var(--muted)]">{shortPeriod(entry.period)} aidatı</span>
            : (
              <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[.7rem] font-bold" style={{ color: BUCKET_COLOR[bucket], background: `color-mix(in srgb, ${BUCKET_COLOR[bucket]} 14%, transparent)` }} title={`${formatPeriod(entry.period)} aidatı · ${BUCKET_LABEL[bucket]}`}>
                {shortPeriod(entry.period)} · {bucket === "late" ? "gecikmiş" : "peşin"}
              </span>
            )}
          {entry.originalDate && (
            <span className="inline-flex items-center gap-1 rounded-full bg-[var(--surface-muted)] px-2 py-0.5 text-[.7rem] font-semibold text-[var(--muted)]" title={`İlk girilen tarih: ${shortDate(entry.originalDate)} ${entry.originalDate.slice(0, 4)}`}>
              <Icon name="calendar" className="h-3 w-3" />tarih düzeltildi
            </span>
          )}
        </span>
        <span className="hidden text-xs text-[var(--muted)] sm:block">{methodLabel(entry.method)}{entry.prepayPlanMonths ? ` · ${entry.prepayPlanMonths} ay peşin` : ""}</span>
      </span>
      <strong className="shrink-0 text-sm tabular-nums">{money(entry.amount)}</strong>
    </>
  );
  return selecting
    ? <li><label className={`flex cursor-pointer items-center gap-3 px-4 py-2 pl-10 ${checked ? "bg-[var(--brand-soft)]" : "hover:bg-[var(--surface-muted)]"}`}>{body}</label></li>
    : <li className="flex items-center gap-3 px-4 py-2 pl-11">{body}</li>;
}

function lastDayOf(period: string) {
  const [year, month] = period.split("-").map(Number);
  const day = new Date(year, month, 0).getDate();
  return `${period}-${String(day).padStart(2, "0")}`;
}

function DateCorrectionDialog({ entries, onClose, onDone }: { entries: IncomeEntry[]; onClose: () => void; onDone: () => void }) {
  const correct = useCorrectPaymentDates();
  const today = todayInput();
  // Seçilenlerin hepsi aynı geçmiş ayın aidatıysa o ayın son günü iyi bir başlangıç: para en geç
  // o ay içinde alınmış demektir. Kullanıcı yine de gerçek günü seçebilir.
  const periods = Array.from(new Set(entries.map((entry) => entry.period)));
  const suggested = periods.length === 1 && lastDayOf(periods[0]) < today && entries.every((entry) => bucketOf(entry) === "late") ? lastDayOf(periods[0]) : "";
  const [date, setDate] = useState(suggested);
  const [reason, setReason] = useState(periods.length === 1 && suggested ? `${formatPeriod(periods[0])} içinde alındı, sonradan sisteme girildi` : "");
  const [error, setError] = useState<string | null>(null);
  const total = entries.reduce((sum, entry) => sum + entry.amount, 0);
  const currentDates = Array.from(new Set(entries.map((entry) => entry.date))).sort();
  const hasPrepay = entries.some((entry) => entry.prepayPlanMonths);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await correct.mutateAsync({ paymentIds: entries.map((entry) => entry.id), paymentDate: date, reason: reason.trim() });
      onDone();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Tarih düzeltilemedi.");
    }
  }

  return (
    <Modal open title="Ödeme tarihini düzelt" description={`${entries.length} ödeme · ${money(total)}`} onClose={onClose} size="sm">
      <form onSubmit={submit} className="space-y-3">
        <div className="max-h-40 overflow-auto rounded-xl border border-[var(--line)]">
          <ul className="divide-y divide-[var(--line)] text-[.8125rem]">
            {entries.map((entry) => (
              <li key={entry.id} className="flex items-center justify-between gap-2 px-3 py-1.5">
                <span className="min-w-0 truncate"><strong>{entry.studentName}</strong> <span className="text-meta">· {shortPeriod(entry.period)}</span></span>
                <span className="shrink-0 tabular-nums text-[var(--muted)]">{shortDate(entry.date)} · {money(entry.amount)}</span>
              </li>
            ))}
          </ul>
        </div>
        <p className="text-meta text-[.75rem]">Şu anki tarih{currentDates.length > 1 ? "ler" : ""}: {currentDates.map(shortDate).join(", ")}. Gelir, ödemenin yeni tarihinin ayına yazılır.</p>
        <label className="form-label">Gerçek ödeme tarihi
          <input type="date" value={date} max={today} onChange={(event) => setDate(event.target.value)} required className="field" />
        </label>
        <label className="form-label">Düzeltme nedeni
          <textarea value={reason} onChange={(event) => setReason(event.target.value)} required maxLength={500} rows={2} className="field resize-y" placeholder="Ör. Eylül'de nakit alındı, 2 Ekim'de toplu girildi" />
        </label>
        {hasPrepay && <p className="rounded-xl bg-[var(--warning-soft)] px-3 py-2 text-[.75rem] text-[var(--warning-strong)]">Seçimde peşin ödeme var: aynı peşin ödemenin diğer aylarının tarihi de birlikte taşınır.</p>}
        {error && <FormMessage tone="error">{error}</FormMessage>}
        <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-3">
          <button type="button" onClick={onClose} className="btn btn-quiet">Vazgeç</button>
          <button type="submit" disabled={correct.isPending || !date || !reason.trim()} className="btn btn-primary">
            {correct.isPending ? "Kaydediliyor…" : `${entries.length} ödemenin tarihini düzelt`}
          </button>
        </div>
      </form>
    </Modal>
  );
}
