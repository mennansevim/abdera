"use client";

import Link from "next/link";
import { Suspense, useState } from "react";
import { useSearchParams } from "next/navigation";
import {
  AnalysisCard, AnalysisHero, EmptyCell, Gauge, HeroChip, LevelRow, MiniBar, PeriodPicker, StateLegend, StatBlock,
  STATE_STYLE, STICKY_LABEL, Strip, StripFootnote, StripRow, monthColumns, rateTone,
} from "@/components/analysis";
import { Icon } from "@/components/icons";
import { EmptyState } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  WEEKDAY_NAMES, formatDecimal, levelName, longMonth, rate, sum, trimLeadingMonths, useStudentAnalysis,
  type AnalysisReceivableStatus, type PeriodOption, type StudentAnalysis, type StudentAnalysisMonth,
} from "@/lib/analysis";
import { formatMoney } from "@/lib/billing-format";

export default function StudentAnalysisPage() {
  return (
    <Suspense>
      <StudentAnalysisContent />
    </Suspense>
  );
}

const BILL_STYLE: Record<AnalysisReceivableStatus, { label: string; className: string }> = {
  Paid: { label: "Ödendi", className: "bg-[var(--success-soft)] text-[var(--success-strong)]" },
  Partial: { label: "Kısmi", className: "bg-[var(--warning-soft)] text-[var(--warning-strong)]" },
  Unpaid: { label: "Bekliyor", className: "bg-[var(--surface-muted)] text-[var(--muted)]" },
  Overdue: { label: "Gecikmiş", className: "bg-[var(--danger-soft)] text-[var(--danger-strong)]" },
  Cancelled: { label: "İptal", className: "bg-[var(--surface-muted)] text-[var(--muted)]" },
};

const DIFFICULTY_STYLE: Record<number, string> = {
  1: "bg-[#f9e7d8] text-[#5a3018]",
  2: "bg-[#f6d9c2] text-[#5a3018]",
  3: "bg-[#e3935f] text-[#2b1f18]",
  4: "bg-[#a84e1f] text-white",
  5: "bg-[#6e2f10] text-white",
};

function ageOf(birthDate: string) {
  const birth = new Date(`${birthDate}T00:00:00`);
  const now = new Date();
  let age = now.getFullYear() - birth.getFullYear();
  if (now.getMonth() < birth.getMonth() || (now.getMonth() === birth.getMonth() && now.getDate() < birth.getDate())) age -= 1;
  return age;
}

function formatDate(value: string, withYear = true) {
  return new Date(value.length === 10 ? `${value}T00:00:00` : value)
    .toLocaleDateString("tr-TR", { day: "numeric", month: "long", ...(withYear ? { year: "numeric" } : {}) });
}

