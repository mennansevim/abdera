"use client";

import Link from "next/link";
import { Suspense, useState } from "react";
import { useSearchParams } from "next/navigation";
import {
  AnalysisCard, AnalysisHero, EmptyCell, Gauge, HeroChip, LevelRow, MiniBar, PeriodPicker, StateLegend, StatBlock,
  STATE_STYLE, Strip, StripFootnote, StripRow, rateTone,
} from "@/components/analysis";
import { EmptyState } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  WEEKDAY_NAMES, formatDecimal, levelName, longMonth, rate, sum, trimLeadingMonths, useTeacherAnalysis,
  type AnalysisLessonState, type PeriodOption, type TeacherAnalysis, type TeacherAnalysisMonth,
} from "@/lib/analysis";
import { formatMoney } from "@/lib/billing-format";
import { useBranchTerms } from "@/lib/instance";
import { useMe } from "@/lib/use-auth";

export default function TeacherAnalysisPage() {
  return (
    <Suspense>
      <TeacherAnalysisContent />
    </Suspense>
  );
}

// Sütundaki yığın sırası: alttan üste yapılan → kaçan → yapılmayan → henüz gelmemiş.
const STACK: { state: AnalysisLessonState; pick: (month: TeacherAnalysisMonth) => number }[] = [
  { state: "Present", pick: (month) => month.present },
  { state: "Makeup", pick: (month) => month.makeup },
  { state: "Excused", pick: (month) => month.excused },
  { state: "Absent", pick: (month) => month.absent },
  { state: "Unmarked", pick: (month) => month.unmarked },
  { state: "Cancelled", pick: (month) => month.cancelled },
  { state: "Upcoming", pick: (month) => month.upcoming },
];

function monthTotal(month: TeacherAnalysisMonth) {
  return sum(STACK, (item) => item.pick(month));
}

function formatDate(value: string) {
  return new Date(value).toLocaleDateString("tr-TR", { day: "numeric", month: "short" });
}

