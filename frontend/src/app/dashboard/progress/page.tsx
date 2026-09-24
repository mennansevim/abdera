"use client";

import { Suspense, useEffect, useMemo, useState, type FormEvent } from "react";
import { useSearchParams } from "next/navigation";
import { Icon } from "@/components/icons";
import { StudentLibrarySuggestions } from "@/components/music-library/student-suggestions";
import { FormActions, FormMessage, Modal, PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useStudents, type Student } from "@/lib/people";
import { buildProgressAnalysis, type PieceInsight, type ProgressAnalysis } from "@/lib/progress-analysis";
import { useCreateProgressNote, useRevokeParentComment, useProgressSummary, useSetParentComment, useStudentProgress, type ProgressEntry } from "@/lib/progress";
import { useCalendar, type CalendarLesson } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import { useSessionState } from "@/lib/use-session-state";

type TimelineFilter = "all" | "pieces" | "homework";

const DATE_FORMATTER = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", year: "numeric" });
const SHORT_DATE_FORMATTER = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "short" });
const ENTRY_DATE_FORMATTER = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", weekday: "short" });
// Mobilde uzun bir akışı tek seferde döşemek yerine son kayıtlarla açılır; eskiler istenince gelir.
const TIMELINE_PAGE_SIZE = 8;
const PIECE_PREVIEW_COUNT = 4;

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

// Ekran çoğunlukla öğretmenin telefonundan, ders arasında açılır. Önceki sürüm dört istatistik
// kartı, her zaman açık dört filtre ve koyu bir özet paneliyle asıl soruyu ("bu öğrenci şu an
// ne çalışıyor, son derste ne oldu?") ekranın altına itiyordu. Sıra artık o soruya göre:
// öğrenci → güncel ödev/hedef → özet (repertuvar) → ders ders akış. Geniş ekranda özet sağ
// sütuna geçer.
function ProgressPageContent() {
  const { data: me } = useMe();
  const canWrite = me?.role === "Teacher";
  const { data: students, isLoading: studentsLoading, isError: studentsError, isFetching: studentsFetching, refetch: refetchStudents } = useStudents();
  const searchParams = useSearchParams();
  // Yalnızca ilk yüklemede okunur (deep-link) - sonrasında seçim tamamen kullanıcı
  // etkileşimiyle yönetilir, URL'i her seçimde güncellemeye gerek yok.
  const [selectedStudentId, setSelectedStudentId] = useState(() => searchParams.get("studentId") ?? "");
  const [showComposer, setShowComposer] = useState(false);
  const [timelineFilter, setTimelineFilter] = useSessionState<TimelineFilter>("abdera:progress:timeline", "all");
  const [teacherFilter, setTeacherFilter] = useSessionState("abdera:progress:teacher", "all");
  const [instrumentFilter, setInstrumentFilter] = useSessionState("abdera:progress:instrument", "all");
  const [difficultyFilter, setDifficultyFilter] = useSessionState("abdera:progress:difficulty", "all");
  const [lastWorkedFrom, setLastWorkedFrom] = useSessionState("abdera:progress:worked-from", "");

  const activeStudentId = selectedStudentId || students?.[0]?.id || "";
  const activeStudent = students?.find((student) => student.id === activeStudentId);
  const { data: progress, isLoading: progressLoading, isError: progressError, isFetching: progressFetching, refetch: refetchProgress } = useStudentProgress(activeStudentId);

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

  const entries = useMemo(() => [...(progress?.entries ?? [])].sort((a, b) => b.lessonStartAt.localeCompare(a.lessonStartAt)), [progress?.entries]);
  const analysis = useMemo(() => buildProgressAnalysis(entries), [entries]);
  const timelineFilters = useMemo(() => ({ teacherFilter, instrumentFilter, difficultyFilter, lastWorkedFrom }), [difficultyFilter, instrumentFilter, lastWorkedFrom, teacherFilter]);
  const filteredEntries = useMemo(() => filterTimeline(entries, timelineFilter, timelineFilters), [entries, timelineFilter, timelineFilters]);
  // Sekmelerdeki sayılar: hemen her kayıtta hem eser hem ödev olduğu için sayısız sekmeler
  // aynı listeyi gösteriyormuş gibi görünüyordu ("butonlar işlevsiz").
  const timelineCounts = useMemo(() => ({
    all: filterTimeline(entries, "all", timelineFilters).length,
    pieces: filterTimeline(entries, "pieces", timelineFilters).length,
    homework: filterTimeline(entries, "homework", timelineFilters).length,
  }), [entries, timelineFilters]);

  function clearFilters() {
    setTeacherFilter("all");
    setInstrumentFilter("all");
    setDifficultyFilter("all");
    setLastWorkedFrom("");
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Gelişim günlüğü"
        description="Ders notları, ödevler ve çalışılan eserler."
      />

      <StudentBar
        students={students ?? []}
        student={activeStudent}
        noteCount={analysis.noteCount}
        lastEntryAt={progress?.lastEntryAt ?? null}
        isLoading={studentsLoading}
        isError={studentsError}
        isFetching={studentsFetching}
        onRetry={() => void refetchStudents()}
        onSelect={(studentId) => { setSelectedStudentId(studentId); setShowComposer(false); setTimelineFilter("all"); }}
        onAdd={canWrite ? () => setShowComposer(true) : undefined}
      />

      {!studentsLoading && !studentsError && !activeStudent ? (
        <div className="app-card grid min-h-64 place-items-center p-8 text-center">
          <div>
            <span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="students" className="h-6 w-6" /></span>
            <p className="mt-4 text-sm font-bold">Gelişimi izlenecek öğrenci yok</p>
            <p className="mt-1 text-xs text-[var(--muted)]">Öğrenci eklendiğinde gelişim günlüğü burada açılır.</p>
          </div>
        </div>
      ) : activeStudent && (
        <>
          {canWrite && (
            <Modal open={showComposer} title="Yeni gelişim notu" description="Kayıt eklendiğinde özet otomatik yenilenir." onClose={() => setShowComposer(false)}>
              <ProgressComposer studentId={activeStudent.id} lessons={studentLessons} onClose={() => setShowComposer(false)} />
            </Modal>
          )}

          <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_21rem]">
            {!progressLoading && !progressError && <CurrentFocus entries={entries} />}
            {!progressLoading && !progressError && entries.length > 0 && (
              <div className="xl:sticky xl:top-4 xl:col-start-2 xl:row-span-2 xl:row-start-1">
                <SummaryCard analysis={analysis} studentId={activeStudent.id} />
              </div>
            )}
            <Timeline
              key={activeStudent.id}
              entries={filteredEntries}
              allEntries={entries}
              isLoading={progressLoading}
              isError={progressError}
              isFetching={progressFetching}
              onRetry={() => void refetchProgress()}
              filter={timelineFilter}
              counts={timelineCounts}
              onFilter={setTimelineFilter}
              studentId={activeStudent.id}
              canWrite={canWrite}
              onAdd={canWrite ? () => setShowComposer(true) : undefined}
              filters={{ teacherFilter, instrumentFilter, difficultyFilter, lastWorkedFrom }}
              onTeacher={setTeacherFilter}
              onInstrument={setInstrumentFilter}
              onDifficulty={setDifficultyFilter}
              onLastWorkedFrom={setLastWorkedFrom}
              onClearFilters={clearFilters}
            />
            <StudentLibrarySuggestions key={`suggestions-${activeStudent.id}`} studentId={activeStudent.id} />
          </div>
        </>
      )}
    </div>
  );
}

