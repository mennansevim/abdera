"use client";

import { useMemo, useState } from "react";
import { Icon } from "@/components/icons";
import { PageHeader } from "@/components/ui";
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

const FILTERS: { value: AttendanceFilter | "all"; label: string }[] = [
  { value: "all", label: "Tümü" },
  { value: "Present", label: "Geldi" },
  { value: "Absent", label: "Gelmedi" },
  { value: "Excused", label: "Mazeretli" },
  { value: "NotMarked", label: "Yoklama girilmedi" },
];

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

function attendanceRate(item: AttendanceTeacherBreakdown) {
  const marked = item.presentCount + item.absentCount + item.excusedCount;
  return marked === 0 ? null : Math.round((item.presentCount / marked) * 100);
}

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

  return (
    <div className="space-y-4">
      <PageHeader
        title="Yoklama"
        description={isAdmin
          ? "Tamamlanan dersleri geriye dönük, öğretmen kırılımıyla incele; öğrencinin derse gelip gelmediğini gör."
          : "Kendi tamamladığın dersleri geriye dönük incele; öğrencinin derse gelip gelmediğini gör."}
      />

      <section className="app-card space-y-3 p-4">
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <label className="form-label">Başlangıç
            <input type="date" value={from} max={to || today} onChange={(event) => { setFrom(event.target.value); setPage(1); }} className="field min-h-11 text-sm" />
          </label>
          <label className="form-label">Bitiş
            <input type="date" value={to} min={from} max={today} onChange={(event) => { setTo(event.target.value); setPage(1); }} className="field min-h-11 text-sm" />
          </label>
          {isAdmin && (
            <label className="form-label">Öğretmen
              <select value={teacherId} onChange={(event) => { setTeacherId(event.target.value); setPage(1); }} className="field min-h-11 text-sm">
                <option value="">Tüm öğretmenler</option>
                {teachers?.map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>)}
              </select>
            </label>
          )}
          <label className="form-label">Öğrenci
            <select value={studentId} onChange={(event) => { setStudentId(event.target.value); setPage(1); }} className="field min-h-11 text-sm">
              <option value="">Tüm öğrenciler</option>
              {students?.map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}
            </select>
          </label>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <span className="text-micro text-[var(--muted)]">Hızlı aralık</span>
          <button type="button" onClick={() => applyRange(shiftDays(-6), today)} className="pressable min-h-11 rounded-full border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--muted)] hover:border-[#e0c39d]">Son 7 gün</button>
          <button type="button" onClick={() => applyRange(shiftDays(-29), today)} className="pressable min-h-11 rounded-full border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--muted)] hover:border-[#e0c39d]">Son 30 gün</button>
          <button type="button" onClick={() => applyRange(startOfMonth(), today)} className="pressable min-h-11 rounded-full border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--muted)] hover:border-[#e0c39d]">Bu ay</button>
          <button type="button" onClick={() => applyRange(shiftDays(-89), today)} className="pressable min-h-11 rounded-full border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--muted)] hover:border-[#e0c39d]">Son 3 ay</button>
        </div>

        <div className="flex flex-wrap gap-2">
          {FILTERS.map((filter) => (
            <button
              key={filter.value}
              type="button"
              onClick={() => { setStatus(filter.value); setPage(1); }}
              className={`pressable min-h-11 rounded-full px-4 text-xs font-bold ${status === filter.value ? "bg-[var(--brand)] text-white" : "border border-[var(--line)] bg-white text-[var(--muted)] hover:border-[#e0c39d]"}`}
            >
              {filter.label}
            </button>
          ))}
        </div>

        {!rangeIsValid && <p role="alert" className="text-xs font-medium text-[var(--danger-strong)]">Bitiş tarihi başlangıçtan önce olamaz.</p>}
      </section>

      {isError && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">Yoklama geçmişi yüklenemedi.</p>}

      {data && (
        <section className="grid grid-cols-2 gap-3 xl:grid-cols-5">
          <SummaryTile label="İşlenen ders" value={data.totalLessonCount} />
          <SummaryTile label="Geldi" value={data.presentCount} tone="success" />
          <SummaryTile label="Gelmedi" value={data.absentCount} tone="danger" />
          <SummaryTile label="Mazeretli" value={data.excusedCount} tone="warning" />
          <SummaryTile label="Yoklama girilmedi" value={data.notMarkedCount} />
        </section>
      )}

      {data && data.teachers.length > 0 && (
        <section className="space-y-2">
          <h2 className="text-title">Öğretmen kırılımı</h2>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {data.teachers.map((teacher) => {
              const rate = attendanceRate(teacher);
              const selected = teacherId === teacher.teacherId;
              return (
                <button
                  key={teacher.teacherId}
                  type="button"
                  // Admin bir karta basınca liste o öğretmene daralır; ikinci basış filtreyi
                  // kaldırır. Öğretmen oturumunda zaten tek kart var, filtre değiştirmez.
                  onClick={() => { if (!isAdmin) return; setTeacherId(selected ? "" : teacher.teacherId); setPage(1); }}
                  aria-pressed={isAdmin ? selected : undefined}
                  disabled={!isAdmin}
                  className={`app-card p-4 text-left ${isAdmin ? "pressable hover:border-[#e0c39d]" : "cursor-default"} ${selected ? "border-[color:var(--brand)]" : ""}`}
                >
                  <p className="truncate text-sm font-bold">{teacher.teacherName}</p>
                  <p className="text-meta mt-0.5">{teacher.lessonCount} ders{rate !== null && <> · katılım %{rate}</>}</p>
                  <div className="mt-3 flex flex-wrap gap-1.5">
                    <Pill tone="success" label="Geldi" value={teacher.presentCount} />
                    <Pill tone="danger" label="Gelmedi" value={teacher.absentCount} />
                    <Pill tone="warning" label="Mazeretli" value={teacher.excusedCount} />
                    {teacher.notMarkedCount > 0 && <Pill tone="muted" label="Girilmedi" value={teacher.notMarkedCount} />}
                  </div>
                </button>
              );
            })}
          </div>
        </section>
      )}

      <section className="space-y-3">
        <h2 className="text-title">Ders dökümü</h2>

        {isLoading && <div className="space-y-2">{Array.from({ length: 5 }, (_, index) => <div key={index} className="skeleton h-14 rounded-xl" />)}</div>}

        {!isLoading && items?.length === 0 && (
          <div className="app-card grid min-h-40 place-items-center border-dashed p-8 text-center">
            <div>
              <span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="check" className="h-6 w-6" /></span>
              <p className="mt-4 text-sm font-bold">Bu aralıkta işlenmiş ders yok</p>
              <p className="text-meta mt-1">Tarih aralığını genişletmeyi veya filtreleri sıfırlamayı dene.</p>
            </div>
          </div>
        )}

        {days.map(([day, lessons]) => (
          <div key={day} className="app-card overflow-hidden">
            <div className="flex items-baseline justify-between gap-3 border-b border-[var(--line)] bg-[var(--surface-muted)] px-4 py-2.5">
              <h3 className="text-sm font-bold">{dayFormatter.format(new Date(`${day}T00:00:00`))}</h3>
              <span className="text-meta">{lessons.length} ders</span>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[46rem] text-sm">
                <thead>
                  <tr className="text-micro border-b border-[var(--line)] text-left">
                    <th className="px-4 py-2.5">Saat</th>
                    <th className="px-4 py-2.5">Öğrenci</th>
                    {isAdmin && <th className="px-4 py-2.5">Öğretmen</th>}
                    <th className="px-4 py-2.5">Ders</th>
                    <th className="px-4 py-2.5">Katılım</th>
                    <th className="px-4 py-2.5">Not</th>
                  </tr>
                </thead>
                <tbody>
                  {lessons.map((lesson) => (
                    <tr key={lesson.lessonId} className="border-b border-[var(--line)] last:border-0">
                      <td className="px-4 py-3 font-semibold whitespace-nowrap">{timeFormatter.format(new Date(lesson.startAt))}</td>
                      <td className="px-4 py-3 font-semibold">{lesson.studentName}</td>
                      {isAdmin && <td className="text-meta px-4 py-3">{lesson.teacherName}</td>}
                      <td className="text-meta px-4 py-3">{lesson.instrumentName}{lesson.lessonStatus === "Makeup" && <span className="ml-1.5 rounded-full bg-[var(--brand-soft)] px-2 py-0.5 text-[.65rem] font-bold text-[var(--brand-strong)]">telafi</span>}</td>
                      <td className="px-4 py-3">
                        {lesson.attendanceStatus
                          ? <span className={`inline-block rounded-full px-2.5 py-1 text-xs font-bold ${STATUS_CLASSES[lesson.attendanceStatus]}`}>{STATUS_LABELS[lesson.attendanceStatus]}</span>
                          : <span className="inline-block rounded-full bg-[var(--surface-muted)] px-2.5 py-1 text-xs font-bold text-[var(--muted)]">Yoklama girilmedi</span>}
                      </td>
                      <td className="text-meta max-w-xs px-4 py-3">{lesson.note ?? "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ))}

        {data && data.lessons.totalCount > 0 && (
          <div className="flex items-center justify-between text-sm">
            <span className="text-meta">Toplam {data.lessons.totalCount} ders · sayfa {data.lessons.page} / {totalPages}</span>
            <div className="flex gap-2">
              <button type="button" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={page <= 1} className="pressable min-h-11 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold disabled:opacity-50">Önceki</button>
              <button type="button" onClick={() => setPage((current) => Math.min(totalPages, current + 1))} disabled={page >= totalPages} className="pressable min-h-11 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold disabled:opacity-50">Sonraki</button>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}

function SummaryTile({ label, value, tone }: { label: string; value: number; tone?: "success" | "danger" | "warning" }) {
  const toneClass = tone === "success" ? "text-[var(--success-strong)]"
    : tone === "danger" ? "text-[var(--danger-strong)]"
    : tone === "warning" ? "text-[var(--warning-strong)]"
    : "";
  return (
    <div className="app-card p-4">
      <p className={`text-2xl font-bold ${toneClass}`}>{value}</p>
      <p className="text-meta mt-0.5">{label}</p>
    </div>
  );
}

function Pill({ tone, label, value }: { tone: "success" | "danger" | "warning" | "muted"; label: string; value: number }) {
  const toneClass = tone === "success" ? "bg-[var(--success-soft)] text-[var(--success-strong)]"
    : tone === "danger" ? "bg-[var(--danger-soft)] text-[var(--danger-strong)]"
    : tone === "warning" ? "bg-[var(--warning-soft)] text-[var(--warning-strong)]"
    : "bg-[var(--surface-muted)] text-[var(--muted)]";
  return <span className={`rounded-full px-2 py-0.5 text-[.7rem] font-bold ${toneClass}`}>{label} {value}</span>;
}