function StudentAnalysisContent() {
  const studentId = useSearchParams().get("studentId");
  const [months, setMonths] = useState<PeriodOption>("6");
  const [selectedPeriod, setSelectedPeriod] = useState<string | null>(null);
  const { data, isLoading, error } = useStudentAnalysis(studentId, Number(months));

  if (!studentId) return <EmptyState icon="students" title="Öğrenci seçilmedi" description="Öğrenciler listesinden bir öğrencinin analizini açın." />;
  if (error) {
    const forbidden = error instanceof ApiError && error.status === 403;
    return <EmptyState icon="shield" title={forbidden ? "Bu öğrencinin analizini göremezsiniz" : "Analiz yüklenemedi"} description={forbidden ? "Öğrenci size atanmamış." : error.message} />;
  }
  if (isLoading || !data) return <div className="app-card h-96 animate-pulse" aria-label="Yükleniyor" />;

  const visible = trimLeadingMonths(data.months, (month) => month.lessons.length > 0 || month.billing !== null || month.noteCount > 0, data.enrolledSince);
  const view: StudentAnalysis = { ...data, months: visible };
  const trimmed = visible.length < data.months.length;
  const periods = visible.map((month) => month.period);
  const selected = Math.max(0, selectedPeriod ? periods.indexOf(selectedPeriod) : periods.length - 1);
  const hasLevel = visible.some((month) => month.averageDifficulty !== null);
  const hasAssignments = visible.some((month) => month.assignmentCount > 0);
  const hasBilling = data.billingVisible && visible.some((month) => month.billing !== null);
  const hidden = [!hasLevel && "seviye (ders notuna eser zorluğu girildikçe)", !hasAssignments && "ödev (derste ödev verildikçe)"].filter(Boolean);

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <nav aria-label="Konum" className="flex items-center gap-2 text-[.82rem] font-semibold text-[var(--muted)]">
          <Link href="/dashboard/students" className="hover:text-[var(--brand)]">Öğrenciler</Link>
          <span aria-hidden>/</span>
          <span className="text-[var(--foreground)]">Analiz</span>
        </nav>
        <div className="flex flex-wrap items-center gap-2">
          <PeriodPicker value={months} onChange={(value) => { setMonths(value); setSelectedPeriod(null); }} />
          <Link href={`/dashboard/progress?studentId=${data.studentId}`} className="btn btn-quiet"><Icon name="activity" className="h-4 w-4" /> Ders notları</Link>
        </div>
      </div>

      <StudentHero data={view} trimmed={trimmed} />

      <AnalysisCard
        title="Ay ay"
        action={<StateLegend states={["Present", "Makeup", "Excused", "Absent", "Cancelled", "Unmarked", "Upcoming"]} />}
      >
        <Strip periods={periods} selected={selected} onSelect={(index) => setSelectedPeriod(periods[index]!)} caption="Aylık öğrenci şeridi">
          <StripRow label="Dersler" periods={periods} selected={selected}>
            {(index) => <LessonDots month={visible[index]!} />}
          </StripRow>
          {hasLevel && <LevelRow label="Seviye" hint="eser zorluğu, 1–5" values={visible.map((month) => month.averageDifficulty)} periods={periods} selected={selected} format={formatDecimal} />}
          {hasAssignments && (
            <StripRow label="Ödev" hint="tamamlanan" periods={periods} selected={selected}>
              {(index) => {
                const month = visible[index]!;
                return month.assignmentCount
                  ? <MiniBar percent={month.assignmentRate} label={`${month.assignmentCompleted}/${month.assignmentCount}`} />
                  : <EmptyCell label="Ödev verilmedi" />;
              }}
            </StripRow>
          )}
          {hasBilling && (
            <StripRow label="Aidat" periods={periods} selected={selected}>
              {(index) => <BillingCell month={visible[index]!} />}
            </StripRow>
          )}
          <PieceRows data={view} periods={periods} />
        </Strip>
        {hidden.length > 0 && <StripFootnote>Henüz veri olmadığı için gösterilmeyen satırlar: {hidden.join(", ")}.</StripFootnote>}
        <MonthDetail month={visible[selected]!} billingVisible={hasBilling} showLevel={hasLevel} showAssignments={hasAssignments} />
      </AnalysisCard>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <WeekdayCard data={view} />
        <ProfileCard data={data} />
        {data.billingVisible && <AccountCard data={view} />}
      </div>
    </div>
  );
}

