"use client";

// Müsaitlik: yeni bir veli geldiğinde "hangi öğretmen, hangi saatte boş" sorusunun ekranı.
// Takvimden farkı: takvim "bu hafta ne var" der, burası "her hafta boş olan saat hangisi".
// Hesap sunucuda (OpenSlots.cs - uygunluk penceresi eksi aktif ders serileri); ekran yalnızca
// gün x yarım saat ızgarasına toplar. Seçilen saat, Yeni öğrenci formunu öğretmen/enstrüman/
// gün/saat dolu olarak açar - ayrı bir kayıt akışı yok.
import { useMemo, useRef, useState, type ReactNode, type RefObject } from "react";
import Link from "next/link";
import { AdminGate, EmptyState, PageHeader } from "@/components/ui";
import { Icon } from "@/components/icons";
import { useInstruments, useTeachers } from "@/lib/people";
import { DAY_NAMES_TR, SCHOOL_DAY_END, SCHOOL_DAY_START, useOpenSlots, type OpenSlot, type TeacherOpenSlots } from "@/lib/scheduling";

const WEEK_DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
const DAY_SHORT: Record<string, string> = { Monday: "Pzt", Tuesday: "Sal", Wednesday: "Çar", Thursday: "Per", Friday: "Cum", Saturday: "Cmt", Sunday: "Paz" };
const DURATIONS = [30, 45, 60];
const ROW_MINUTES = 30;
// Okul çıkışı: hafta içi bu saatten sonrası, hafta sonu bütün gün.
const AFTER_SCHOOL_FROM = 15 * 60;

type Preference = "any" | "afterSchool" | "weekend";
const PREFERENCES: { key: Preference; label: string }[] = [
  { key: "any", label: "Fark etmez" },
  { key: "afterSchool", label: "Okul sonrası" },
  { key: "weekend", label: "Hafta sonu" },
];