// Kullanıcı isteği: uzun, kaydırmalı bir liste yerine tek bir combobox. Not ekleme eylemi
// seçili öğrencinin hemen yanında durur - not her zaman o öğrenciye yazılır, sayfa başlığındaki
// bağlamsız bir "+" bunu söylemiyordu. Seçici içeriğe göre boyutlanır: geniş ekranda tüm satırı
// kaplayan kutu bir metin alanı gibi okunuyor, açılır ok isimden kopuk kalıyordu.
function StudentBar({
  students,
  student,
  noteCount,
  lastEntryAt,
  isLoading,
  isError,
  isFetching,
  onRetry,
  onSelect,
  onAdd,
}: {
  students: Student[];
  student?: Student;
  noteCount: number;
  lastEntryAt: string | null;
  isLoading: boolean;
  isError: boolean;
  isFetching: boolean;
  onRetry: () => void;
  onSelect: (studentId: string) => void;
  onAdd?: () => void;
}) {
  if (isLoading) return <div className="skeleton h-[5.5rem] rounded-[1.35rem]" />;
  if (isError) {
    return (
      <div className="flex items-center justify-between gap-3 rounded-[1.35rem] bg-[var(--danger-soft)] p-4">
        <p className="text-sm font-bold text-[var(--danger-strong)]">Öğrenciler yüklenemedi</p>
        <button type="button" onClick={onRetry} disabled={isFetching} className="btn btn-quiet disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button>
      </div>
    );
  }
  if (!students.length || !student) return null;

  return (
    <section className="app-card p-3 sm:p-4" aria-label="Öğrenci">
      <div className="flex items-center gap-2">
        {/* Sr-only span'i saran <label>, tarayıcıda combobox'ın erişilebilir adını
            "Öğrenci seç" yerine SEÇİLİ SEÇENEĞİN metnine ("Kerem Aksoy" gibi) çeviriyordu -
            açık aria-label bu belirsizliği ortadan kaldırıyor. */}
        <select
          aria-label="Öğrenci seç"
          value={student.id}
          onChange={(event) => onSelect(event.target.value)}
          className="field min-h-12 min-w-0 flex-1 truncate py-2 text-base font-bold sm:w-auto sm:max-w-sm sm:flex-none sm:pr-10"
        >
          {students.map((option) => (
            <option key={option.id} value={option.id}>
              {option.firstName} {option.lastName}{option.status !== "Active" ? " (pasif)" : ""}
            </option>
          ))}
        </select>
        {onAdd && (
          <button type="button" onClick={onAdd} aria-label="Yeni gelişim notu" className="btn btn-primary min-h-12 shrink-0 rounded-2xl">
            <Icon name="plus" className="h-4 w-4" />
            <span className="sm:hidden">Yeni not</span>
            <span className="hidden sm:inline">Yeni gelişim notu</span>
          </button>
        )}
      </div>
      <p className="mt-1.5 truncate px-1 text-xs text-[var(--muted)]">
        {lastEntryAt ? `Son kayıt ${formatDate(lastEntryAt, true)} · ${noteCount} ders notu` : "Henüz gelişim kaydı yok"}
      </p>
    </section>
  );
}

