"use client";

import { Suspense, useMemo, useState, type FormEvent } from "react";
import { useSearchParams } from "next/navigation";
import { Icon } from "@/components/icons";
import { AddButton, FormActions, FormMessage, Modal, PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useStudents, type Student } from "@/lib/people";
import { buildProgressAnalysis, type PieceInsight } from "@/lib/progress-analysis";
import { useCreateProgressNote, useRevokeParentComment, useSetParentComment, useSuggestParentComment, useStudentProgress, type ProgressEntry } from "@/lib/progress";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";

type TimelineFilter = "all" | "pieces" | "homework";

const DATE_FORMATTER = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", year: "numeric" });
const SHORT_DATE_FORMATTER = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "short" });

function initials(name: string) {
  return name.split(" ").map((part) => part[0]).filter(Boolean).slice(0, 2).join("").toLocaleUpperCase("tr-TR");
}

function formatDate(value: string, short = false) {
  return (short ? SHORT_DATE_FORMATTER : DATE_FORMATTER).format(new Date(value));
}

function formatTime(value: string) {
  return new Date(value).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
}

function difficultyLabel(value: number | null) {
  if (!value) return "Belirtilmedi";
  return value <= 2 ? "Başlangıç" : value <= 3 ? "Orta" : value <= 4 ? "İleri" : "Ustalık";
}

function difficultyTone(value: number | null) {
  if (!value) return "bg-[var(--surface-muted)] text-[var(--muted)]";
  return value <= 2
    ? "bg-[var(--success-soft)] text-[var(--success-strong)]"
    : value <= 3
      ? "bg-[var(--warning-soft)] text-[var(--warning-strong)]"
      : "bg-[var(--brand-soft)] text-[var(--brand-strong)]";
}

// Öğrenci sayfasındaki "Gelişim" bağlantısı (student-detail.tsx) ?studentId= ile buraya
// yönlendirir - useSearchParams App Router'da bir Suspense sınırı ister, o yüzden asıl
// içerik ayrı bir bileşende.
export default function ProgressPage() {
  return (
    <Suspense>
      <ProgressPageContent />
    </Suspense>
  );
}

