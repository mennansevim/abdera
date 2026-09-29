"use client";

import { useMemo, useState } from "react";
import { EmptyState, FilterField, PageHeader, Pager, Panel, Segmented, StatStrip, TONE_TEXT, type Tone } from "@/components/ui";
import {
  useAttendanceHistory,
  type AttendanceFilter,
  type AttendanceHistoryItem,
  type AttendanceStatus,
  type AttendanceTeacherBreakdown,
} from "@/lib/attendance";
import { useStudents, useTeachers } from "@/lib/people";
import { useMe } from "@/lib/use-auth";

const PAGE_SIZE = 50;

// Yoklama etiketleri "Bugün" ekranındaki (teacher-today-lessons.tsx) karşılıklarıyla birebir
// aynı - aynı kaydı iki ekranda farklı kelimelerle göstermek kafa karıştırıyor.
const STATUS_LABELS: Record<AttendanceStatus, string> = {
  Present: "Geldi",
  Absent: "Gelmedi",
  Excused: "Mazeretli",
};

const STATUS_CLASSES: Record<AttendanceStatus, string> = {
  Present: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  Absent: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  Excused: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
};

const TONE_BAR: Record<Tone, string> = {
  brand: "bg-[var(--brand)]",
  success: "bg-[var(--success-strong)]",
  danger: "bg-[var(--danger-strong)]",
  warning: "bg-[var(--warning-strong)]",
  muted: "bg-[var(--line)]",
};

function toDateInput(date: Date) {
  // `toISOString()` UTC'ye çevirir ve yerel saatle 00:00-03:00 arasında bir önceki güne
  // kayar; tarih kutuları yerel günü göstermeli, bu yüzden parçalar elle birleştiriliyor.
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

// Kutulardaki gün, kullanıcının yerel gününün başlangıcı/sonu olarak yorumlanır; sunucuya
// UTC instant gider (CLAUDE.md: veritabanında her zaman timestamptz).
function startOfDayIso(value: string) {
  return new Date(`${value}T00:00:00`).toISOString();
}

function endOfDayIso(value: string) {
  const date = new Date(`${value}T00:00:00`);
  date.setDate(date.getDate() + 1);
  return date.toISOString();
}

function shiftDays(days: number) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return toDateInput(date);
}

function startOfMonth() {
  const date = new Date();
  return toDateInput(new Date(date.getFullYear(), date.getMonth(), 1));
}

const dayFormatter = new Intl.DateTimeFormat("tr-TR", { weekday: "long", day: "numeric", month: "long", year: "numeric" });
const timeFormatter = new Intl.DateTimeFormat("tr-TR", { hour: "2-digit", minute: "2-digit" });

type Counts = Pick<AttendanceTeacherBreakdown, "presentCount" | "absentCount" | "excusedCount" | "notMarkedCount">;

function attendanceRate(item: Counts) {
  const marked = item.presentCount + item.absentCount + item.excusedCount;
  return marked === 0 ? null : Math.round((item.presentCount / marked) * 100);
}


const RANGE_PRESETS = [
  { label: "7 gün", from: () => shiftDays(-6) },
  { label: "30 gün", from: () => shiftDays(-29) },
  { label: "Bu ay", from: startOfMonth },
  { label: "3 ay", from: () => shiftDays(-89) },
];