const todayLocal = () => {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
};
const toMinutes = (time: string) => { const [h = "0", m = "0"] = time.split(":"); return Number(h) * 60 + Number(m); };
const hhmm = (minutes: number) => `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`;
const isWeekend = (day: string) => day === "Saturday" || day === "Sunday";
const formatDate = (value: string) =>
  new Date(`${value}T12:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long", weekday: "long" });

function fitsPreference(day: string, minutes: number, preference: Preference) {
  if (preference === "weekend") return isWeekend(day);
  if (preference === "afterSchool") return isWeekend(day) || minutes >= AFTER_SCHOOL_FROM;
  return true;
}

type CellTeacher = { teacher: TeacherOpenSlots; slots: OpenSlot[] };

type FreeRange = { day: string; start: number; end: number; first: OpenSlot };

// Sunucu 15 dakikalık adımlarla boş BAŞLANGIÇ saatlerini döner; art arda gelen başlangıçlar
// tek bir boş aralıktır: ilk başlangıçtan son başlangıç + ders süresine kadar. Panel bu
// aralıkları gösterir ("Cmt 14:45-21:00"), yalnızca seçilen yarım saati değil.
function freeRanges(slots: OpenSlot[], durationMinutes: number, preference: Preference): FreeRange[] {
  const ranges: FreeRange[] = [];
  for (const slot of slots) {
    const start = toMinutes(slot.startTime);
    if (!fitsPreference(slot.dayOfWeek, start, preference)) continue;
    const last = ranges[ranges.length - 1];
    if (last && last.day === slot.dayOfWeek && start - (last.end - durationMinutes) === 15) {
      last.end = start + durationMinutes;
    } else {
      ranges.push({ day: slot.dayOfWeek, start, end: start + durationMinutes, first: slot });
    }
  }
  return ranges;
}

const rangeMinutes = (ranges: FreeRange[]) => ranges.reduce((total, range) => total + range.end - range.start, 0);
const formatHours = (minutes: number) => {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours && rest ? `${hours} sa ${rest} dk` : hours ? `${hours} sa` : `${rest} dk`;
};

export default function AvailabilityPage() {
  return <AdminGate><AvailabilityBoard /></AdminGate>;
}

function AvailabilityBoard() {
  const [instrumentId, setInstrumentId] = useState("");
  const [durationMinutes, setDurationMinutes] = useState(45);
  const [preference, setPreference] = useState<Preference>("afterSchool");
  const [from, setFrom] = useState(todayLocal);
  const [selected, setSelected] = useState<{ day: string; row: number } | null>(null);
  const [copied, setCopied] = useState<string | null>(null);
  const panelRef = useRef<HTMLElement>(null);

  // Dar ekranda liste ızgaranın altında kalıyor; hücreye dokununca oraya kaydırılır.
  function selectCell(cell: { day: string; row: number }) {
    setSelected(cell);
    if (window.matchMedia("(max-width: 1023px)").matches) {
      requestAnimationFrame(() => panelRef.current?.scrollIntoView({ behavior: "smooth", block: "start" }));
    }
  }

  const instrumentsQuery = useInstruments();
  const teachersQuery = useTeachers();
  const openSlots = useOpenSlots({ instrumentId, durationMinutes, from });

  // Yalnızca aktif bir öğretmenin verdiği enstrümanlar: kimsenin vermediği bir enstrümanı
  // seçtirip boş ızgara göstermenin anlamı yok.
  const instruments = useMemo(() => {
    const taught = new Set((teachersQuery.data ?? []).filter((t) => t.status === "Active").flatMap((t) => t.instrumentIds));
    return (instrumentsQuery.data ?? []).filter((i) => taught.has(i.id));
  }, [instrumentsQuery.data, teachersQuery.data]);
  const instrumentName = instruments.find((i) => i.id === instrumentId)?.name;

  const teachers = useMemo(() => openSlots.data?.teachers ?? [], [openSlots.data]);

  // Pazar sütunu yalnızca bir öğretmen pazar günü açıksa görünür; okulun haftası Pzt-Cmt.
  const days = useMemo(
    () => WEEK_DAYS.filter((day) => day !== "Sunday" || teachers.some((t) => t.slots.some((s) => s.dayOfWeek === "Sunday"))),
    [teachers],
  );

  const rows = useMemo(() => {
    const starts = teachers.flatMap((t) => t.slots.map((s) => toMinutes(s.startTime)));
    const first = starts.length ? Math.floor(Math.min(...starts) / ROW_MINUTES) * ROW_MINUTES : toMinutes(SCHOOL_DAY_START);
    const last = starts.length ? Math.max(...starts) : toMinutes(SCHOOL_DAY_END) - ROW_MINUTES;
    const result: number[] = [];
    for (let row = first; row <= last; row += ROW_MINUTES) result.push(row);
    return result;
  }, [teachers]);

  const cells = useMemo(() => {
    const map = new Map<string, CellTeacher[]>();
    for (const teacher of teachers) {
      const byCell = new Map<string, OpenSlot[]>();
      for (const slot of teacher.slots) {
        const row = Math.floor(toMinutes(slot.startTime) / ROW_MINUTES) * ROW_MINUTES;
        const key = `${slot.dayOfWeek}|${row}`;
        byCell.set(key, [...(byCell.get(key) ?? []), slot]);
      }
      for (const [key, slots] of byCell) map.set(key, [...(map.get(key) ?? []), { teacher, slots }]);
    }
    return map;
  }, [teachers]);

  const preferredSlots = useMemo(
    () => teachers.flatMap((teacher) => teacher.slots
      .filter((slot) => fitsPreference(slot.dayOfWeek, toMinutes(slot.startTime), preference))
      .map((slot) => ({ teacher, slot }))),
    [teachers, preference],
  );
  const freeTeacherCount = new Set(preferredSlots.map((item) => item.teacher.teacherId)).size;
  const earliest = preferredSlots
    .filter((item) => item.slot.firstOpenDate)
    .sort((a, b) => `${a.slot.firstOpenDate} ${a.slot.startTime}`.localeCompare(`${b.slot.firstOpenDate} ${b.slot.startTime}`))[0];

  // Aynı saatte boş olanlar arasında velinin tercihine en çok boş vakti olan başta: en fazla
  // seçeneği sunan öğretmen. (Önceki "doluluk %" tüm gün açık tanımlanan pencereye göre
  // hesaplandığından okul sonrası seçeneği az olan öğretmeni boş gösteriyordu.)
  const selectedTeachers = [...(selected ? cells.get(`${selected.day}|${selected.row}`) ?? [] : [])]
    .map((entry) => ({ ...entry, ranges: freeRanges(entry.teacher.slots, durationMinutes, preference) }))
    .sort((a, b) => rangeMinutes(b.ranges) - rangeMinutes(a.ranges));

  // Veliye okunacak/gönderilecek kısa liste: her günden en erken saat, en fazla beş seçenek.
  // Veli henüz kayıtlı değil, WhatsApp onayı yok - bu yüzden gönderim değil, panoya kopya.
  async function copyForGuardian() {
    const perDay = new Map<string, number>();
    for (const { slot } of preferredSlots) {
      const minutes = toMinutes(slot.startTime);
      const current = perDay.get(slot.dayOfWeek);
      if (current === undefined || minutes < current) perDay.set(slot.dayOfWeek, minutes);
    }
    const options = days.filter((day) => perDay.has(day)).slice(0, 5).map((day) => `${DAY_NAMES_TR[day]} ${hhmm(perDay.get(day)!)}`);
    if (!options.length) { setCopied("Seçilen tercihte boş saat yok."); return; }
    const text = `${instrumentName ?? "Ders"} için uygun saatlerimiz: ${options.join(", ")}. Hangisi size uygun?`;
    try {
      await navigator.clipboard.writeText(text);
      setCopied(`Kopyalandı: “${text}”`);
    } catch {
      setCopied(text);
    }
  }

  const chipClass = (active: boolean) => `pressable min-h-11 shrink-0 rounded-xl border px-3 text-xs font-bold xl:min-h-9 ${active ? "border-[var(--brand)] bg-[var(--brand)] text-white" : "border-[var(--line)] bg-white text-[#5c4d3f] hover:border-[var(--brand)] hover:text-[var(--brand)]"}`;

  return (
    <div className="space-y-3">
      <PageHeader
        title="Müsaitlik"
        description="Yeni kayıt için öğretmenlerin her hafta boş olan saatleri."
        actions={<Link href="/dashboard/students/new" className="btn btn-quiet"><Icon name="plus" className="h-4 w-4" />Yeni öğrenci</Link>}
      />

      <section className="app-card space-y-3 p-4">
        <FilterRow label="Enstrüman">
          <button type="button" onClick={() => setInstrumentId("")} aria-pressed={!instrumentId} className={chipClass(!instrumentId)}>Tümü</button>
          {instruments.map((i) => (
            <button key={i.id} type="button" onClick={() => setInstrumentId(i.id)} aria-pressed={instrumentId === i.id} className={chipClass(instrumentId === i.id)}>{i.name}</button>
          ))}
        </FilterRow>
        <FilterRow label="Ders süresi">
          {DURATIONS.map((d) => (
            <button key={d} type="button" onClick={() => setDurationMinutes(d)} aria-pressed={durationMinutes === d} className={chipClass(durationMinutes === d)}>{d} dk</button>
          ))}
        </FilterRow>
        <FilterRow label="Veli tercihi">
          {PREFERENCES.map((p) => (
            <button key={p.key} type="button" onClick={() => setPreference(p.key)} aria-pressed={preference === p.key} className={chipClass(preference === p.key)}>{p.label}</button>
          ))}
        </FilterRow>
        <FilterRow label="Başlangıç">
          <input type="date" className="field w-auto text-sm" value={from} min={todayLocal()} onChange={(e) => { if (e.target.value) setFrom(e.target.value); }} />
        </FilterRow>
      </section>

      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        <Metric label="Boş saati olan öğretmen" value={openSlots.isLoading ? "…" : String(freeTeacherCount)} />
        <Metric label="En erken ders" value={openSlots.isLoading ? "…" : earliest ? `${formatDate(earliest.slot.firstOpenDate!)} ${earliest.slot.startTime.slice(0, 5)}` : "-"} />
        <div className="col-span-2 flex items-center sm:col-span-1">
          <button type="button" onClick={copyForGuardian} className="btn btn-quiet w-full"><Icon name="whatsapp" className="h-4 w-4" />Veliye seçenekleri kopyala</button>
        </div>
      </div>
      {copied && <p role="status" className="text-meta rounded-xl bg-[var(--success-soft)] px-3 py-2 text-[var(--success-strong)]">{copied}</p>}

      <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <section className="app-card min-w-0 p-3 sm:p-4" aria-label="Gün ve saate göre boş öğretmen sayısı">
          {openSlots.isError ? (
            <EmptyState icon="x" title="Müsaitlik yüklenemedi" description="Sayfayı yenileyip tekrar dene." />
          ) : openSlots.isLoading ? (
            <div className="skeleton h-96 rounded-xl" />
          ) : teachers.length === 0 ? (
            <EmptyState icon="calendar" title="Uygunluğu tanımlı öğretmen yok" description="Öğretmenler ekranından öğretmenlerin çalıştığı günleri tanımla." />
          ) : (
            <>
              <div className="grid gap-1" style={{ gridTemplateColumns: `2.75rem repeat(${days.length}, minmax(0, 1fr))` }}>
                <span />
                {days.map((day) => <span key={day} className="text-center text-[.75rem] font-bold text-[var(--muted)]">{DAY_SHORT[day]}</span>)}
                {rows.map((row) => (
                  <Row key={row} row={row} days={days} cells={cells} preference={preference} selected={selected} onSelect={selectCell} />
                ))}
              </div>
              <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-[.75rem] text-[var(--muted)]">
                <span className="flex items-center gap-1.5"><span className="h-3 w-3 rounded bg-[var(--success-soft)]" />1 öğretmen</span>
                <span className="flex items-center gap-1.5"><span className="h-3 w-3 rounded bg-[var(--success)]" />2+ öğretmen</span>
                <span className="flex items-center gap-1.5"><span className="h-3 w-3 rounded border border-dashed border-[var(--line)]" />tercih dışı</span>
              </div>
            </>
          )}
        </section>

        <SelectedPanel
          panelRef={panelRef}
          selected={selected}
          entries={selectedTeachers}
          preference={preference}
          instrumentId={instrumentId}
          durationMinutes={durationMinutes}
          instrumentNames={new Map((instrumentsQuery.data ?? []).map((i) => [i.id, i.name]))}
        />
      </div>

      {!!openSlots.data?.teachersWithoutAvailability.length && (
        <p className="text-meta text-[var(--muted)]">
          Uygunluğu tanımlı olmadığı için listede yok: {openSlots.data.teachersWithoutAvailability.map((t) => t.teacherName).join(", ")}.{" "}
          <Link href="/dashboard/teachers" className="font-semibold text-[var(--brand)] underline-offset-2 hover:underline">Öğretmenler ekranından tanımla</Link>
        </p>
      )}
    </div>
  );
}

function FilterRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5 sm:flex-row sm:items-center">
      <span className="text-meta w-28 shrink-0 font-semibold">{label}</span>
      <div className="flex flex-wrap gap-1.5" role="group" aria-label={label}>{children}</div>
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="app-card px-4 py-3">
      <p className="text-meta">{label}</p>
      <p className="mt-0.5 text-title font-bold">{value}</p>
    </div>
  );
}

function Row({ row, days, cells, preference, selected, onSelect }: {
  row: number;
  days: string[];
  cells: Map<string, CellTeacher[]>;
  preference: Preference;
  selected: { day: string; row: number } | null;
  onSelect: (cell: { day: string; row: number }) => void;
}) {
  return (
    <>
      <span className="flex items-center text-[.75rem] tabular-nums text-[var(--muted)]">{row % 60 === 0 ? hhmm(row) : ""}</span>
      {days.map((day) => {
        const count = cells.get(`${day}|${row}`)?.length ?? 0;
        const preferred = fitsPreference(day, row, preference);
        const isSelected = selected?.day === day && selected.row === row;
        const tone = !preferred
          ? "border border-dashed border-[var(--line)] bg-transparent text-[var(--muted)]"
          : count >= 2 ? "bg-[var(--success)] text-white"
          : count === 1 ? "bg-[var(--success-soft)] text-[var(--success-strong)]"
          : "bg-[var(--surface-muted)]/50 text-transparent";
        return (
          <button
            key={day}
            type="button"
            onClick={() => onSelect({ day, row })}
            aria-pressed={isSelected}
            aria-label={`${DAY_NAMES_TR[day]} ${hhmm(row)}: ${count ? `${count} öğretmen boş` : "boş öğretmen yok"}`}
            className={`h-7 rounded-md text-[.75rem] font-bold tabular-nums ${tone} ${isSelected ? "ring-2 ring-[var(--brand)] ring-offset-1" : ""}`}
          >
            {count || ""}
          </button>
        );
      })}
    </>
  );
}

function SelectedPanel({ panelRef, selected, entries, preference, instrumentId, durationMinutes, instrumentNames }: {
  panelRef: RefObject<HTMLElement | null>;
  selected: { day: string; row: number } | null;
  entries: (CellTeacher & { ranges: FreeRange[] })[];
  preference: Preference;
  instrumentId: string;
  durationMinutes: number;
  instrumentNames: Map<string, string>;
}) {
  if (!selected) {
    return (
      <aside ref={panelRef} className="app-card p-4">
        <EmptyState icon="clock" title="Bir saat seç" description="Izgarada bir hücreye dokun; o saatte boş olan öğretmenler ve haftanın tüm boş aralıkları burada listelenir." />
      </aside>
    );
  }

  const preferenceLabel = PREFERENCES.find((p) => p.key === preference)!.label.toLocaleLowerCase("tr-TR");

  return (
    <aside ref={panelRef} className="app-card scroll-mt-20 p-4" aria-live="polite">
      <p className="text-title font-bold">{DAY_NAMES_TR[selected.day]} {hhmm(selected.row)}–{hhmm(selected.row + ROW_MINUTES)}</p>
      <p className="text-meta">{durationMinutes} dakikalık ders için</p>
      {entries.length === 0 ? (
        <p className="text-meta mt-4 rounded-xl bg-[var(--surface-muted)] px-3 py-3">Bu saatte boş öğretmen yok. Yeşil hücrelere bak.</p>
      ) : (
        <ul className="mt-3 divide-y divide-[var(--line)]">
          {entries.map(({ teacher, slots, ranges }) => {
            const lessonInstrumentId = instrumentId || teacher.instrumentIds[0] || "";
            const enrollHref = (slot: OpenSlot, minutes: number) => {
              const params = new URLSearchParams({
                teacherId: teacher.teacherId, instrumentId: lessonInstrumentId, day: slot.dayOfWeek, time: hhmm(minutes), duration: String(durationMinutes),
              });
              if (slot.firstOpenDate) params.set("startedAt", slot.firstOpenDate);
              return `/dashboard/students/new?${params}`;
            };
            const days = WEEK_DAYS.filter((day) => ranges.some((range) => range.day === day));
            return (
              <li key={teacher.teacherId} className="py-3">
                <p className="text-sm font-bold">{teacher.teacherName}</p>
                <p className="text-meta">
                  {teacher.instrumentIds.map((id) => instrumentNames.get(id)).filter(Boolean).join(", ")} · haftada {teacher.weeklyLessonCount} ders
                </p>
                <p className="text-meta font-semibold text-[var(--success-strong)]">
                  {preference === "any" ? "Haftada" : `${preferenceLabel[0]!.toLocaleUpperCase("tr-TR")}${preferenceLabel.slice(1)}`} {formatHours(rangeMinutes(ranges))} boş
                </p>
                {teacher.upcomingTimeOff.map((off) => (
                  <p key={off.startsOn} className="text-meta mt-1 font-semibold text-[var(--warning-strong)]">
                    İzinli: {formatDate(off.startsOn)} – {formatDate(off.endsOn)}
                  </p>
                ))}

                <div className="mt-2 flex flex-wrap gap-1.5">
                  {slots.map((slot) => (
                    <Link
                      key={slot.startTime}
                      href={enrollHref(slot, toMinutes(slot.startTime))}
                      className="pressable rounded-xl border border-[var(--brand)] bg-[var(--brand-soft)] px-2.5 py-1.5 text-left"
                    >
                      <span className="block text-xs font-bold text-[var(--brand-strong)]">{slot.startTime.slice(0, 5)} · Kayıt aç</span>
                      <span className="block text-[.75rem] text-[var(--muted)]">{slot.firstOpenDate ? `İlk ders ${formatDate(slot.firstOpenDate)}` : "8 hafta içinde boş tarih yok"}</span>
                    </Link>
                  ))}
                </div>

                <p className="text-meta mt-3 font-semibold">Haftanın tüm boş aralıkları</p>
                <dl className="mt-1 space-y-1">
                  {days.map((day) => (
                    <div key={day} className={`flex items-start gap-2 rounded-lg px-1.5 py-1 ${day === selected.day ? "bg-[var(--surface-muted)]" : ""}`}>
                      <dt className="w-9 shrink-0 pt-1 text-[.75rem] font-bold text-[var(--muted)]">{DAY_SHORT[day]}</dt>
                      <dd className="flex flex-wrap gap-1">
                        {ranges.filter((range) => range.day === day).map((range) => (
                          <Link
                            key={range.start}
                            href={enrollHref(range.first, range.start)}
                            title={`${DAY_NAMES_TR[day]} ${hhmm(range.start)} başlangıçla kayıt aç; saat formda değiştirilebilir`}
                            className="pressable rounded-lg border border-[var(--line)] bg-white px-2 py-1 text-xs font-semibold tabular-nums hover:border-[var(--brand)] hover:text-[var(--brand)]"
                          >
                            {hhmm(range.start)}–{hhmm(range.end)}
                          </Link>
                        ))}
                      </dd>
                    </div>
                  ))}
                </dl>
              </li>
            );
          })}
        </ul>
      )}
    </aside>
  );
}
