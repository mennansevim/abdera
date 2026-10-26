"use client";

import { STATE_STYLE, StateLegend } from "@/components/analysis";
import { rate, sum, useStudentAnalysis, type AnalysisLessonState } from "@/lib/analysis";
import type { AssistantStudent } from "@/lib/assistant";
import { useProgressSummary, useStudentProgress, type SkillAssessmentEntry } from "@/lib/progress";
import { Actions, AssistantCard, ErrorLine, GoLink, Loading, StudentHeading } from "./shared";

// "Mennan Sevim'in gelişimini getir": son 3 ayın devamı, beceri değişimi, eser, son not ve
// öğretmen notlarından üretilmiş özet - üç mevcut uçtan (analysis, progress, progress-summary).
// Öğretmen oturumunda uçlar zaten yalnızca kendi notlarını döndürür.
const MONTHS = 3;
const RECENT_LESSONS = 16;
const MARKED_STATES: AnalysisLessonState[] = ["Present", "Makeup", "Excused", "Absent"];

export function ProgressCard({ student }: { student: AssistantStudent }) {
  const analysis = useStudentAnalysis(student.studentId, MONTHS);
  const progress = useStudentProgress(student.studentId);
  const summary = useProgressSummary(student.studentId);

  if (analysis.isLoading || progress.isLoading) return <AssistantCard><StudentHeading student={student} /><Loading>Gelişim bilgileri getiriliyor…</Loading></AssistantCard>;
  if (analysis.isError || progress.isError || !analysis.data || !progress.data) {
    return <AssistantCard><StudentHeading student={student} /><ErrorLine>Gelişim bilgileri şu an getirilemedi.</ErrorLine></AssistantCard>;
  }

  const months = analysis.data.months;
  const attendance = rate(sum(months, (month) => month.attended), sum(months, (month) => month.rated));
  const assignments = sum(months, (month) => month.assignmentCount);
  const assignmentsDone = sum(months, (month) => month.assignmentCompleted);
  const lessons = months.flatMap((month) => month.lessons)
    .filter((lesson) => MARKED_STATES.includes(lesson.state))
    .sort((a, b) => a.startAt.localeCompare(b.startAt))
    .slice(-RECENT_LESSONS);
  const skills = skillChanges(progress.data.skillAssessments);
  const piece = analysis.data.pieces.filter((item) => !item.finished).at(-1);
  const lastEntry = progress.data.entries[0];
  const since = analysis.data.enrolledSince
    ? `Kayıt: ${new Date(analysis.data.enrolledSince).toLocaleDateString("tr-TR", { month: "long", year: "numeric" })}`
    : undefined;

  return (
    <AssistantCard>
      <StudentHeading student={student} meta={since} />

      <dl className="grid grid-cols-3 gap-2">
        <Kpi label="Devam (3 ay)" value={attendance === null ? "—" : `%${attendance}`} />
        <Kpi label="Ödev" value={assignments ? `${assignmentsDone}/${assignments}` : "—"} />
        <Kpi label="Telafi hakkı" value={String(analysis.data.availableMakeupCredits)} />
      </dl>

      {lessons.length > 0 && (
        <section className="space-y-1.5">
          <h3 className="text-xs font-bold uppercase tracking-wide text-[var(--muted)]">Son {lessons.length} ders</h3>
          <ul className="flex flex-wrap gap-1" aria-label="Son dersler">
            {lessons.map((lesson) => (
              <li key={lesson.startAt} title={`${new Date(lesson.startAt).toLocaleDateString("tr-TR")} · ${STATE_STYLE[lesson.state].label}`} className="h-3.5 w-3.5 rounded" style={STATE_STYLE[lesson.state].dot} />
            ))}
          </ul>
          <StateLegend states={MARKED_STATES.filter((state) => lessons.some((lesson) => lesson.state === state))} />
        </section>
      )}

      {skills.length > 0 && (
        <section className="space-y-1.5">
          <h3 className="text-xs font-bold uppercase tracking-wide text-[var(--muted)]">Beceriler (1–5)</h3>
          <ul className="space-y-1.5">
            {skills.map((skill) => (
              <li key={skill.label} className="grid grid-cols-[6.5rem_minmax(0,1fr)_3.25rem] items-center gap-2 text-xs">
                <span className="truncate">{skill.label}</span>
                <span className="relative h-2 overflow-hidden rounded-full bg-[var(--surface-muted)]">
                  <span className="absolute inset-y-0 left-0 rounded-full bg-[var(--brand)]" style={{ width: `${skill.score * 20}%` }} />
                </span>
                <span className={`text-right tabular-nums ${skill.previous !== null && skill.score > skill.previous ? "font-bold text-[var(--success-strong)]" : "text-[var(--muted)]"}`}>
                  {skill.score}{skill.previous !== null && skill.score !== skill.previous ? (skill.score > skill.previous ? " ↑" : " ↓") : ""}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {(piece || lastEntry) && (
        <div className="space-y-1 border-l-[3px] border-[var(--line)] pl-3 text-sm">
          {piece && <p><span className="text-meta text-xs">Çalıştığı eser</span><br /><b>{piece.title}</b>{piece.composer ? ` · ${piece.composer}` : ""}</p>}
          {lastEntry && (lastEntry.note || lastEntry.nextGoal) && (
            <p className="text-xs">
              <span className="text-meta">Son not ({new Date(lastEntry.lessonStartAt).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}, {lastEntry.teacherName}): </span>
              {lastEntry.note}{lastEntry.nextGoal ? ` Sonraki hedef: ${lastEntry.nextGoal}` : ""}
            </p>
          )}
        </div>
      )}

      {summary.data?.status === "Ready" && summary.data.summary && (
        <div className="space-y-1 rounded-xl bg-[var(--brand-soft)] px-3 py-2.5 text-sm">
          <p className="text-xs font-bold text-[var(--brand-strong)]">
            Öğretmen notlarından özet{summary.data.generatedAt ? ` · ${new Date(summary.data.generatedAt).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}` : ""} · {summary.data.sourceNoteCount} not
          </p>
          <p>{summary.data.summary}</p>
        </div>
      )}
      {summary.data?.status === "NotEnoughNotes" && (
        <p className="text-meta text-xs">Genel özet {summary.data.minimumNotes} not girilince oluşur ({summary.data.noteCount}/{summary.data.minimumNotes}).</p>
      )}

      <Actions>
        <GoLink href={`/dashboard/students/analysis?studentId=${student.studentId}`}>Öğrenci analizi</GoLink>
        <GoLink href={`/dashboard/progress?studentId=${student.studentId}`}>Gelişim notları</GoLink>
      </Actions>
    </AssistantCard>
  );
}

function Kpi({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl bg-[var(--surface-muted)] px-2.5 py-2">
      <dt className="text-[.7rem] text-[var(--muted)]">{label}</dt>
      <dd className="text-base font-bold tabular-nums">{value}</dd>
    </div>
  );
}

// Her beceri için son puan ve bir önceki puan (değişim oku için).
function skillChanges(assessments: SkillAssessmentEntry[]) {
  const byLabel = new Map<string, SkillAssessmentEntry[]>();
  for (const item of [...assessments].sort((a, b) => b.assessedAt.localeCompare(a.assessedAt))) {
    byLabel.set(item.skillLabel, [...(byLabel.get(item.skillLabel) ?? []), item]);
  }
  return Array.from(byLabel, ([label, items]) => ({ label, score: items[0]!.score, previous: items[1]?.score ?? null }))
    .sort((a, b) => a.label.localeCompare(b.label, "tr"));
}
