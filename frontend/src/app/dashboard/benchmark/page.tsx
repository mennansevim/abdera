"use client";

// docs/13 Pillar F — öğrenci/öğretmen benchmark. Öğretmenlerin eğitici, öğrencilerin öğrenci
// olarak performansı: öğrenci sayısı, çalışma günü (ders), not/onaylı yorum, katılım oranı ve
// bunlardan türetilen kompozit skor (backend Benchmark.cs).
import { useMemo, useState } from "react";
import { AdminGate, PageHeader, SearchInput } from "@/components/ui";
import {
  useStudentBenchmark,
  useTeacherBenchmark,
  type StudentBenchmarkRow,
  type TeacherBenchmarkRow,
} from "@/lib/benchmark";

type Tab = "teachers" | "students";

export default function BenchmarkPage() {
  return (
    <AdminGate>
      <BenchmarkView />
    </AdminGate>
  );
}

function BenchmarkView() {
  const [tab, setTab] = useState<Tab>("teachers");
  return (
    <div className="space-y-4">
      <PageHeader
        title="Performans / Benchmark"
        description="Öğretmen ve öğrenci performansı: öğrenci sayısı, çalışma günü, not & onaylı yorum, katılım."
      />
      <div className="inline-flex rounded-xl bg-[var(--surface-muted)] p-1 text-sm font-semibold">
        <button
          type="button"
          onClick={() => setTab("teachers")}
          className={`pressable min-h-10 rounded-lg px-4 ${tab === "teachers" ? "bg-white shadow-sm text-[var(--brand-strong)]" : "text-[var(--muted)]"}`}
        >Öğretmenler</button>
        <button
          type="button"
          onClick={() => setTab("students")}
          className={`pressable min-h-10 rounded-lg px-4 ${tab === "students" ? "bg-white shadow-sm text-[var(--brand-strong)]" : "text-[var(--muted)]"}`}
        >Öğrenciler</button>
      </div>

      {tab === "teachers" ? <TeachersTab /> : <StudentsTab />}
    </div>
  );
}

function ScoreBar({ score }: { score: number }) {
  return (
    <div className="flex items-center gap-2">
      <div className="h-2 flex-1 overflow-hidden rounded-full bg-[var(--surface-muted)]">
        <div className="h-full rounded-full bg-[var(--brand)]" style={{ width: `${Math.max(2, Math.min(100, score))}%` }} />
      </div>
      <span className="w-10 shrink-0 text-right text-sm font-bold tabular-nums">{score.toFixed(0)}</span>
    </div>
  );
}

function Metric({ label, value }: { label: string; value: string | number }) {
  return (
    <span className="inline-flex flex-col rounded-lg bg-[var(--surface-muted)] px-2.5 py-1">
      <span className="text-[.75rem] font-semibold uppercase tracking-wide text-[var(--muted)]">{label}</span>
      <span className="text-sm font-bold tabular-nums">{value}</span>
    </span>
  );
}

function RankBadge({ rank }: { rank: number }) {
  const medal = rank === 1 ? "bg-[#f6d97a] text-[#6b4e00]" : rank === 2 ? "bg-[#dfe3e8] text-[#4a5560]" : rank === 3 ? "bg-[#eabf94] text-[#6b3f16]" : "bg-[var(--surface-muted)] text-[var(--muted)]";
  return <span className={`grid h-7 w-7 shrink-0 place-items-center rounded-full text-xs font-bold ${medal}`}>{rank}</span>;
}

function Loading() {
  return <div className="app-card space-y-3 p-4">{Array.from({ length: 6 }, (_, i) => <div key={i} className="skeleton h-14 rounded-xl" />)}</div>;
}

function TeachersTab() {
  const { data, isLoading } = useTeacherBenchmark();
  const [search, setSearch] = useState("");
  const rows = useMemo(() => {
    const q = search.trim().toLocaleLowerCase("tr-TR");
    return (data ?? []).filter((r) => !q || r.teacherName.toLocaleLowerCase("tr-TR").includes(q));
  }, [data, search]);

  if (isLoading) return <Loading />;

  return (
    <div className="space-y-3">
      <SearchInput value={search} onChange={setSearch} label="Öğretmen ara" placeholder="Öğretmen ara…" />
      <div className="app-card divide-y divide-[var(--line)]">
        {rows.map((r: TeacherBenchmarkRow) => (
          <div key={r.teacherId} className="flex flex-col gap-2 p-4 sm:flex-row sm:items-center sm:gap-4">
            <div className="flex items-center gap-3 sm:w-56 sm:shrink-0">
              {/* Sıra her zaman filtrelenmemiş listedeki konumdur; arama yalnızca görünürlüğü daraltır. */}
              <RankBadge rank={(data ?? []).indexOf(r) + 1} />
              <span className="truncate text-sm font-bold">{r.teacherName}</span>
            </div>
            <div className="min-w-0 flex-1"><ScoreBar score={r.score} /></div>
            <div className="flex flex-wrap gap-1.5">
              <Metric label="Öğrenci" value={r.activeStudents} />
              <Metric label="Ders" value={r.lessons} />
              <Metric label="Not" value={r.notes} />
              <Metric label="Onaylı yorum" value={r.approvedComments} />
              <Metric label="Katılım" value={`${Math.round(r.attendanceRate * 100)}%`} />
            </div>
          </div>
        ))}
        {rows.length === 0 && <div className="p-6 text-center text-sm text-[var(--muted)]">Öğretmen bulunamadı.</div>}
      </div>
    </div>
  );
}

const STUDENT_LIMIT = 100;

function StudentsTab() {
  const { data, isLoading } = useStudentBenchmark();
  const [search, setSearch] = useState("");
  const filtered = useMemo(() => {
    const q = search.trim().toLocaleLowerCase("tr-TR");
    return (data ?? []).filter((r) => !q || r.studentName.toLocaleLowerCase("tr-TR").includes(q));
  }, [data, search]);
  const rows = filtered.slice(0, STUDENT_LIMIT);

  if (isLoading) return <Loading />;

  return (
    <div className="space-y-3">
      <SearchInput value={search} onChange={setSearch} label="Öğrenci ara" placeholder="Öğrenci ara…" />
      {!search && (data?.length ?? 0) > STUDENT_LIMIT && (
        <p className="text-meta text-[var(--muted)]">En yüksek skorlu {STUDENT_LIMIT} öğrenci gösteriliyor ({data?.length} öğrenciden). Aramayla tümünde filtreleyin.</p>
      )}
      <div className="app-card divide-y divide-[var(--line)]">
        {rows.map((r: StudentBenchmarkRow) => {
          const rank = (data ?? []).indexOf(r) + 1;
          return (
            <div key={r.studentId} className="flex flex-col gap-2 p-4 sm:flex-row sm:items-center sm:gap-4">
              <div className="flex items-center gap-3 sm:w-56 sm:shrink-0">
                <RankBadge rank={rank} />
                <span className="truncate text-sm font-bold">{r.studentName}</span>
              </div>
              <div className="min-w-0 flex-1"><ScoreBar score={r.score} /></div>
              <div className="flex flex-wrap gap-1.5">
                <Metric label="Kurs" value={r.activeEnrollments} />
                <Metric label="Ders" value={r.lessons} />
                <Metric label="Not" value={r.notesReceived} />
                <Metric label="Katılım" value={`${Math.round(r.attendanceRate * 100)}%`} />
              </div>
            </div>
          );
        })}
        {rows.length === 0 && <div className="p-6 text-center text-sm text-[var(--muted)]">Öğrenci bulunamadı.</div>}
      </div>
    </div>
  );
}