function StudentHero({ data, trimmed }: { data: StudentAnalysis; trimmed: boolean }) {
  const months = data.months;
  const attended = sum(months, (month) => month.attended);
  const rated = sum(months, (month) => month.rated);
  const attendance = rate(attended, rated);
  const levels = months.map((month) => month.averageDifficulty).filter((value): value is number => value !== null);
  const level = levels.at(-1) ?? null;
  const levelDelta = levels.length > 1 ? levels.at(-1)! - levels[0]! : null;
  const assignmentCount = sum(months, (month) => month.assignmentCount);
  const assignmentRate = rate(sum(months, (month) => month.assignmentCompleted), assignmentCount);
  const billed = sum(months, (month) => month.billing?.amount ?? 0);
  const paid = sum(months, (month) => month.billing?.paid ?? 0);
  const payRate = rate(paid, billed);
  const currency = months.find((month) => month.billing)?.billing?.currency ?? "TRY";
  const activeCourses = data.courses.filter((course) => course.status === "Active");
  const initials = data.fullName.split(" ").map((part) => part[0]).slice(0, 2).join("");

  const parts: string[] = [];
  parts.push(attendance === null ? "Bu dönemde yoklaması girilmiş ders yok" : `${trimmed ? "Kayıttan bu yana" : `Son ${months.length} ayda`} derslerin %${attendance}'ine geldi`);
  if (level !== null) parts.push(levelDelta !== null && Math.abs(levelDelta) >= 0.1
    ? `çalıştığı eserlerin zorluğu ${formatDecimal(levels[0]!)} → ${formatDecimal(level)}`
    : `eser zorluğu ${formatDecimal(level)} (${levelName(level)})`);
  if (data.billingVisible && data.openBalance !== null) parts.push(data.openBalance > 0 ? `açık bakiye ${formatMoney(data.openBalance, currency)}` : "açık bakiyesi yok");

  return (
    <AnalysisHero summary={`${parts.join("; ")}.`}>
      <div className="flex flex-wrap items-center gap-6">
        <div className="flex min-w-0 flex-[1_1_20rem] items-center gap-4">
          <div aria-hidden className="grid h-[72px] w-[72px] shrink-0 place-items-center rounded-full border-[1.5px] border-[#6b5444] font-serif text-2xl font-semibold text-[#f0a273]">{initials}</div>
          <div className="min-w-0 space-y-1.5">
            <h1 className="font-serif text-[1.9rem] font-semibold leading-tight tracking-[-0.015em]">{data.fullName}</h1>
            <p className="text-[.82rem] font-medium text-[#c9b6a3]">
              {ageOf(data.birthDate)} yaş
              {data.enrolledSince && ` · ${formatDate(data.enrolledSince)}'den beri öğrenci`}
              {data.status === "Inactive" && " · Pasif"}
            </p>
            <div className="flex flex-wrap gap-1.5 pt-0.5">
              {activeCourses.map((course) => (
                <HeroChip key={course.enrollmentId}>{course.instrumentName} · {course.courseKind === "Group" ? "Grup" : "Birebir"} · {course.teacherName}</HeroChip>
              ))}
              {data.siblingDiscount && <HeroChip>Kardeş indirimi</HeroChip>}
              {data.availableMakeupCredits > 0 && (
                <HeroChip tone="good">{data.availableMakeupCredits} telafi hakkı{data.nextMakeupCreditExpiry && ` · ${formatDate(data.nextMakeupCreditExpiry, false)}`}</HeroChip>
              )}
            </div>
          </div>
        </div>
        <div className="grid flex-[1_1_30rem] grid-cols-2 gap-3.5 sm:grid-cols-[repeat(auto-fit,minmax(150px,1fr))]">
          <Gauge label="Devam" value={attendance === null ? "—" : `%${attendance}`} hint={`${attended} / ${rated} ders`} percent={attendance} color="#86c98d" />
          {level !== null && <Gauge label="Seviye" value={formatDecimal(level)} hint={level === null ? "ders notu yok" : `${levelName(level)}${levelDelta !== null && Math.abs(levelDelta) >= 0.05 ? ` · ${levelDelta > 0 ? "+" : ""}${formatDecimal(levelDelta)}` : ""}`} percent={level === null ? null : ((level - 1) / 4) * 100} color="#f0a273" />}
          {assignmentCount > 0 && <Gauge label="Ödev" value={assignmentRate === null ? "—" : `%${assignmentRate}`} hint={assignmentCount ? `${assignmentCount} ödevden` : "ödev verilmedi"} percent={assignmentRate} color="#e9d8c4" />}
          {data.billingVisible && (
            <Gauge label="Ödeme" value={payRate === null ? "—" : `%${payRate}`} hint={billed ? `${formatMoney(paid, currency)} / ${formatMoney(billed, currency)}` : "aidat yok"} percent={payRate} color={payRate !== null && payRate < 100 ? "#f2c14e" : "#86c98d"} />
          )}
        </div>
      </div>
    </AnalysisHero>
  );
}

function LessonDots({ month }: { month: StudentAnalysisMonth }) {
  if (!month.lessons.length) return <EmptyCell label="Ders yok" />;
  return (
    <>
      <div className="flex max-w-[5.5rem] flex-wrap justify-center gap-1">
        {month.lessons.map((lesson) => (
          <span
            key={lesson.startAt}
            title={`${formatDate(lesson.startAt, false)} · ${STATE_STYLE[lesson.state].label} · ${lesson.teacherName}`}
            className="h-2.5 w-2.5 rounded-full"
            style={STATE_STYLE[lesson.state].dot}
          />
        ))}
      </div>
      {month.rated > 0 && (
        <span title={`Devam %${month.attendanceRate}`} className={`text-xs font-extrabold tabular-nums ${rateTone(month.attendanceRate)}`}>{month.attended}/{month.rated}</span>
      )}
    </>
  );
}

function BillingCell({ month }: { month: StudentAnalysisMonth }) {
  if (!month.billing) return <EmptyCell label="Aidat yok" />;
  const style = BILL_STYLE[month.billing.status];
  return (
    <span title={`${formatMoney(month.billing.amount, month.billing.currency)} · ${style.label}`} className={`inline-flex flex-col items-center rounded-lg px-2.5 py-1 leading-tight ${style.className}`}>
      <span className="text-[.75rem] font-extrabold tabular-nums">{formatMoney(month.billing.amount, month.billing.currency)}</span>
      <span className="text-[.65rem] font-bold">{style.label}</span>
    </span>
  );
}