// Öğretmenin derse girerken ilk bakacağı şey: en son verilen ödev ve bir sonraki hedef. Akıştaki
// her kayıtta tekrar eden renkli kutular yerine bir kez, en üstte.
function CurrentFocus({ entries }: { entries: ProgressEntry[] }) {
  const homework = entries.find((entry) => entry.homework);
  const goal = entries.find((entry) => entry.nextGoal);
  if (!homework && !goal) return null;

  return (
    <section className="app-card p-4" aria-label="Güncel ödev ve hedef">
      <p className="text-micro text-[var(--muted)]">Şu an</p>
      <dl className="mt-3 grid gap-3 sm:grid-cols-2">
        {homework && <FocusItem label="Ödev" tone="warning" text={homework.homework!} entry={homework} />}
        {goal && <FocusItem label="Sonraki hedef" tone="success" text={goal.nextGoal!} entry={goal} />}
      </dl>
    </section>
  );
}

function FocusItem({ label, tone, text, entry }: { label: string; tone: "warning" | "success"; text: string; entry: ProgressEntry }) {
  const bar = tone === "warning" ? "bg-[var(--warning)]" : "bg-[var(--success)]";
  const color = tone === "warning" ? "text-[var(--warning-strong)]" : "text-[var(--success-strong)]";
  return (
    <div className="flex gap-3">
      <span aria-hidden className={`w-1 shrink-0 rounded-full ${bar}`} />
      <div className="min-w-0">
        <dt className={`text-xs font-bold ${color}`}>{label}</dt>
        <dd className="mt-0.5 text-sm leading-relaxed">{text}</dd>
        <dd className="mt-1 text-xs text-[var(--muted)]">{formatDate(entry.lessonStartAt, true)} · {entry.teacherName}</dd>
      </div>
    </div>
  );
}

// Eski dört istatistik kartı + koyu özet paneli tek, açık zeminli "Genel gelişim" kartına indi:
// öğretmen notlarından üretilen yapay zekâ yorumu, üç sayı ve repertuvar.
function SummaryCard({ analysis, studentId }: { analysis: ProgressAnalysis; studentId: string }) {
  const [showAllPieces, setShowAllPieces] = useState(false);
  const pieces = showAllPieces ? analysis.pieces : analysis.pieces.slice(0, PIECE_PREVIEW_COUNT);
  const stats = [
    { value: analysis.noteCount, label: "ders notu" },
    { value: analysis.pieceCount, label: "eser" },
    { value: `%${analysis.practiceRate}`, label: "süreklilik", hint: "Derslerin ne kadarında çalışılan konu veya gelişim notu var" },
  ];

  return (
    <section className="app-card overflow-hidden" aria-labelledby="general-progress-title">
      <div className="p-4">
        <h2 id="general-progress-title" className="text-title">Genel gelişim</h2>
        <AiProgressSummary studentId={studentId} />
        <dl className="mt-3 grid grid-cols-3 divide-x divide-[var(--line)] rounded-xl bg-[var(--surface-muted)]/60 py-2.5 text-center">
          {stats.map((stat) => (
            <div key={stat.label} title={stat.hint} className="px-1">
              <dd className="text-lg font-bold tabular-nums leading-none">{stat.value}</dd>
              <dt className="mt-1 text-[.75rem] font-semibold text-[var(--muted)]">{stat.label}</dt>
            </div>
          ))}
        </dl>
      </div>

      <div className="border-t border-[var(--line)] p-4">
        <p className="text-micro text-[var(--muted)]">Çalışılan eserler</p>
        {analysis.pieces.length ? (
          <>
            <ul className="mt-2 divide-y divide-[var(--line)]">
              {pieces.map((piece) => <PieceRow key={piece.title} piece={piece} />)}
            </ul>
            {analysis.pieces.length > PIECE_PREVIEW_COUNT && (
              <button type="button" onClick={() => setShowAllPieces((value) => !value)} className="pressable mt-1 min-h-11 text-xs font-bold text-[var(--brand-strong)]">
                {showAllPieces ? "Daha az göster" : `Tüm eserler (${analysis.pieces.length})`}
              </button>
            )}
          </>
        ) : (
          <p className="mt-2 text-xs leading-relaxed text-[var(--muted)]">Ders notuna eser adı eklendiğinde repertuvar burada listelenir.</p>
        )}
      </div>

    </section>
  );
}