// Geriye dönük yoklama dökümü: hangi ders işlendi, öğrenci geldi mi, hangi öğretmende.
// Yoklama GİRME işi burada değil - o, dersin öğretmeninin "Bugün" ekranındaki akışı
// (teacher-today-lessons.tsx). Bu ekran salt okunur bir geçmiş görünümüdür.
export default function AttendancePage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";

  // Ekran geriye dönük çalışır (sunucu da başlamamış dersleri elemiştir), bu yüzden tarih
  // kutuları bugünden ileriye açılmaz.
  const today = toDateInput(new Date());
  const [from, setFrom] = useState(() => shiftDays(-29));
  const [to, setTo] = useState(today);
  const [teacherId, setTeacherId] = useState("");
  const [studentId, setStudentId] = useState("");
  const [status, setStatus] = useState<AttendanceFilter | "all">("all");
  const [page, setPage] = useState(1);

  const { data: teachers } = useTeachers();
  const { data: students } = useStudents();

  const rangeIsValid = Boolean(from) && Boolean(to) && from <= to;
  const { data, isLoading, isError } = useAttendanceHistory({
    from: rangeIsValid ? startOfDayIso(from) : startOfDayIso(to || from),
    to: rangeIsValid ? endOfDayIso(to) : endOfDayIso(to || from),
    teacherId: teacherId || undefined,
    studentId: studentId || undefined,
    status: status === "all" ? undefined : status,
    page,
    pageSize: PAGE_SIZE,
  });

  const items = data?.lessons.items;
  const totalPages = data ? Math.max(1, Math.ceil(data.lessons.totalCount / data.lessons.pageSize)) : 1;
  const hasFilters = Boolean(teacherId || studentId) || status !== "all";
  const activePreset = to === today ? RANGE_PRESETS.find((preset) => preset.from() === from)?.label : undefined;
  const showBreakdown = isAdmin && Boolean(data && data.teachers.length > 0);

  // Listeyi güne göre kümeler - "liste ve tarih şeklinde" görünüm: her gün tek bir başlık
  // altında, en yeni gün en üstte (sunucu zaten StartAt'e göre azalan sıralıyor).
  const days = useMemo(() => {
    const grouped = new Map<string, AttendanceHistoryItem[]>();
    for (const item of items ?? []) {
      const key = toDateInput(new Date(item.startAt));
      const bucket = grouped.get(key);
      if (bucket) bucket.push(item);
      else grouped.set(key, [item]);
    }
    return [...grouped.entries()];
  }, [items]);

  function applyRange(nextFrom: string, nextTo: string) {
    setFrom(nextFrom);
    setTo(nextTo);
    setPage(1);
  }

  function resetFilters() {
    setTeacherId("");
    setStudentId("");
    setStatus("all");
    setPage(1);
  }

  const columnCount = isAdmin ? 6 : 5;
  const rate = data ? attendanceRate(data) : null;

  return (
    <div className="space-y-3">
      <PageHeader
        title="Yoklama"
        description={isAdmin
          ? "Tamamlanan dersleri geriye dönük, öğretmen kırılımıyla incele; öğrencinin derse gelip gelmediğini gör."
          : "Kendi tamamladığın dersleri geriye dönük incele; öğrencinin derse gelip gelmediğini gör."}
      />

      {/* Filtre çubuğu: tarih aralığı + hızlı aralık + kişi seçimleri tek satırda. */}
      <section className="app-card p-3">
        <div className="flex flex-wrap items-end gap-x-3 gap-y-2">
          <div className="min-w-0">
            <span className="text-micro text-[var(--muted)]">Tarih aralığı</span>
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <div className="flex items-center gap-1.5">
                <input type="date" aria-label="Başlangıç" value={from} max={to || today} onChange={(event) => { setFrom(event.target.value); setPage(1); }} className="field w-[9.5rem] text-sm" />
                <span className="text-[var(--muted)]">–</span>
                <input type="date" aria-label="Bitiş" value={to} min={from} max={today} onChange={(event) => { setTo(event.target.value); setPage(1); }} className="field w-[9.5rem] text-sm" />
              </div>
              <Segmented
                label="Hızlı aralık"
                options={RANGE_PRESETS.map((preset) => ({ value: preset.label, label: preset.label }))}
                value={activePreset}
                onChange={(label) => { const preset = RANGE_PRESETS.find((item) => item.label === label); if (preset) applyRange(preset.from(), today); }}
              />
            </div>
          </div>

          {isAdmin && (
            <FilterField label="Öğretmen" className="min-w-[11rem] flex-1">
              <select value={teacherId} onChange={(event) => { setTeacherId(event.target.value); setPage(1); }} className="field text-sm">
                <option value="">Tüm öğretmenler</option>
                {teachers?.map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>)}
              </select>
            </FilterField>
          )}
          <FilterField label="Öğrenci" className="min-w-[11rem] flex-1">
            <select value={studentId} onChange={(event) => { setStudentId(event.target.value); setPage(1); }} className="field text-sm">
              <option value="">Tüm öğrenciler</option>
              {students?.map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}
            </select>
          </FilterField>

          {hasFilters && (
            <button type="button" onClick={resetFilters} className="btn px-3 text-xs text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">
              Filtreleri temizle
            </button>
          )}
        </div>

        {!rangeIsValid && <p role="alert" className="mt-2 text-xs font-medium text-[var(--danger-strong)]">Bitiş tarihi başlangıçtan önce olamaz.</p>}
      </section>

      {isError && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">Yoklama geçmişi yüklenemedi.</p>}

      {/* Özet şeridi: her sayaç tıklanınca listeyi o duruma daraltır; alttaki çubuk dağılımı gösterir. */}
      {data && (
        <StatStrip
          label="Katılım durumuna göre filtrele"
          items={([
            ["all", "İşlenen ders", data.totalLessonCount, undefined, rate !== null ? `katılım %${rate}` : undefined],
            ["Present", STATUS_LABELS.Present, data.presentCount, "success", undefined],
            ["Absent", STATUS_LABELS.Absent, data.absentCount, "danger", undefined],
            ["Excused", STATUS_LABELS.Excused, data.excusedCount, "warning", undefined],
            ["NotMarked", "Yoklama girilmedi", data.notMarkedCount, "muted", undefined],
          ] as const).map(([value, label, count, tone, hint]) => ({
            key: value, label, value: count, tone, hint,
            active: status === value,
            onClick: () => { setStatus(value); setPage(1); },
          }))}
          footer={<DistributionBar counts={data} className="h-1.5" />}
        />
      )}

      <div className={showBreakdown ? "grid items-start gap-3 xl:grid-cols-[minmax(0,1fr)_20rem]" : ""}>
        {showBreakdown && data && (
          <Panel flush title="Öğretmen kırılımı" meta={`${data.teachers.length} öğretmen`} className="xl:sticky xl:top-4 xl:order-2">
            <ul className="grid divide-[var(--line)] sm:grid-cols-2 xl:grid-cols-1 xl:divide-y">
              {data.teachers.map((teacher) => {
                const teacherRate = attendanceRate(teacher);
                const selected = teacherId === teacher.teacherId;
                return (
                  <li key={teacher.teacherId}>
                    <button
                      type="button"
                      // Bir satıra basınca liste o öğretmene daralır; ikinci basış filtreyi kaldırır.
                      onClick={() => { setTeacherId(selected ? "" : teacher.teacherId); setPage(1); }}
                      aria-pressed={selected}
                      className={`pressable block w-full px-4 py-2.5 text-left hover:bg-[var(--surface-muted)] ${selected ? "bg-[var(--brand-soft)]" : ""}`}
                    >
                      <div className="flex items-baseline justify-between gap-2">
                        <span className={`truncate text-sm font-bold ${selected ? "text-[var(--brand-strong)]" : ""}`}>{teacher.teacherName}</span>
                        <span className="text-meta shrink-0 whitespace-nowrap">
                          {teacher.lessonCount} ders{teacherRate !== null && <> · <span className="font-bold text-[var(--success-strong)]">%{teacherRate}</span></>}
                        </span>
                      </div>
                      <DistributionBar counts={teacher} className="mt-1.5 h-1.5 rounded-full" />
                      <div className="mt-1 flex flex-wrap gap-x-2.5 text-[.7rem] font-bold">
                        {teacher.presentCount > 0 && <span className={TONE_TEXT.success}>{teacher.presentCount} geldi</span>}
                        {teacher.absentCount > 0 && <span className={TONE_TEXT.danger}>{teacher.absentCount} gelmedi</span>}
                        {teacher.excusedCount > 0 && <span className={TONE_TEXT.warning}>{teacher.excusedCount} mazeretli</span>}
                        {teacher.notMarkedCount > 0 && <span className={TONE_TEXT.muted}>{teacher.notMarkedCount} girilmedi</span>}
                      </div>
                    </button>
                  </li>
                );
              })}
            </ul>
          </Panel>
        )}

        <Panel
          flush
          title="Ders dökümü"
          meta={data ? `${data.lessons.totalCount} ders` : undefined}
          footer={data && totalPages > 1 ? <Pager page={data.lessons.page} totalPages={totalPages} onChange={setPage} /> : undefined}
        >
          {isLoading && <div className="space-y-2 p-4">{Array.from({ length: 6 }, (_, index) => <div key={index} className="skeleton h-9 rounded-lg" />)}</div>}

          {!isLoading && items?.length === 0 && (
            <EmptyState title="Bu aralıkta işlenmiş ders yok" description="Tarih aralığını genişletmeyi veya filtreleri sıfırlamayı dene." />
          )}

          {/* Dar ekranda tablo yatay kayıyor ve adlar kırılıyordu; her ders tek satırlık bir öğe. */}
          {days.length > 0 && (
            <div className="md:hidden">
              {days.map(([day, lessons]) => (
                <div key={day}>
                  <h3 className="bg-[var(--surface-muted)] px-4 py-1.5 text-xs font-bold">
                    {dayFormatter.format(new Date(`${day}T00:00:00`))}
                    <span className="text-meta ml-2 font-medium">{lessons.length} ders</span>
                  </h3>
                  <ul className="divide-y divide-[var(--line)]">
                    {lessons.map((lesson) => (
                      <li key={lesson.lessonId} className="flex items-start gap-3 px-4 py-2">
                        <span className="w-11 shrink-0 pt-0.5 text-sm font-semibold tabular-nums">{timeFormatter.format(new Date(lesson.startAt))}</span>
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-sm font-semibold">{lesson.studentName}</p>
                          <p className="text-meta truncate text-xs">
                            {isAdmin && <>{lesson.teacherName} · </>}{lesson.instrumentName}{lesson.lessonStatus === "Makeup" && " · telafi"}
                          </p>
                          {lesson.note && <p className="text-meta mt-0.5 line-clamp-2 text-xs">{lesson.note}</p>}
                        </div>
                        <AttendanceBadge status={lesson.attendanceStatus} />
                      </li>
                    ))}
                  </ul>
                </div>
              ))}
            </div>
          )}

          {days.length > 0 && (
            <div className="hidden overflow-x-auto md:block">
              <table className="w-full min-w-[40rem] text-sm">
                <thead>
                  <tr className="text-micro border-b border-[var(--line)] text-left text-[var(--muted)]">
                    <th className="w-16 px-4 py-2">Saat</th>
                    <th className="px-3 py-2">Öğrenci</th>
                    {isAdmin && <th className="px-3 py-2">Öğretmen</th>}
                    <th className="px-3 py-2">Ders</th>
                    <th className="px-3 py-2">Katılım</th>
                    <th className="px-3 py-2">Not</th>
                  </tr>
                </thead>
                {days.map(([day, lessons]) => (
                  <tbody key={day}>
                    <tr className="bg-[var(--surface-muted)]">
                      <th colSpan={columnCount} scope="colgroup" className="px-4 py-1.5 text-left text-xs font-bold">
                        {dayFormatter.format(new Date(`${day}T00:00:00`))}
                        <span className="text-meta ml-2 font-medium">{lessons.length} ders</span>
                      </th>
                    </tr>
                    {lessons.map((lesson) => (
                      <tr key={lesson.lessonId} className="border-t border-[var(--line)] hover:bg-[var(--surface-muted)]/60">
                        <td className="px-4 py-2 font-semibold tabular-nums whitespace-nowrap">{timeFormatter.format(new Date(lesson.startAt))}</td>
                        <td className="px-3 py-2 font-semibold whitespace-nowrap">{lesson.studentName}</td>
                        {isAdmin && <td className="text-meta px-3 py-2 whitespace-nowrap">{lesson.teacherName}</td>}
                        <td className="text-meta px-3 py-2 whitespace-nowrap">{lesson.instrumentName}{lesson.lessonStatus === "Makeup" && <span className="ml-1.5 rounded-full bg-[var(--brand-soft)] px-2 py-0.5 text-[.65rem] font-bold text-[var(--brand-strong)]">telafi</span>}</td>
                        <td className="px-3 py-2 whitespace-nowrap"><AttendanceBadge status={lesson.attendanceStatus} /></td>
                        <td className="text-meta max-w-[16rem] truncate px-3 py-2" title={lesson.note ?? undefined}>{lesson.note ?? "—"}</td>
                      </tr>
                    ))}
                  </tbody>
                ))}
              </table>
            </div>
          )}

        </Panel>
      </div>
    </div>
  );
}