function PieceRows({ data, periods }: { data: StudentAnalysis; periods: string[] }) {
  if (!data.pieces.length) return null;
  const last = periods.length - 1;
  return (
    <>
      <div className="grid grid-cols-[6rem_minmax(0,1fr)] border-t border-[#f3ebe0] pt-2 sm:grid-cols-[7.5rem_minmax(0,1fr)]">
        <span className={`py-1.5 text-[.78rem] font-bold ${STICKY_LABEL}`}>Repertuvar</span>
      </div>
      {data.pieces.map((piece) => {
        const start = Math.max(0, periods.indexOf(piece.firstPeriod));
        const endIndex = periods.indexOf(piece.lastPeriod);
        const end = endIndex < 0 ? last : endIndex;
        const ongoing = !piece.finished && end >= last - 1;
        const tone = DIFFICULTY_STYLE[piece.difficulty ?? 2] ?? DIFFICULTY_STYLE[2];
        return (
          <div key={piece.title} className="grid grid-cols-[6rem_minmax(0,1fr)] items-center sm:grid-cols-[7.5rem_minmax(0,1fr)]">
            <div className={`min-w-0 py-1 pr-2.5 ${STICKY_LABEL}`}>
              <p className="truncate text-xs font-bold">{piece.title}</p>
              {piece.composer && <p className="truncate text-[.7rem] font-semibold text-[#9a8573]">{piece.composer}</p>}
            </div>
            <div className="grid py-1" style={monthColumns(periods.length)}>
              <div
                title={`${piece.title} · ${levelName(piece.difficulty)}`}
                className={`mx-1 flex h-[26px] items-center justify-between gap-1.5 overflow-hidden whitespace-nowrap px-2.5 text-[.7rem] font-extrabold ${tone} ${ongoing ? "rounded-l-lg rounded-r-sm" : "rounded-lg"}`}
                style={{ gridColumn: `${start + 1} / ${end + 2}` }}
              >
                <span>{levelName(piece.difficulty)}</span>
                {end > start && <span>{piece.finished ? "Bitti" : ongoing ? "Sürüyor" : ""}</span>}
              </div>
            </div>
          </div>
        );
      })}
    </>
  );
}

function MonthDetail({ month, billingVisible, showLevel, showAssignments }: { month: StudentAnalysisMonth; billingVisible: boolean; showLevel: boolean; showAssignments: boolean }) {
  const extra = [month.cancelled && `${month.cancelled} iptal`, month.unmarked && `${month.unmarked} yoklamasız`].filter(Boolean).join(" · ");
  return (
    <div className="mt-1 flex flex-wrap items-start gap-x-7 gap-y-4 rounded-2xl bg-[#fbf4eb] px-5 py-4">
      <div className="min-w-36">
        <p className="text-micro text-[var(--muted)]">Seçili ay</p>
        <p className="font-serif text-xl font-semibold capitalize">{longMonth(month.period)}</p>
      </div>
      <div className="grid flex-[1_1_22rem] grid-cols-2 gap-3 sm:grid-cols-4">
        <StatBlock value={`${month.attended} / ${month.rated}`} label={extra ? `ders · ${extra}` : "derse geldi"} />
        {showLevel && <StatBlock value={formatDecimal(month.averageDifficulty)} label="ort. eser zorluğu" />}
        {showAssignments && <StatBlock value={month.assignmentRate === null ? "—" : `%${month.assignmentRate}`} label="ödev tamamlama" />}
        {billingVisible && (month.billing
          ? <StatBlock value={BILL_STYLE[month.billing.status].label} tone={BILL_STYLE[month.billing.status].className.split(" ")[1]}
              label={`${formatMoney(month.billing.amount, month.billing.currency)}${month.billing.lastPaymentDate ? ` · ${formatDate(month.billing.lastPaymentDate, false)}` : ` · vade ${formatDate(month.billing.dueDate, false)}`}`} />
          : <StatBlock value="—" label="aidat yok" />)}
      </div>
      {month.lastNote && (
        <blockquote className="flex w-full flex-col gap-1 border-t border-[#ecdcc8] pt-3">
          {month.lastNote.text && <p className="font-serif text-[.95rem] italic text-[#3d3027]">“{month.lastNote.text}”</p>}
          <footer className="text-xs font-bold text-[var(--brand-strong)]">
            {month.lastNote.teacherName} · {formatDate(month.lastNote.at, false)}{month.lastNote.nextGoal && ` · Sonraki hedef: ${month.lastNote.nextGoal}`}
          </footer>
        </blockquote>
      )}
    </div>
  );
}