// Yorum sunucuda üretilip önbelleğe alınır; yeni not girilince bir sonraki açılışta yenilenir.
// Kural tabanlı eski özet metni (buildProgressAnalysis.summary) burada artık gösterilmiyor:
// "yapay zekâ" etiketi yalnızca gerçekten modelden gelen metnin üstünde durmalı.
function AiProgressSummary({ studentId }: { studentId: string }) {
  const { data, isLoading, isError, isFetching, refetch } = useProgressSummary(studentId);
  if (!isLoading && !isError && data?.status === "NoNotes") return null;

  return (
    <div className="mt-3 rounded-xl border border-[var(--brand)]/20 bg-[var(--brand-soft)]/45 p-3" aria-live="polite" aria-busy={isLoading}>
      <p className="flex items-center gap-1.5 text-[.75rem] font-bold uppercase tracking-[.06em] text-[var(--brand-strong)]">
        <Icon name="sparkles" className="h-3.5 w-3.5 shrink-0" /> Yapay zekâ yorumu
      </p>
      {isLoading ? (
        <div className="mt-2 space-y-1.5">
          <div className="skeleton h-3.5 rounded" />
          <div className="skeleton h-3.5 rounded" />
          <div className="skeleton h-3.5 w-2/3 rounded" />
          <p className="pt-1 text-[.75rem] text-[var(--muted)]">Ders notları yorumlanıyor…</p>
        </div>
      ) : data?.status === "Unavailable" ? (
        // Gizlemek yerine yerini gösterir: kutu hiç görünmeyince "yapay zekâ yorumu nerede?"
        // sorusu cevapsız kalıyordu. Sunucuda Ai__Provider=OpenAi + Ai__ApiKey tanımlanınca dolar.
        <p className="mt-1.5 text-sm text-[var(--muted)]">Yapay zekâ yorumu şu an kapalı. Okul için bir yapay zekâ anahtarı tanımlandığında öğretmen notlarından otomatik oluşur.</p>
      ) : isError || data?.status === "Failed" || !data?.summary ? (
        <div className="mt-1.5">
          <p className="text-sm text-[var(--muted)]">Yorum şu an hazırlanamadı.</p>
          <button type="button" onClick={() => void refetch()} disabled={isFetching} className="mt-1 min-h-11 text-xs font-bold text-[var(--brand-strong)] underline disabled:opacity-50">{isFetching ? "Deneniyor…" : "Tekrar dene"}</button>
        </div>
      ) : (
        <>
          <p className="mt-1.5 whitespace-pre-line text-sm leading-relaxed">{data.summary}</p>
          <p className="mt-2 text-[.75rem] text-[var(--muted)]">
            {data.isStale
              ? "Son notları henüz kapsamıyor; bir sonraki açılışta yenilenecek."
              : `${data.sourceNoteCount} öğretmen notundan üretildi${data.generatedAt ? ` · ${formatDate(data.generatedAt, true)}` : ""}`}
          </p>
        </>
      )}
    </div>
  );
}

function DifficultyDots({ value }: { value: number | null }) {
  const filled = value ? Math.round(value) : 0;
  return (
    <span className="inline-flex shrink-0 items-center gap-0.5" role="img" aria-label={value ? `Zorluk ${value.toFixed(1).replace(".0", "")}/5 · ${difficultyLabel(value)}` : "Zorluk belirtilmedi"}>
      {[1, 2, 3, 4, 5].map((step) => (
        <span key={step} className={`h-1.5 w-1.5 rounded-full ${step <= filled ? "bg-[var(--brand)]" : "bg-[var(--line)]"}`} />
      ))}
    </span>
  );
}