function TeacherAnalysisContent() {
  const { data: me } = useMe();
  const teacherId = useSearchParams().get("teacherId") ?? me?.teacherId ?? null;
  const [months, setMonths] = useState<PeriodOption>("6");
  const [selectedPeriod, setSelectedPeriod] = useState<string | null>(null);
  const { data, isLoading, error } = useTeacherAnalysis(teacherId, Number(months));

  if (me && !teacherId) return <EmptyState icon="teachers" title="Öğretmen seçilmedi" description="Öğretmenler listesinden bir öğretmenin analizini açın." />;
  if (error) {
    const forbidden = error instanceof ApiError && error.status === 403;
    return <EmptyState icon="shield" title={forbidden ? "Bu analizi göremezsiniz" : "Analiz yüklenemedi"} description={forbidden ? "Yalnızca kendi analizinizi görebilirsiniz." : error.message} />;
  }
  if (isLoading || !data) return <div className="app-card h-96 animate-pulse" aria-label="Yükleniyor" />;

  const visible = trimLeadingMonths(data.months, (month) => monthTotal(month) > 0 || month.paid > 0);
  const view: TeacherAnalysis = { ...data, months: visible };
  const periods = visible.map((month) => month.period);
  const selected = Math.max(0, selectedPeriod ? periods.indexOf(selectedPeriod) : periods.length - 1);
  const maxLessons = Math.max(1, ...visible.map(monthTotal));
  const hasLevel = visible.some((month) => month.averageDifficulty !== null);
  const hasPaid = visible.some((month) => month.paid > 0);
  const hidden = [!hasLevel && "eser zorluğu (ders notuna zorluk girildikçe)", !hasPaid && "ödenen (ödeme yapıldıkça)"].filter(Boolean);
  const isAdmin = me?.role === "Admin";

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <nav aria-label="Konum" className="flex items-center gap-2 text-[.82rem] font-semibold text-[var(--muted)]">
          {isAdmin ? <Link href="/dashboard/teachers" className="hover:text-[var(--brand)]">Öğretmenler</Link> : <span>Öğretmen</span>}
          <span aria-hidden>/</span>
          <span className="text-[var(--foreground)]">Analiz</span>
        </nav>
        <PeriodPicker value={months} onChange={(value) => { setMonths(value); setSelectedPeriod(null); }} />
      </div>

      <TeacherHero data={view} trimmed={visible.length < data.months.length} />

      <AnalysisCard title="Ay ay" action={<StateLegend states={["Present", "Makeup", "Excused", "Absent", "Unmarked", "Cancelled", "Upcoming"]} />}>
        <Strip periods={periods} selected={selected} onSelect={(index) => setSelectedPeriod(periods[index]!)} caption="Aylık öğretmen şeridi">
          <StripRow label="Dersler" hint="durumlarına göre" periods={periods} selected={selected}>
            {(index) => <LessonStack month={visible[index]!} max={maxLessons} />}
          </StripRow>
          <StripRow label="Öğrenci devamı" periods={periods} selected={selected}>
            {(index) => {
              const month = visible[index]!;
              return month.attendanceRate === null
                ? <EmptyCell label="Yoklama yok" />
                : <span className={`text-sm font-extrabold tabular-nums ${rateTone(month.attendanceRate)}`}>%{month.attendanceRate}</span>;
            }}
          </StripRow>
          <StripRow label="Ders notu" hint="gelinen derslerde" periods={periods} selected={selected}>
            {(index) => {
              const month = visible[index]!;
              return month.present + month.makeup
                ? <MiniBar tone="brand" percent={month.noteCoverage} label={`%${month.noteCoverage ?? 0}`} />
                : <EmptyCell label="Yapılan ders yok" />;
            }}
          </StripRow>
          <StripRow label="Öğrenci" hint="ders gören" periods={periods} selected={selected}>
            {(index) => visible[index]!.activeStudents
              ? <span className="text-sm font-extrabold tabular-nums">{visible[index]!.activeStudents}</span>
              : <EmptyCell label="Öğrenci yok" />}
          </StripRow>
          {hasLevel && <LevelRow label="Eser zorluğu" hint="1–5" values={visible.map((month) => month.averageDifficulty)} periods={periods} selected={selected} format={formatDecimal} />}
          {hasPaid && (
            <StripRow label="Ödenen" hint="haftalık + grup" periods={periods} selected={selected}>
              {(index) => {
                const paid = visible[index]!.paid;
                return paid ? <span className="text-xs font-extrabold tabular-nums">{formatMoney(paid, data.currency)}</span> : <EmptyCell label="Ödeme yok" />;
              }}
            </StripRow>
          )}
        </Strip>
        {hidden.length > 0 && <StripFootnote>Henüz veri olmadığı için gösterilmeyen satırlar: {hidden.join(", ")}.</StripFootnote>}
        <MonthDetail month={visible[selected]!} currency={data.currency} />
      </AnalysisCard>

      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <StudentsCard data={view} />
        <WeekdayCard data={view} />
      </div>
    </div>
  );
}