function ProgressPageContent() {
  const { data: me } = useMe();
  const canWrite = me?.role === "Teacher";
  const { data: students, isLoading: studentsLoading } = useStudents();
  const searchParams = useSearchParams();
  // Yalnızca ilk yüklemede okunur (deep-link) - sonrasında seçim tamamen kullanıcı
  // etkileşimiyle yönetilir, URL'i her seçimde güncellemeye gerek yok.
  const [selectedStudentId, setSelectedStudentId] = useState(() => searchParams.get("studentId") ?? "");
  const [showComposer, setShowComposer] = useState(false);
  const [timelineFilter, setTimelineFilter] = useState<TimelineFilter>("all");
  const [teacherFilter, setTeacherFilter] = useState("all");
  const [instrumentFilter, setInstrumentFilter] = useState("all");
  const [difficultyFilter, setDifficultyFilter] = useState("all");
  const [lastWorkedFrom, setLastWorkedFrom] = useState("");

  const activeStudentId = selectedStudentId || students?.[0]?.id || "";
  const activeStudent = students?.find((student) => student.id === activeStudentId);
  const { data: progress, isLoading: progressLoading } = useStudentProgress(activeStudentId);

  const calendarRange = useMemo(() => {
    const now = new Date();
    const from = new Date(now);
    from.setDate(from.getDate() - 70);
    const to = new Date(now);
    to.setDate(to.getDate() + 20);
    return { from: from.toISOString(), to: to.toISOString() };
  }, []);
  const { data: calendarLessons } = useCalendar(calendarRange.from, calendarRange.to);

  const studentLessons = useMemo(() => (calendarLessons ?? [])
    .filter((lesson) => lesson.studentId === activeStudentId && lesson.status !== "Rescheduled" && lesson.status !== "Cancelled")
    .sort((a, b) => b.startAt.localeCompare(a.startAt)), [activeStudentId, calendarLessons]);

  const analysis = useMemo(() => buildProgressAnalysis(progress?.entries ?? []), [progress?.entries]);
  const filteredEntries = useMemo(() => (progress?.entries ?? []).filter((entry) => {
    if (timelineFilter === "pieces") return !!entry.pieceTitle;
    if (timelineFilter === "homework") return !!entry.homework || !!entry.nextGoal;
    return true;
  }).filter((entry) =>
    (teacherFilter === "all" || entry.teacherId === teacherFilter) &&
    (instrumentFilter === "all" || entry.instrumentId === instrumentFilter) &&
    (difficultyFilter === "all" || entry.pieceDifficulty === Number(difficultyFilter)) &&
    (!lastWorkedFrom || new Date(entry.lessonStartAt) >= new Date(`${lastWorkedFrom}T00:00:00`))), [difficultyFilter, instrumentFilter, lastWorkedFrom, progress?.entries, teacherFilter, timelineFilter]);
  return (
    <div className="space-y-5">
      <PageHeader
        title="Gelişim günlüğü"
        description="Ders notları, ödevler ve eser yolculuğu tek bir akışta birikir."
        actions={canWrite && <AddButton label="Yeni gelişim notu" onClick={() => setShowComposer(true)} disabled={!activeStudentId} />}
      />

      <div className="grid gap-5 lg:grid-cols-[17rem_minmax(0,1fr)]">
        <StudentPicker
          students={students ?? []}
          selectedStudentId={activeStudentId}
          isLoading={studentsLoading}
          onSelect={(studentId) => { setSelectedStudentId(studentId); setShowComposer(false); setTimelineFilter("all"); }}
        />

        {!activeStudent ? (
          <div className="app-card grid min-h-80 place-items-center p-8 text-center">
            <div><span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="students" className="h-6 w-6" /></span><p className="mt-4 text-sm font-bold">Gelişimi izlenecek öğrenci yok</p><p className="mt-1 text-xs text-[var(--muted)]">Öğrenci eklendiğinde gelişim günlüğü burada açılır.</p></div>
          </div>
        ) : (
          <section className="min-w-0 space-y-5">
            <StudentHeader student={activeStudent} progress={progress} analysis={analysis} />
            <ProgressStats analysis={analysis} />
            <RepertoireFilters entries={progress?.entries ?? []} teacherFilter={teacherFilter} instrumentFilter={instrumentFilter} difficultyFilter={difficultyFilter} lastWorkedFrom={lastWorkedFrom} onTeacher={setTeacherFilter} onInstrument={setInstrumentFilter} onDifficulty={setDifficultyFilter} onLastWorkedFrom={setLastWorkedFrom} />
            {canWrite && (
              <Modal open={showComposer} title="Yeni gelişim notu" description="Kayıt eklendiğinde kümülatif analiz otomatik yenilenir." onClose={() => setShowComposer(false)}>
                <ProgressComposer studentId={activeStudent.id} lessons={studentLessons} onClose={() => setShowComposer(false)} />
              </Modal>
            )}

            <div className="grid items-start gap-5 xl:grid-cols-[minmax(0,1.2fr)_minmax(19rem,.8fr)]">
              <Timeline
                entries={filteredEntries}
                isLoading={progressLoading}
                filter={timelineFilter}
                onFilter={setTimelineFilter}
                studentId={activeStudent.id}
                canWrite={canWrite}
              />
              <AnalysisPanel analysis={analysis} />
            </div>
          </section>
        )}
      </div>
    </div>
  );
}