function PieceRow({ piece }: { piece: PieceInsight }) {
  const suggested = piece.difficultySource === "assistant";
  return (
    <li className="flex items-center gap-3 py-2.5">
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-semibold">{piece.title}</p>
        <p className="mt-0.5 text-xs text-[var(--muted)]">{piece.appearances} ders · son {formatDate(piece.latestAt, true)}</p>
      </div>
      <div className="shrink-0 text-right" title={piece.difficultyReason}>
        <DifficultyDots value={piece.averageDifficulty} />
        <p className="mt-1 text-[.75rem] font-semibold text-[var(--muted)]">{difficultyLabel(piece.averageDifficulty)}{suggested && " · öneri"}</p>
      </div>
    </li>
  );
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
      <label className="form-label rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/55 p-3"><span>Öğretmen notu <span className="font-medium">· yalnızca okul ekibi görür</span></span><textarea value={note} onChange={(event) => setNote(event.target.value)} rows={3} className="field resize-y bg-white text-sm" placeholder="Bugünkü ilerleme, güçlü taraflar ve dikkat edilmesi gerekenler…" /><span className="text-meta mt-1.5 block">Bu alan veli portalına gönderilmez. Veliye paylaşılacak metin, kayıt sonrasında ayrı olarak hazırlanır ve onaylanır.</span></label>
      <div className="grid gap-3 sm:grid-cols-3"><label className="form-label"><span>Ödev</span><textarea value={homework} onChange={(event) => setHomework(event.target.value)} rows={2} className="field resize-y text-sm" placeholder="Bir sonraki derse kadar" /></label><label className="form-label"><span>Sonraki hedef</span><textarea value={nextGoal} onChange={(event) => setNextGoal(event.target.value)} rows={2} className="field resize-y text-sm" placeholder="Bir sonraki odak" /></label><label className="form-label"><span>Eser zorluğu <span className="font-medium">· isteğe bağlı</span></span><select value={pieceDifficulty} onChange={(event) => setPieceDifficulty(event.target.value)} className="field text-sm"><option value="">Otomatik öner</option>{[1, 2, 3, 4, 5].map((level) => <option key={level} value={level}>{level}/5 · {difficultyLabel(level)}</option>)}</select><span className="block text-[.75rem] font-medium leading-relaxed">Boş bırakırsan ders notuna göre kural tabanlı önerilir.</span></label></div>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Gelişim notunu kaydet" pending={createNote.isPending} disabled={!lessons.length} />
    </div>}
  </form>;
}

type TimelineFilters = { teacherFilter: string; instrumentFilter: string; difficultyFilter: string; lastWorkedFrom: string };