function WeekdayCard({ data }: { data: StudentAnalysis }) {
  return (
    <AnalysisCard title="Devam alışkanlığı">
      {data.weekdays.length === 0 && <p className="text-meta">Seçili dönemde yoklaması girilmiş ders yok.</p>}
      {data.weekdays.map((day) => {
        const total = day.present + day.makeup + day.excused + day.absent;
        const segments = [
          { key: "Present", value: day.present },
          { key: "Makeup", value: day.makeup },
          { key: "Excused", value: day.excused },
          { key: "Absent", value: day.absent },
        ] as const;
        return (
          <div key={day.day} className="space-y-1.5">
            <div className="flex justify-between text-[.82rem] font-bold">
              <span>{WEEKDAY_NAMES[day.day]}</span>
              <span className="text-[var(--muted)] tabular-nums">{total} ders · %{rate(day.present + day.makeup, total)}</span>
            </div>
            <div className="flex h-3 overflow-hidden rounded-full bg-[#f1e7da]" role="img" aria-label={segments.map((s) => `${STATE_STYLE[s.key].label} ${s.value}`).join(", ")}>
              {segments.filter((s) => s.value > 0).map((s) => (
                <span key={s.key} style={{ width: `${(s.value / total) * 100}%`, background: STATE_STYLE[s.key].dot.background }} />
              ))}
            </div>
          </div>
        );
      })}
    </AnalysisCard>
  );
}

function ProfileCard({ data }: { data: StudentAnalysis }) {
  const rows: { k: string; v: string }[] = [
    { k: "Doğum tarihi", v: formatDate(data.birthDate) },
    ...(data.enrolledSince ? [{ k: "İlk kayıt", v: formatDate(data.enrolledSince) }] : []),
    ...data.guardians.flatMap((guardian) => [
      { k: guardian.relationship ? `Veli (${guardian.relationship})` : "Veli", v: guardian.name },
      { k: "Telefon", v: guardian.phoneNumber },
      { k: "Bildirim rızası", v: guardian.notificationConsent ? "Açık" : "Kapalı" },
    ]),
    ...data.courses.filter((course) => course.status !== "Active").map((course) => ({ k: "Geçmiş kurs", v: `${course.instrumentName} · ${course.teacherName}` })),
  ];
  return (
    <AnalysisCard title="Künye" action={<Link href="/dashboard/students" className="text-[.82rem] font-bold text-[var(--brand-strong)]">Öğrencilerde aç</Link>}>
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2 text-[.82rem]">
        {rows.map((row, index) => (
          <div key={`${row.k}-${index}`} className="contents">
            <dt className="font-semibold text-[var(--muted)]">{row.k}</dt>
            <dd className="truncate text-right font-bold">{row.v}</dd>
          </div>
        ))}
      </dl>
    </AnalysisCard>
  );
}

function AccountCard({ data }: { data: StudentAnalysis }) {
  const currency = data.months.find((month) => month.billing)?.billing?.currency ?? "TRY";
  const open = data.months.filter((month) => month.billing && month.billing.status !== "Paid" && month.billing.amount > month.billing.paid);
  const collected = sum(data.months, (month) => month.billing?.paid ?? 0);
  return (
    <AnalysisCard title="Hesap durumu" action={<Link href="/dashboard/billing" className="text-[.82rem] font-bold text-[var(--brand-strong)]">Aidatlarda aç</Link>}>
      <div className="flex items-baseline gap-2">
        <span className={`text-3xl font-extrabold tabular-nums tracking-[-0.02em] ${data.openBalance ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>{formatMoney(data.openBalance ?? 0, currency)}</span>
        <span className="text-meta">açık bakiye (tüm zamanlar)</span>
      </div>
      <ul className="space-y-2 text-[.82rem]">
        {open.map((month) => (
          <li key={month.period} className="flex justify-between gap-3 font-semibold">
            <span className="capitalize">{longMonth(month.period)} · {BILL_STYLE[month.billing!.status].label.toLocaleLowerCase("tr-TR")}</span>
            <span className="font-extrabold tabular-nums">{formatMoney(month.billing!.amount - month.billing!.paid, currency)}</span>
          </li>
        ))}
        <li className="flex justify-between gap-3 font-semibold text-[var(--muted)]">
          <span>Seçili dönemde tahsil edilen</span>
          <span className="font-extrabold tabular-nums">{formatMoney(collected, currency)}</span>
        </li>
      </ul>
      <p className="text-meta mt-auto">Tahsilat Aidatlar ekranındaki ödeme penceresinden yapılır.</p>
    </AnalysisCard>
  );
}