function RepertoireFilters({ entries, teacherFilter, instrumentFilter, difficultyFilter, lastWorkedFrom, onTeacher, onInstrument, onDifficulty, onLastWorkedFrom }: { entries: ProgressEntry[]; teacherFilter: string; instrumentFilter: string; difficultyFilter: string; lastWorkedFrom: string; onTeacher: (value: string) => void; onInstrument: (value: string) => void; onDifficulty: (value: string) => void; onLastWorkedFrom: (value: string) => void }) {
  const teachers = Array.from(new Map(entries.map((entry) => [entry.teacherId, entry.teacherName])).entries());
  const instruments = Array.from(new Map(entries.map((entry) => [entry.instrumentId, entry.instrumentName])).entries());
  return <section className="app-card grid gap-3 p-3 sm:grid-cols-2 xl:grid-cols-4" aria-label="Repertuvar filtreleri"><label className="text-micro text-[var(--muted)]">Öğretmen<select value={teacherFilter} onChange={(event) => onTeacher(event.target.value)} className="field mt-1 text-sm"><option value="all">Tümü</option>{teachers.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label><label className="text-micro text-[var(--muted)]">Enstrüman<select value={instrumentFilter} onChange={(event) => onInstrument(event.target.value)} className="field mt-1 text-sm"><option value="all">Tümü</option>{instruments.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label><label className="text-micro text-[var(--muted)]">Zorluk<select value={difficultyFilter} onChange={(event) => onDifficulty(event.target.value)} className="field mt-1 text-sm"><option value="all">Tümü</option>{[1, 2, 3, 4, 5].map((level) => <option key={level} value={level}>{level}/5 · {difficultyLabel(level)}</option>)}</select></label><label className="text-micro text-[var(--muted)]">Son çalışma başlangıcı<input type="date" value={lastWorkedFrom} onChange={(event) => onLastWorkedFrom(event.target.value)} className="field mt-1 text-sm" /></label></section>;
}

// Kullanıcı isteği: uzun, kaydırmalı bir liste yerine tek bir combobox - seçtikçe altındaki
// "Öğrenci gelişimi [Ad]" başlığı (StudentHeader, activeStudent üzerinden) aynı şekilde
// güncellenmeye devam eder, yalnızca seçim arayüzü değişti.
function StudentPicker({
  students,
  selectedStudentId,
  isLoading,
  onSelect,
}: {
  students: Student[];
  selectedStudentId: string;
  isLoading: boolean;
  onSelect: (studentId: string) => void;
}) {
  return (
    <aside className="app-card h-fit overflow-hidden p-4">
      <div className="flex items-center justify-between gap-2"><p className="text-micro">Öğrenciler</p><span className="rounded-full bg-[var(--surface-muted)] px-2 py-1 text-[.62rem] font-bold text-[var(--muted)]">{students.length}</span></div>
      {isLoading ? (
        <div className="mt-3 skeleton h-11 rounded-xl" />
      ) : !students.length ? (
        <p className="mt-3 text-xs text-[var(--muted)]">Henüz öğrenci yok.</p>
      ) : (
        <label className="mt-3 block">
          <span className="sr-only">Öğrenci seç</span>
          {/* Sr-only span'i saran <label>, tarayıcıda combobox'ın erişilebilir adını
              "Öğrenci seç" yerine SEÇİLİ SEÇENEĞİN metnine ("Kerem Aksoy" gibi) çeviriyordu -
              ekran okuyucu kullanıcısı alanın ne işe yaradığını hiç duymuyordu. Açık
              aria-label bu belirsizliği ortadan kaldırıyor; wrapping label görsel/yapısal
              ilişki için kalıyor. */}
          <select aria-label="Öğrenci seç" value={selectedStudentId} onChange={(event) => onSelect(event.target.value)} className="field min-h-11 w-full text-sm font-semibold">
            {students.map((student) => (
              <option key={student.id} value={student.id}>
                {student.firstName} {student.lastName}{student.status !== "Active" ? " (pasif)" : ""}
              </option>
            ))}
          </select>
        </label>
      )}
    </aside>
  );
}

function StudentHeader({ student, progress, analysis }: { student: Student; progress?: { lastEntryAt: string | null }; analysis: ReturnType<typeof buildProgressAnalysis> }) {
  const name = `${student.firstName} ${student.lastName}`;
  return <div className="app-card flex flex-wrap items-center gap-4 p-4 sm:p-5">
    <span className="grid h-14 w-14 shrink-0 place-items-center rounded-2xl bg-[linear-gradient(145deg,#ea8a4c,#a84e1f)] font-serif text-lg font-bold italic text-white shadow-[0_8px_18px_rgba(168,78,31,.2)]">{initials(name)}</span>
    <div className="min-w-0 flex-1"><p className="text-micro text-[var(--brand-strong)]">ÖĞRENCİ GELİŞİMİ</p><h2 className="mt-1 truncate font-serif text-2xl font-bold italic">{name}</h2><p className="mt-1 text-xs text-[var(--muted)]">{progress?.lastEntryAt ? `Son kayıt ${formatDate(progress.lastEntryAt, true)}` : "Henüz gelişim kaydı yok"} · {analysis.pieceCount ? `${analysis.pieceCount} eser izleniyor` : "Eser kaydı bekleniyor"}</p></div>
    <div className="rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] px-3 py-2 text-right"><p className="text-[.6rem] font-bold uppercase tracking-[.08em] text-[var(--muted)]">Durum</p><p className="mt-1 text-xs font-bold text-[var(--success-strong)]">{analysis.trend === "positive" ? "İyi ilerliyor" : analysis.trend === "steady" ? "İstikrarlı" : "Yeni dönem"}</p></div>
  </div>;
}

function ProgressStats({ analysis }: { analysis: ReturnType<typeof buildProgressAnalysis> }) {
  const stats = [
    { label: "Ders kaydı", value: analysis.noteCount, suffix: "not", icon: "note" as const },
    { label: "Takip edilen eser", value: analysis.pieceCount, suffix: "eser", icon: "music" as const },
    { label: "Ort. zorluk", value: analysis.averageDifficulty ? analysis.averageDifficulty.toFixed(1) : "—", suffix: "/ 5", icon: "target" as const },
    { label: "Hedefli ders", value: analysis.goalCount, suffix: "kayıt", icon: "activity" as const },
  ];
  return <div className="grid grid-cols-2 gap-3 xl:grid-cols-4">{stats.map((stat) => <div key={stat.label} className="app-card flex items-center gap-3 p-3.5"><span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name={stat.icon} className="h-4 w-4" /></span><span className="min-w-0"><span className="block truncate text-[.66rem] font-bold text-[var(--muted)]">{stat.label}</span><span className="mt-0.5 block text-lg font-bold tabular-nums">{stat.value} <small className="text-[.65rem] font-semibold text-[var(--muted)]">{stat.suffix}</small></span></span></div>)}</div>;
}

function ProgressComposer({ studentId, lessons, onClose }: { studentId: string; lessons: CalendarLesson[]; onClose: () => void }) {
  const createNote = useCreateProgressNote(studentId);
  const [lessonId, setLessonId] = useState(lessons[0]?.id ?? "");
  const [practiced, setPracticed] = useState("");
  const [note, setNote] = useState("");
  const [homework, setHomework] = useState("");
  const [nextGoal, setNextGoal] = useState("");
  const [pieceTitle, setPieceTitle] = useState("");
  const [pieceDifficulty, setPieceDifficulty] = useState("");
  const [pieceComposer, setPieceComposer] = useState("");
  const [pieceStatus, setPieceStatus] = useState<"Learning" | "Polishing" | "PerformanceReady" | "Archived">("Learning");
  const [pieceTargetDate, setPieceTargetDate] = useState("");
  const [pieceResourceUrl, setPieceResourceUrl] = useState("");
  const [pieceResourceVisibleToGuardian, setPieceResourceVisibleToGuardian] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const activeLessonId = lessonId || lessons[0]?.id || "";

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (!activeLessonId) { setError("Notu bağlamak için bir ders seçmelisin."); return; }
    if (!note && !practiced && !homework && !nextGoal && !pieceTitle) { setError("En az bir gelişim alanı doldurmalısın."); return; }
    setError(null);
    try {
      await createNote.mutateAsync({ lessonId: activeLessonId, practiced: practiced || undefined, note: note || undefined, homework: homework || undefined, nextGoal: nextGoal || undefined, pieceTitle: pieceTitle || undefined, pieceDifficulty: pieceDifficulty ? Number(pieceDifficulty) : undefined, pieceComposer: pieceComposer || undefined, pieceStatus: pieceTitle ? pieceStatus : undefined, pieceTargetDate: pieceTargetDate || undefined, pieceResourceUrl: pieceResourceUrl || undefined, pieceResourceVisibleToGuardian });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Gelişim notu kaydedilemedi.");
    }
  }

  return <form onSubmit={handleSubmit}>
    {!lessons.length ? <p className="text-sm text-[var(--muted)]">Bu öğrenci için yakın tarihli ders bulunamadı. Önce takvimden bir ders oluşturmalısın.</p> : <div className="grid gap-3.5">
      <label className="form-label sm:max-w-md">Ders<select value={activeLessonId} onChange={(event) => setLessonId(event.target.value)} className="field text-sm">{lessons.map((lesson) => <option key={lesson.id} value={lesson.id}>{formatDate(lesson.startAt, true)} · {formatTime(lesson.startAt)} · {lesson.instrumentName}</option>)}</select></label>
      <div className="grid gap-3 sm:grid-cols-2"><label className="form-label"><span>Ne çalışıldı?</span><input value={practiced} onChange={(event) => setPracticed(event.target.value)} className="field text-sm" placeholder="Örn. Sol majör gam, legato" /></label><label className="form-label"><span>Çalınan eser</span><input value={pieceTitle} onChange={(event) => setPieceTitle(event.target.value)} className="field text-sm" placeholder="Örn. Bach · Minuet in G" /></label></div>
      {pieceTitle && <div className="grid gap-3 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] p-3 sm:grid-cols-2 lg:grid-cols-4"><label className="form-label">Besteci<input value={pieceComposer} onChange={(event) => setPieceComposer(event.target.value)} className="field bg-white text-sm" /></label><label className="form-label">Eser durumu<select value={pieceStatus} onChange={(event) => setPieceStatus(event.target.value as typeof pieceStatus)} className="field bg-white text-sm"><option value="Learning">Çalışılıyor</option><option value="Polishing">Pekiştiriliyor</option><option value="PerformanceReady">Sahneye hazır</option><option value="Archived">Arşivlendi</option></select></label><label className="form-label">Hedef tarih<input type="date" value={pieceTargetDate} onChange={(event) => setPieceTargetDate(event.target.value)} className="field bg-white text-sm" /></label><label className="form-label">Nota / bağlantı<input type="url" value={pieceResourceUrl} onChange={(event) => setPieceResourceUrl(event.target.value)} placeholder="https://…" className="field bg-white text-sm" /></label><label className="flex items-center gap-2 text-xs font-semibold text-[var(--muted)] sm:col-span-2 lg:col-span-4"><input type="checkbox" checked={pieceResourceVisibleToGuardian} onChange={(event) => setPieceResourceVisibleToGuardian(event.target.checked)} disabled={!pieceResourceUrl} /> Bağlantıyı veli portalında göster</label></div>}
      <label className="form-label"><span>Ders notu</span><textarea value={note} onChange={(event) => setNote(event.target.value)} rows={3} className="field resize-y text-sm" placeholder="Bugünkü ilerleme, güçlü taraflar ve dikkat edilmesi gerekenler…" /></label>
      <div className="grid gap-3 sm:grid-cols-3"><label className="form-label"><span>Ödev</span><textarea value={homework} onChange={(event) => setHomework(event.target.value)} rows={2} className="field resize-y text-sm" placeholder="Bir sonraki derse kadar" /></label><label className="form-label"><span>Sonraki hedef</span><textarea value={nextGoal} onChange={(event) => setNextGoal(event.target.value)} rows={2} className="field resize-y text-sm" placeholder="Bir sonraki odak" /></label><label className="form-label"><span>Eser zorluğu <span className="font-medium">· isteğe bağlı</span></span><select value={pieceDifficulty} onChange={(event) => setPieceDifficulty(event.target.value)} className="field text-sm"><option value="">Otomatik öner</option>{[1, 2, 3, 4, 5].map((level) => <option key={level} value={level}>{level}/5 · {difficultyLabel(level)}</option>)}</select><span className="block text-[.62rem] font-medium leading-relaxed">Boş bırakırsan ders notuna göre kural tabanlı önerilir.</span></label></div>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Gelişim notunu kaydet" pending={createNote.isPending} disabled={!lessons.length} />
    </div>}
  </form>;
}

function Timeline({ entries, isLoading, filter, onFilter, studentId, canWrite }: { entries: ProgressEntry[]; isLoading: boolean; filter: TimelineFilter; onFilter: (filter: TimelineFilter) => void; studentId: string; canWrite: boolean }) {
  const filters: Array<[TimelineFilter, string]> = [["all", "Tümü"], ["pieces", "Eserler"], ["homework", "Ödev ve hedefler"]];
  return <section className="app-card overflow-hidden">
    <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--line)] p-4 sm:p-5"><div><p className="text-micro">GELİŞİM ZAMAN AKIŞI</p><h2 className="mt-1 text-title">Derslerden kalan izler</h2></div><div className="flex flex-wrap gap-1 rounded-xl bg-[var(--surface-muted)] p-1">{filters.map(([value, label]) => <button key={value} onClick={() => onFilter(value)} className={`pressable rounded-lg px-2.5 py-1.5 text-[.62rem] font-bold ${filter === value ? "bg-white text-[var(--brand-strong)] shadow-sm" : "text-[var(--muted)]"}`}>{label}</button>)}</div></div>
    {isLoading && <div className="space-y-4 p-5">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-28 rounded-xl" />)}</div>}
    {!isLoading && !entries.length && <div className="grid min-h-64 place-items-center p-8 text-center"><div><span className="mx-auto grid h-11 w-11 place-items-center rounded-2xl bg-[var(--surface-muted)] text-[var(--brand)]"><Icon name="note" className="h-5 w-5" /></span><p className="mt-4 text-sm font-bold">Henüz bu filtrede kayıt yok</p><p className="mt-1 max-w-sm text-xs text-[var(--muted)]">İlk ders notunu eklediğinde gelişim akışı ve açıklanabilir özet birlikte oluşur.</p></div></div>}
    {!isLoading && entries.length > 0 && <div className="divide-y divide-[var(--line)]">{entries.map((entry) => <TimelineEntry key={entry.id} entry={entry} studentId={studentId} canWrite={canWrite} />)}</div>}
  </section>;
}