function AttendanceBadge({ status }: { status: AttendanceStatus | null }) {
  return status
    ? <span className={`inline-block shrink-0 rounded-full px-2 py-0.5 text-xs font-bold ${STATUS_CLASSES[status]}`}>{STATUS_LABELS[status]}</span>
    : <span className="inline-block shrink-0 rounded-full bg-[var(--surface-muted)] px-2 py-0.5 text-xs font-bold text-[var(--muted)]">Girilmedi</span>;
}

// Geldi/gelmedi/mazeretli/girilmedi oranlarını tek yatay çubukta gösterir.
function DistributionBar({ counts, className = "" }: { counts: Counts; className?: string }) {
  const segments: [Tone, number][] = [
    ["success", counts.presentCount],
    ["danger", counts.absentCount],
    ["warning", counts.excusedCount],
    ["muted", counts.notMarkedCount],
  ];
  const total = segments.reduce((sum, [, value]) => sum + value, 0);
  return (
    <div aria-hidden className={`flex overflow-hidden bg-[var(--surface-muted)] ${className}`}>
      {total > 0 && segments.map(([tone, value]) => value > 0 && (
        <span key={tone} className={TONE_BAR[tone]} style={{ width: `${(value / total) * 100}%` }} />
      ))}
    </div>
  );
}