function TeacherHero({ data, trimmed }: { data: TeacherAnalysis; trimmed: boolean }) {
  const terms = useBranchTerms();
  const months = data.months;
  const attended = sum(months, (month) => month.present + month.makeup);
  const rated = sum(months, (month) => month.present + month.makeup + month.excused + month.absent);
  const unmarked = sum(months, (month) => month.unmarked);
  const attendance = rate(attended, rated);
  const marking = rate(rated, rated + unmarked);
  const notes = rate(sum(months, (month) => month.notedLessons), attended);
  const levels = months.map((month) => month.averageDifficulty).filter((value): value is number => value !== null);
  const level = levels.length ? levels.reduce((a, b) => a + b, 0) / levels.length : null;
  const paid = sum(months, (month) => month.paid);
  const cancelled = sum(months, (month) => month.cancelled);
  const initials = data.fullName.split(" ").map((part) => part[0]).slice(0, 2).join("");

  const parts = [
    `${trimmed ? "İlk dersinden bu yana" : `Son ${months.length} ayda`} ${attended} ders yaptı${cancelled ? `, ${cancelled} ders iptal oldu` : ""}`,
    attendance === null ? "öğrenci devamı için yoklama yok" : `öğrencilerinin devamı %${attendance}`,
    unmarked ? `${unmarked} geçmiş dersin yoklaması girilmedi` : "geçmiş derslerin hepsinin yoklaması girildi",
    paid ? `bu dönemde ${formatMoney(paid, data.currency)} ödendi` : null,
  ].filter(Boolean);

  return (
    <AnalysisHero summary={`${parts.join("; ")}.`}>
      <div className="flex flex-wrap items-center gap-6">
        <div className="flex min-w-0 flex-[1_1_20rem] items-center gap-4">
          <div aria-hidden className="grid h-[72px] w-[72px] shrink-0 place-items-center rounded-full border-[1.5px] border-[#6b5444] font-serif text-2xl font-semibold text-[#f0a273]">{initials}</div>
          <div className="min-w-0 space-y-1.5">
            <h1 className="font-serif text-[1.9rem] font-semibold leading-tight tracking-[-0.015em]">{data.fullName}</h1>
            <p className="text-[.82rem] font-medium text-[#c9b6a3]">{data.instruments.join(", ") || `${terms.branch} atanmadı`}{data.status === "Inactive" && " · Pasif"}</p>
            <div className="flex flex-wrap gap-1.5 pt-0.5">
              <HeroChip>{data.activeStudentCount} aktif öğrenci</HeroChip>
              {unmarked > 0 && <HeroChip tone="warn">{unmarked} yoklama bekliyor</HeroChip>}
            </div>
          </div>
        </div>
        <div className="grid flex-[1_1_30rem] grid-cols-2 gap-3.5 sm:grid-cols-[repeat(auto-fit,minmax(150px,1fr))]">
          <Gauge label="Öğrenci devamı" value={attendance === null ? "—" : `%${attendance}`} hint={`${attended} / ${rated} ders`} percent={attendance} color="#86c98d" />
          <Gauge label="Yoklama" value={marking === null ? "—" : `%${marking}`} hint={unmarked ? `${unmarked} ders eksik` : "eksik yok"} percent={marking} color={unmarked ? "#f2c14e" : "#86c98d"} />
          <Gauge label="Ders notu" value={notes === null ? "—" : `%${notes}`} hint="gelinen derslerde" percent={notes} color="#e9d8c4" />
          {level !== null && <Gauge label="Eser zorluğu" value={formatDecimal(level)} hint={levelName(level)} percent={((level - 1) / 4) * 100} color="#f0a273" />}
        </div>
      </div>
    </AnalysisHero>
  );
}

function LessonStack({ month, max }: { month: TeacherAnalysisMonth; max: number }) {
  const total = monthTotal(month);
  if (!total) return <EmptyCell label="Ders yok" />;
  return (
    <div className="flex h-[5.5rem] w-full flex-col items-center justify-end gap-1">
      <span className="text-[.7rem] font-extrabold tabular-nums">{total}</span>
      <div
        className="flex w-full max-w-9 flex-col-reverse overflow-hidden rounded-t-lg rounded-b-sm"
        style={{ height: `${Math.max(6, (total / max) * 64)}px` }}
        role="img"
        aria-label={STACK.filter((item) => item.pick(month)).map((item) => `${STATE_STYLE[item.state].label} ${item.pick(month)}`).join(", ")}
      >
        {STACK.filter((item) => item.pick(month) > 0).map((item) => (
          <span key={item.state} title={`${STATE_STYLE[item.state].label}: ${item.pick(month)}`} style={{ flexGrow: item.pick(month), ...STATE_STYLE[item.state].dot }} />
        ))}
      </div>
    </div>
  );
}

function MonthDetail({ month, currency }: { month: TeacherAnalysisMonth; currency: string }) {
  const attended = month.present + month.makeup;
  return (
    <div className="mt-1 flex flex-wrap items-start gap-x-7 gap-y-4 rounded-2xl bg-[#fbf4eb] px-5 py-4">
      <div className="min-w-36">
        <p className="text-micro text-[var(--muted)]">Seçili ay</p>
        <p className="font-serif text-xl font-semibold capitalize">{longMonth(month.period)}</p>
      </div>
      <div className="grid flex-[1_1_22rem] grid-cols-2 gap-3 sm:grid-cols-5">
        <StatBlock value={attended} label={month.makeup ? `ders · ${month.makeup} telafi` : "ders yapıldı"} />
        <StatBlock value={month.excused + month.absent} label={`kaçan · ${month.absent} habersiz`} />
        <StatBlock value={month.cancelled} label="iptal" />
        <StatBlock value={month.unmarked} label="yoklamasız" tone={month.unmarked ? "text-[var(--warning-strong)]" : undefined} />
        <StatBlock value={month.paid ? formatMoney(month.paid, currency) : "—"} label="ödenen" />
      </div>
    </div>
  );
}