function TimelineEntry({ entry, studentId, canWrite }: { entry: ProgressEntry; studentId: string; canWrite: boolean }) {
  return <article className="relative p-4 sm:p-5"><div className="flex gap-3 sm:gap-4"><div className="flex w-14 shrink-0 flex-col items-center text-center"><span className="grid h-9 w-9 place-items-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="music" className="h-4 w-4" /></span><span className="mt-2 text-[.62rem] font-bold leading-tight text-[var(--muted)]">{formatDate(entry.lessonStartAt, true)}</span></div><div className="min-w-0 flex-1"><div className="flex flex-wrap items-start justify-between gap-2"><div><h3 className="text-sm font-bold">{entry.instrumentName} dersi</h3><p className="mt-1 text-[.68rem] text-[var(--muted)]">{entry.teacherName} · {formatTime(entry.lessonStartAt)} · Kayıt {formatDate(entry.createdAt, true)}</p></div>{entry.pieceTitle && <span className={`rounded-full px-2 py-1 text-[.6rem] font-bold ${difficultyTone(entry.pieceDifficulty)}`}>Zorluk {entry.pieceDifficulty ?? "—"}/5</span>}</div>
      {entry.pieceTitle && <div className="mt-3 flex items-center gap-2 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] px-3 py-2"><Icon name="music" className="h-4 w-4 shrink-0 text-[var(--brand)]" /><span className="min-w-0 flex-1 truncate text-xs font-bold">{entry.pieceTitle}</span><span className="shrink-0 text-[.64rem] font-semibold text-[var(--muted)]">{difficultyLabel(entry.pieceDifficulty)}</span></div>}
      {entry.note && <p className="mt-3 whitespace-pre-line text-sm leading-relaxed text-[#5c4d3f]">{entry.note}</p>}
      {entry.practiced && <p className="mt-3 text-xs"><span className="font-bold text-[var(--brand-strong)]">Çalışıldı:</span> <span className="text-[var(--muted)]">{entry.practiced}</span></p>}
      {(entry.homework || entry.nextGoal) && <div className="mt-3 grid gap-2 sm:grid-cols-2">{entry.homework && <div className="rounded-xl bg-[var(--warning-soft)]/60 px-3 py-2"><p className="text-[.6rem] font-bold uppercase tracking-[.08em] text-[var(--warning-strong)]">Ödev</p><p className="mt-1 text-xs leading-relaxed">{entry.homework}</p></div>}{entry.nextGoal && <div className="rounded-xl bg-[var(--success-soft)]/60 px-3 py-2"><p className="text-[.6rem] font-bold uppercase tracking-[.08em] text-[var(--success-strong)]">Sonraki hedef</p><p className="mt-1 text-xs leading-relaxed">{entry.nextGoal}</p></div>}</div>}
      {canWrite && <ParentCommentEditor entry={entry} studentId={studentId} />}
      </div></div></article>;
}