// "Eserler" bir repertuvar görünümüdür: her eser, en son çalışıldığı kayıtla bir kez listelenir
// (kayıtlar yeniden eskiye sıralı geldiği için ilk görülen en yenisidir). "Ödevler" ödev veya
// sonraki hedef yazılmış kayıtlardır.
function filterTimeline(entries: ProgressEntry[], kind: TimelineFilter, filters: TimelineFilters) {
  const matching = entries.filter((entry) =>
    (filters.teacherFilter === "all" || entry.teacherId === filters.teacherFilter) &&
    (filters.instrumentFilter === "all" || entry.instrumentId === filters.instrumentFilter) &&
    (filters.difficultyFilter === "all" || entry.pieceDifficulty === Number(filters.difficultyFilter)) &&
    (!filters.lastWorkedFrom || new Date(entry.lessonStartAt) >= new Date(`${filters.lastWorkedFrom}T00:00:00`)));
  if (kind === "homework") return matching.filter((entry) => !!entry.homework || !!entry.nextGoal);
  if (kind !== "pieces") return matching;
  const seen = new Set<string>();
  return matching.filter((entry) => {
    const key = entry.pieceTitle?.trim().toLocaleLowerCase("tr-TR");
    if (!key || seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function Timeline({
  entries,
  allEntries,
  isLoading,
  isError,
  isFetching,
  onRetry,
  filter,
  counts,
  onFilter,
  studentId,
  canWrite,
  onAdd,
  filters,
  onTeacher,
  onInstrument,
  onDifficulty,
  onLastWorkedFrom,
  onClearFilters,
}: {
  entries: ProgressEntry[];
  allEntries: ProgressEntry[];
  isLoading: boolean;
  isError: boolean;
  isFetching: boolean;
  onRetry: () => void;
  filter: TimelineFilter;
  counts: Record<TimelineFilter, number>;
  onFilter: (filter: TimelineFilter) => void;
  studentId: string;
  canWrite: boolean;
  onAdd?: () => void;
  filters: TimelineFilters;
  onTeacher: (value: string) => void;
  onInstrument: (value: string) => void;
  onDifficulty: (value: string) => void;
  onLastWorkedFrom: (value: string) => void;
  onClearFilters: () => void;
}) {
  const [visibleCount, setVisibleCount] = useState(TIMELINE_PAGE_SIZE);
  const activeFilterCount = [filters.teacherFilter !== "all", filters.instrumentFilter !== "all", filters.difficultyFilter !== "all", !!filters.lastWorkedFrom].filter(Boolean).length;
  // Ek filtreler ancak istenince açılır; kapalıyken de etkin filtre sayısı butondaki rozette
  // görünür, böylece "liste neden kısa" sorusu cevapsız kalmaz.
  const [showFilters, setShowFilters] = useState(false);
  const tabs: Array<[TimelineFilter, string]> = [["all", "Tümü"], ["pieces", "Eserler"], ["homework", "Ödevler"]];
  const visibleEntries = entries.slice(0, visibleCount);

  return (
    <section className="app-card overflow-hidden" aria-label="Ders kayıtları">
      <div className="border-b border-[var(--line)] p-3 sm:p-4">
        <div className="flex items-center justify-between gap-2 px-1">
          <h2 className="text-title">Ders kayıtları</h2>
          {!isLoading && !isError && <span className="text-xs font-semibold text-[var(--muted)]">{entries.length} kayıt</span>}
        </div>
        <div className="mt-3 flex items-center gap-2">
          <div className="flex min-w-0 flex-1 gap-1 rounded-xl bg-[var(--surface-muted)] p-1" role="group" aria-label="Kayıt türü">
            {tabs.map(([value, label]) => (
              <button
                key={value}
                type="button"
                aria-pressed={filter === value}
                onClick={() => { onFilter(value); setVisibleCount(TIMELINE_PAGE_SIZE); }}
                className={`pressable min-h-10 flex-1 truncate rounded-lg px-2 text-xs font-bold ${filter === value ? "bg-[var(--surface)] text-[var(--brand-strong)] shadow-sm" : "text-[var(--muted)]"}`}
              >
                {label} <span className="font-semibold opacity-70">{counts[value]}</span>
              </button>
            ))}
          </div>
          <button
            type="button"
            onClick={() => setShowFilters((value) => !value)}
            aria-expanded={showFilters}
            aria-label={activeFilterCount ? `Filtreler (${activeFilterCount} etkin)` : "Filtreler"}
            className={`pressable relative grid h-12 w-12 shrink-0 place-items-center rounded-xl border ${showFilters || activeFilterCount ? "border-[var(--brand)] text-[var(--brand-strong)]" : "border-[var(--line)] text-[var(--muted)]"}`}
          >
            <Icon name="filter" className="h-4 w-4" />
            {activeFilterCount > 0 && <span className="absolute -right-1 -top-1 grid h-5 min-w-5 place-items-center rounded-full bg-[var(--brand-strong)] px-1 text-[.6875rem] font-bold text-white">{activeFilterCount}</span>}
          </button>
        </div>
        {showFilters && (
          <RepertoireFilters
            entries={allEntries}
            filters={filters}
            activeCount={activeFilterCount}
            onTeacher={onTeacher}
            onInstrument={onInstrument}
            onDifficulty={onDifficulty}
            onLastWorkedFrom={onLastWorkedFrom}
            onClear={onClearFilters}
          />
        )}
      </div>

      {isLoading && <div className="space-y-3 p-4">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-24 rounded-xl" />)}</div>}
      {!isLoading && isError && (
        <div className="grid min-h-56 place-items-center p-8 text-center">
          <div>
            <p className="text-sm font-bold">Gelişim kayıtları yüklenemedi</p>
            <p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p>
            <button type="button" onClick={onRetry} disabled={isFetching} className="btn btn-quiet mt-3 disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button>
          </div>
        </div>
      )}
      {!isLoading && !isError && !entries.length && (
        <div className="grid min-h-56 place-items-center p-8 text-center">
          <div>
            <span className="mx-auto grid h-11 w-11 place-items-center rounded-2xl bg-[var(--surface-muted)] text-[var(--brand)]"><Icon name="note" className="h-5 w-5" /></span>
            {allEntries.length ? (
              <>
                <p className="mt-4 text-sm font-bold">Bu filtrede kayıt yok</p>
                <button type="button" onClick={() => { onFilter("all"); onClearFilters(); }} className="btn btn-quiet mt-3">Filtreleri temizle</button>
              </>
            ) : (
              <>
                <p className="mt-4 text-sm font-bold">Henüz ders kaydı yok</p>
                <p className="mt-1 max-w-sm text-xs text-[var(--muted)]">İlk ders notu eklendiğinde akış ve özet burada oluşur.</p>
                {onAdd && <button type="button" onClick={onAdd} className="btn btn-primary mt-4"><Icon name="plus" className="h-4 w-4" /> İlk notu ekle</button>}
              </>
            )}
          </div>
        </div>
      )}
      {!isLoading && !isError && entries.length > 0 && (
        <>
          <div className="divide-y divide-[var(--line)]">
            {visibleEntries.map((entry, index) => <TimelineEntry key={entry.id} entry={entry} newer={visibleEntries[index - 1]} studentId={studentId} canWrite={canWrite} />)}
          </div>
          {entries.length > visibleCount && (
            <div className="border-t border-[var(--line)] p-3">
              <button type="button" onClick={() => setVisibleCount((count) => count + TIMELINE_PAGE_SIZE)} className="btn btn-quiet w-full">
                Daha eski kayıtlar ({entries.length - visibleCount})
              </button>
            </div>
          )}
        </>
      )}
    </section>
  );
}

function RepertoireFilters({ entries, filters, activeCount, onTeacher, onInstrument, onDifficulty, onLastWorkedFrom, onClear }: { entries: ProgressEntry[]; filters: TimelineFilters; activeCount: number; onTeacher: (value: string) => void; onInstrument: (value: string) => void; onDifficulty: (value: string) => void; onLastWorkedFrom: (value: string) => void; onClear: () => void }) {
  const teachers = Array.from(new Map(entries.map((entry) => [entry.teacherId, entry.teacherName])).entries());
  const instruments = Array.from(new Map(entries.map((entry) => [entry.instrumentId, entry.instrumentName])).entries());
  const labelClass = "text-xs font-semibold text-[var(--muted)]";
  return (
    <div className="mt-3 grid grid-cols-2 gap-2.5 lg:grid-cols-4" aria-label="Repertuvar filtreleri">
      <label className={labelClass}>Öğretmen<select value={filters.teacherFilter} onChange={(event) => onTeacher(event.target.value)} className="field mt-1 text-sm"><option value="all">Tümü</option>{teachers.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>
      <label className={labelClass}>Enstrüman<select value={filters.instrumentFilter} onChange={(event) => onInstrument(event.target.value)} className="field mt-1 text-sm"><option value="all">Tümü</option>{instruments.map(([id, name]) => <option key={id} value={id}>{name}</option>)}</select></label>
      <label className={labelClass}>Zorluk<select value={filters.difficultyFilter} onChange={(event) => onDifficulty(event.target.value)} className="field mt-1 text-sm"><option value="all">Tümü</option>{[1, 2, 3, 4, 5].map((level) => <option key={level} value={level}>{level}/5 · {difficultyLabel(level)}</option>)}</select></label>
      <label className={labelClass}>Şu tarihten beri<input type="date" value={filters.lastWorkedFrom} onChange={(event) => onLastWorkedFrom(event.target.value)} className="field mt-1 text-sm" /></label>
      {activeCount > 0 && <button type="button" onClick={onClear} className="col-span-2 min-h-11 justify-self-start px-1 text-xs font-bold text-[var(--brand-strong)] underline lg:col-span-4">Filtreleri temizle</button>}
    </div>
  );
}

// Tek bir ders kaydı. Önceki sürümde tarih sol sütunda, ödev ve hedef ayrı renkli kutularda,
// eser ayrı bir şeritte duruyordu; telefonda her kayıt neredeyse bir ekran boyu oluyordu.
// Artık: tarih/ders tek satır başlık, eser tek satır, not düz metin, ödev/hedef ince bir sol
// çizgiyle etiketli satırlar. Bir önceki (daha yeni) kayıtla birebir aynı ödev/hedef tekrar
// yazılmaz - öğretmenler aynı ödevi haftalarca taşıyabiliyor ve akış aynı cümlelerle doluyordu.
function TimelineEntry({ entry, newer, studentId, canWrite }: { entry: ProgressEntry; newer?: ProgressEntry; studentId: string; canWrite: boolean }) {
  const homework = entry.homework && entry.homework !== newer?.homework ? entry.homework : null;
  const nextGoal = entry.nextGoal && entry.nextGoal !== newer?.nextGoal ? entry.nextGoal : null;
  const sameHomework = !!entry.homework && !homework;
  const sameGoal = !!entry.nextGoal && !nextGoal;
  const carriedOver = sameHomework && sameGoal ? "Aynı ödev ve hedef sonraki derste de sürdü." : sameHomework ? "Aynı ödev sonraki derste de sürdü." : sameGoal ? "Aynı hedef sonraki derste de sürdü." : null;
  return (
    <article className="p-4">
      <header className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
        <h3 className="text-sm font-bold">
          {ENTRY_DATE_FORMATTER.format(new Date(entry.lessonStartAt))}
          <span className="font-medium text-[var(--muted)]"> · {entry.instrumentName}</span>
        </h3>
        <p className="text-xs text-[var(--muted)]">{entry.teacherName} · {formatTime(entry.lessonStartAt)}</p>
      </header>

      {entry.pieceTitle && (
        <p className="mt-2 flex items-center gap-2 text-sm">
          <Icon name="music" className="h-4 w-4 shrink-0 text-[var(--brand)]" />
          <span className="min-w-0 flex-1 truncate font-semibold">{entry.pieceTitle}</span>
          <DifficultyDots value={entry.pieceDifficulty} />
        </p>
      )}

      {entry.note && <p className="mt-2 whitespace-pre-line text-sm leading-relaxed">{entry.note}</p>}
      {entry.practiced && <p className="mt-1.5 text-xs leading-relaxed text-[var(--muted)]"><span className="font-bold">Çalışıldı:</span> {entry.practiced}</p>}

      {(homework || nextGoal) && (
        <dl className="mt-3 space-y-1.5 border-l-2 border-[var(--line)] pl-3 text-sm leading-relaxed">
          {homework && <div><dt className="inline font-bold text-[var(--warning-strong)]">Ödev: </dt><dd className="inline">{homework}</dd></div>}
          {nextGoal && <div><dt className="inline font-bold text-[var(--success-strong)]">Hedef: </dt><dd className="inline">{nextGoal}</dd></div>}
        </dl>
      )}
      {carriedOver && <p className="mt-2 text-xs text-[var(--muted)]">{carriedOver}</p>}

      {canWrite && <ParentCommentEditor entry={entry} studentId={studentId} />}
    </article>
  );
}

function ParentCommentEditor({ entry, studentId }: { entry: ProgressEntry; studentId: string }) {
  const setComment = useSetParentComment(studentId);
  const revokeComment = useRevokeParentComment(studentId);
  const [open, setOpen] = useState(false);
  const draftKey = `abdera:parent-comment-draft:${entry.id}`;
  const initialComment = entry.parentComment ?? "";
  const [comment, setCommentValue] = useState(() => {
    if (typeof window === "undefined") return initialComment;
    return window.sessionStorage.getItem(draftKey) ?? initialComment;
  });
  const [error, setError] = useState<string | null>(null);
  const dirty = comment !== initialComment;

  useEffect(() => {
    if (!dirty) return;
    window.sessionStorage.setItem(draftKey, comment);
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [comment, dirty, draftKey]);

  async function save(approve: boolean) {
    setError(null);
    try {
      await setComment.mutateAsync({ noteId: entry.id, parentComment: comment, approve });
      window.sessionStorage.removeItem(draftKey);
      setOpen(false);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Veli yorumu kaydedilemedi.");
    }
  }

  const status = entry.parentCommentApprovedAt
    ? { text: "Onaylandı ve veliye görünür", dot: "bg-[var(--success)]" }
    : entry.parentComment
      ? { text: "Taslak — veliye görünmez", dot: "bg-[var(--warning)]" }
      : dirty
        ? { text: "Kaydedilmemiş taslak bu cihazda korunuyor", dot: "bg-[var(--warning)]" }
        : { text: "Henüz hazırlanmadı", dot: "bg-[var(--line)]" };

  return <div className="mt-3 border-t border-dashed border-[var(--line)] pt-2">
    <div className="flex items-center justify-between gap-2">
      <p className="flex min-w-0 items-center gap-2 text-xs"><span aria-hidden className={`h-2 w-2 shrink-0 rounded-full ${status.dot}`} /><span className="font-bold">Veli yorumu</span><span className="truncate text-[var(--muted)]">{status.text}</span></p>
      <button type="button" onClick={() => setOpen((value) => !value)} aria-expanded={open} className="pressable min-h-11 shrink-0 rounded-lg px-2 text-xs font-bold text-[var(--brand-strong)]">{open ? "Kapat" : entry.parentComment || dirty ? "Düzenle" : "Yorum hazırla"}</button>
    </div>
    {!open && entry.parentComment && <p className="line-clamp-3 whitespace-pre-line pl-4 text-sm leading-relaxed text-[var(--muted)]">{entry.parentComment}</p>}
    {open && <div className="mt-1 space-y-2">
      <textarea value={comment} onChange={(event) => setCommentValue(event.target.value)} rows={3} className="field resize-y text-sm" placeholder="Ham notu veliye uygun, yapıcı bir yorum olarak düzenleyin." />
      <div className="sticky bottom-0 z-10 -mx-4 grid grid-cols-2 gap-2 border-t border-[var(--line)] bg-[var(--surface)] px-4 pb-[max(.5rem,env(safe-area-inset-bottom))] pt-2 sm:static sm:mx-0 sm:flex sm:justify-end sm:border-0 sm:p-0">
        <button type="button" onClick={() => void save(false)} disabled={setComment.isPending || !comment.trim()} className="btn btn-quiet">Taslak kaydet</button>
        <button type="button" onClick={() => void save(true)} disabled={setComment.isPending || !comment.trim()} className="btn btn-primary">Onayla ve veliye aç</button>
      </div>
      {error && <p role="alert" className="text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
    </div>}
    {entry.parentCommentApprovedAt && !open && <button type="button" onClick={() => void revokeComment.mutateAsync(entry.id)} disabled={revokeComment.isPending} className="min-h-11 text-xs font-bold text-[var(--danger-strong)] underline">Veli görünürlüğünü geri çek</button>}
  </div>;
}