function StudentsCard({ data }: { data: TeacherAnalysis }) {
  return (
    <AnalysisCard title="Öğrenciler" action={<span className="text-meta">Devamı düşük olan üstte</span>}>
      {data.students.length === 0 && <p className="text-meta">Seçili dönemde öğrenci yok.</p>}
      {data.students.length > 0 && (
        <div className="-mx-1 overflow-x-auto px-1">
          <table className="w-full min-w-[34rem] border-collapse text-[.82rem]">
            <thead>
              <tr className="text-left text-[.7rem] uppercase tracking-[.06em] text-[var(--muted)]">
                <th scope="col" className="py-1.5 pr-2 font-extrabold">Öğrenci</th>
                <th scope="col" className="w-40 py-1.5 pr-2 font-extrabold">Devam</th>
                <th scope="col" className="py-1.5 pr-2 text-right font-extrabold">Ders</th>
                <th scope="col" className="py-1.5 pr-2 text-right font-extrabold">Zorluk</th>
                <th scope="col" className="py-1.5 text-right font-extrabold">Son not</th>
              </tr>
            </thead>
            <tbody>
              {data.students.map((student) => (
                <tr key={student.studentId} className="border-t border-[#f1e7da]">
                  <td className="py-2 pr-2">
                    <Link href={`/dashboard/students/analysis?studentId=${student.studentId}`} className="font-bold hover:text-[var(--brand)]">{student.name}</Link>
                    <p className="text-[.7rem] font-semibold text-[var(--muted)]">{student.instruments}{student.unmarked ? ` · ${student.unmarked} yoklamasız` : ""}</p>
                  </td>
                  <td className="py-2 pr-2">
                    {student.attendanceRate === null
                      ? <span className="text-[var(--muted)]">—</span>
                      : <div className="flex items-center gap-2">
                          <span className="h-1.5 flex-1 overflow-hidden rounded-full bg-[#f1e7da]">
                            <span className="block h-full rounded-full" style={{ width: `${student.attendanceRate}%`, background: student.attendanceRate < 85 ? "#d69a2d" : "var(--success)" }} />
                          </span>
                          <span className={`w-9 text-right font-extrabold tabular-nums ${rateTone(student.attendanceRate)}`}>%{student.attendanceRate}</span>
                        </div>}
                  </td>
                  <td className="py-2 pr-2 text-right font-bold tabular-nums">{student.attended}/{student.rated}</td>
                  <td className="py-2 pr-2 text-right font-semibold tabular-nums">{formatDecimal(student.averageDifficulty)}</td>
                  <td className="py-2 text-right font-semibold text-[var(--muted)]">{student.lastNoteAt ? formatDate(student.lastNoteAt) : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </AnalysisCard>
  );
}

function WeekdayCard({ data }: { data: TeacherAnalysis }) {
  const max = Math.max(1, ...data.weekdays.map((day) => day.lessons));
  return (
    <AnalysisCard title="Haftalık yük">
      {data.weekdays.length === 0 && <p className="text-meta">Seçili dönemde ders yok.</p>}
      {data.weekdays.map((day) => {
        const dayRate = rate(day.attended, day.rated);
        return (
          <div key={day.day} className="space-y-1.5">
            <div className="flex justify-between text-[.82rem] font-bold">
              <span>{WEEKDAY_NAMES[day.day]}</span>
              <span className="tabular-nums text-[var(--muted)]">{day.lessons} ders{dayRate !== null && ` · devam %${dayRate}`}</span>
            </div>
            <span className="block h-2.5 overflow-hidden rounded-full bg-[#f1e7da]">
              <span className="block h-full rounded-full bg-[var(--brand)]" style={{ width: `${(day.lessons / max) * 100}%` }} />
            </span>
          </div>
        );
      })}
    </AnalysisCard>
  );
}