function ParentCommentEditor({ entry, studentId }: { entry: ProgressEntry; studentId: string }) {
  const setComment = useSetParentComment(studentId);
  const revokeComment = useRevokeParentComment(studentId);
  const suggest = useSuggestParentComment();
  const { data: me } = useMe();
  const [open, setOpen] = useState(false);
  const [comment, setCommentValue] = useState(entry.parentComment ?? entry.note ?? "");
  const [error, setError] = useState<string | null>(null);
  // AI önerisi uygulanmadan ÖNCEKI metin. Öğretmen öneriyi beğenmezse tek tıkla dönebilsin
  // (feature_targets.md Faz 10: "AI dönüşümü geri alınabilir").
  const [textBeforeSuggestion, setTextBeforeSuggestion] = useState<string | null>(null);

  const aiAvailable = me?.aiRewriteAvailable ?? false;
  const canRewrite = aiAvailable && Boolean(entry.note?.trim());

  async function save(approve: boolean) {
    setError(null);
    try {
      await setComment.mutateAsync({ noteId: entry.id, parentComment: comment, approve });
      setOpen(false);
      setTextBeforeSuggestion(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Veli yorumu kaydedilemedi.");
    }
  }

  // Öneri doğrudan kaydedilmez, yalnızca düzenleme alanına yazılır - veliye açmak için
  // öğretmenin ayrıca "Onayla ve veliye aç" demesi gerekir.
  async function applySuggestion() {
    setError(null);
    try {
      const previous = comment;
      const result = await suggest.mutateAsync(entry.id);
      setTextBeforeSuggestion(previous);
      setCommentValue(result.suggestion);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Yapıcı metin önerisi alınamadı.");
    }
  }

  function undoSuggestion() {
    if (textBeforeSuggestion === null) return;
    setCommentValue(textBeforeSuggestion);
    setTextBeforeSuggestion(null);
  }

  const rewriteTitle = !aiAvailable
    ? "AI sağlayıcısı yapılandırılmadı"
    : !entry.note?.trim()
      ? "Dönüştürülecek bir ders notu yok"
      : "Ham notu veliye uygun yapıcı bir metne çevirir";

  return <div className="mt-3 rounded-xl border border-[var(--line)] bg-white p-3">
    <div className="flex flex-wrap items-center justify-between gap-2"><div><p className="text-[.62rem] font-bold text-[var(--brand-strong)]">Veliye sunulacak yorum</p><p className="mt-0.5 text-[.58rem] text-[var(--muted)]">{entry.parentCommentApprovedAt ? "Onaylandı ve veliye görünür" : entry.parentComment ? "Taslak — veliye görünmez" : "Henüz hazırlanmadı"}</p></div><button type="button" onClick={() => setOpen((value) => !value)} className="pressable min-h-9 rounded-lg border border-[var(--line)] px-3 text-xs font-bold">{open ? "Kapat" : entry.parentComment ? "Düzenle" : "Yorum hazırla"}</button></div>
    {open && <div className="mt-3 space-y-2">
      <textarea value={comment} onChange={(event) => { setCommentValue(event.target.value); setTextBeforeSuggestion(null); }} rows={3} className="field resize-y text-sm" placeholder="Ham notu veliye uygun, yapıcı bir yorum olarak düzenleyin." />
      {textBeforeSuggestion !== null && <p className="text-[.62rem] font-semibold text-[var(--muted)]">Bu bir AI önerisi — veliye açılmadan önce düzenleyebilir veya geri alabilirsin.</p>}
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={() => void applySuggestion()} disabled={!canRewrite || suggest.isPending} title={rewriteTitle} className={canRewrite ? "pressable min-h-9 rounded-lg border border-[var(--line)] px-3 text-xs font-bold disabled:opacity-50" : "min-h-9 rounded-lg border border-[var(--line)] px-3 text-xs font-bold text-[var(--muted)] opacity-60"}>
          {suggest.isPending ? "Dönüştürülüyor…" : aiAvailable ? "Yapıcı metne dönüştür" : "Yapıcı metne dönüştür · kullanılamıyor"}
        </button>
        {textBeforeSuggestion !== null && <button type="button" onClick={undoSuggestion} className="pressable min-h-9 rounded-lg border border-[var(--line)] px-3 text-xs font-bold">Öneriyi geri al</button>}
        <span className="flex-1" />
        <button type="button" onClick={() => void save(false)} disabled={setComment.isPending || !comment.trim()} className="pressable min-h-9 rounded-lg border border-[var(--line)] px-3 text-xs font-bold disabled:opacity-50">Taslak kaydet</button>
        <button type="button" onClick={() => void save(true)} disabled={setComment.isPending || !comment.trim()} className="pressable min-h-9 rounded-lg bg-[var(--brand)] px-3 text-xs font-bold text-white disabled:opacity-50">Onayla ve veliye aç</button>
      </div>
      {error && <p role="alert" className="text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
    </div>}
    {entry.parentCommentApprovedAt && !open && <button type="button" onClick={() => void revokeComment.mutateAsync(entry.id)} disabled={revokeComment.isPending} className="mt-2 text-[.62rem] font-bold text-[var(--danger-strong)] underline">Veli görünürlüğünü geri çek</button>}
  </div>;
}

function AnalysisPanel({ analysis }: { analysis: ReturnType<typeof buildProgressAnalysis> }) {
  return <aside className="app-card overflow-hidden">
    <div className="relative overflow-hidden bg-[#3e2d29] p-5 text-white">
      <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-[#d9662a]/25 blur-2xl" />
      <div className="relative">
        <div className="flex items-center gap-2 text-[.62rem] font-bold uppercase tracking-[.12em] text-[#f4c4a3]"><Icon name="sparkles" className="h-4 w-4" /> Gelişim özeti</div>
        <h2 className="mt-3 font-serif text-xl font-bold italic">{analysis.headline}</h2>
        <p className="mt-2 text-xs leading-relaxed text-white/75">{analysis.summary}</p>
        <p className="mt-4 text-[.62rem] text-white/45">Kaynak: öğretmenlerin girdiği ders notları ve eser bilgileri</p>
      </div>
    </div>

    <div className="space-y-5 p-5">
      <section>
        <div className="flex items-center justify-between gap-3"><p className="text-micro">Çalışılan eserler</p><span className="rounded-full bg-[var(--surface-muted)] px-2 py-1 text-[.62rem] font-bold text-[var(--muted)]">{analysis.pieceCount} eser</span></div>
        {analysis.pieces.length ? <div className="mt-3 max-h-[26rem] space-y-2 overflow-y-auto pr-1">{analysis.pieces.map((piece, index) => <PieceListItem key={piece.title} piece={piece} index={index} />)}</div> : <p className="mt-3 rounded-xl bg-[var(--surface-muted)] p-3 text-xs leading-relaxed text-[var(--muted)]">Ders notuna eser adı eklendiğinde öğrencinin repertuvarı ve önerilen zorluk seviyesi burada listelenir.</p>}
      </section>

      <section className="border-t border-[var(--line)] pt-5">
        <p className="text-micro">Önerilen odaklar</p>
        <div className="mt-3 flex flex-wrap gap-2">{analysis.focusAreas.map((area) => <span key={area} className="rounded-full bg-[var(--brand-soft)] px-2.5 py-1.5 text-[.66rem] font-bold text-[var(--brand-strong)]">{area}</span>)}</div>
      </section>

      <section className="border-t border-[var(--line)] pt-5">
        <div className="flex items-center justify-between text-xs"><span className="font-bold">Kayıt sürekliliği</span><span className="font-bold text-[var(--brand-strong)]">{analysis.practiceRate}%</span></div>
        <div className="mt-2 h-2 overflow-hidden rounded-full bg-[var(--surface-muted)]"><span className="block h-full rounded-full bg-[var(--brand)] transition-all" style={{ width: `${analysis.practiceRate}%` }} /></div>
        <p className="mt-2 text-[.66rem] leading-relaxed text-[var(--muted)]">Derslerin ne kadarında çalışılan konu veya gelişim notu bulunuyor.</p>
      </section>
    </div>
  </aside>;
}

function PieceListItem({ piece, index }: { piece: PieceInsight; index: number }) {
  return <article className="rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/55 p-3">
    <div className="flex items-start gap-3">
      <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-white text-[.66rem] font-bold text-[var(--brand)] shadow-sm">{index + 1}</span>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <h3 className="min-w-0 flex-1 text-xs font-bold leading-relaxed">{piece.title}</h3>
          <span className={`shrink-0 rounded-full px-2 py-1 text-[.6rem] font-bold ${difficultyTone(piece.averageDifficulty)}`}>{piece.averageDifficulty.toFixed(1)}/5 · {difficultyLabel(piece.averageDifficulty)}</span>
        </div>
        <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-[.62rem]">
          <span className={`font-bold ${piece.difficultySource === "assistant" ? "text-[var(--brand-strong)]" : "text-[var(--success-strong)]"}`}>{piece.difficultySource === "assistant" ? "Kural tabanlı öneri" : "Öğretmen"}</span>
          <span className="text-[var(--muted)]">{piece.difficultyReason}</span>
        </div>
        <p className="mt-1.5 text-[.62rem] text-[var(--muted)]">{piece.appearances} ders kaydı · son çalışma {formatDate(piece.latestAt, true)}</p>
      </div>
    </div>
  </article>;
}
