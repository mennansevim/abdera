"use client";

import { useEffect, useMemo, useState } from "react";
import { Badge, Modal } from "@/components/ui";
import { useBillingDues, useTuitionRates } from "@/lib/billing";
import { useInstruments, useTeachers } from "@/lib/people";
import { buildRevenuePlan, DEFAULT_ASSUMPTIONS, monthName, type PlanAssumptions, type PlanStep } from "@/lib/revenue-plan";
import { useTeacherPayoutWeek } from "@/lib/teacher-payouts";

// Deneysel "Hedef belirle" penceresi: yönetici aylık gelir hedefini girer, planlayıcı
// (lib/revenue-plan.ts) okulun kendi verisinden adım adım yol haritası önerir. Hiçbir şey
// kaydetmez; hedef ve varsayımlar yalnızca bu tarayıcıda hatırlanır (kişisel kolaylık).

const STORAGE_KEY = "abdera.revenue-target.v1";
const tl = (value: number) => `₺${Math.round(value).toLocaleString("tr-TR")}`;
const STEP_TONE: Record<PlanStep["key"], string> = {
  collection: "var(--success)",
  discount: "#b7791f",
  capacity: "var(--brand)",
  price: "#3f6fb0",
  growth: "#9b3f6b",
};

interface Stored { target: number; assumptions: PlanAssumptions }

function readStored(): Stored | null {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) as Stored : null;
  } catch {
    return null;
  }
}

export function RevenueTargetModal({ open, onClose, currentMonth, monthlyExpense }: { open: boolean; onClose: () => void; currentMonth: string; monthlyExpense: number }) {
  return (
    <Modal open={open} title="Hedef gelir planı" description="Aylık hedefini gir; okulun kendi verisinden adım adım yol haritası çıkarılır." onClose={onClose} size="lg">
      <TargetPlanner currentMonth={currentMonth} monthlyExpense={monthlyExpense} />
    </Modal>
  );
}

// Modal yalnızca açıkken çocuğunu çizer: sorgular ve depodan okuma pencere açılınca başlar.
function TargetPlanner({ currentMonth, monthlyExpense }: { currentMonth: string; monthlyExpense: number }) {
  const { data: dues, isLoading: duesLoading } = useBillingDues();
  const { data: rates, isLoading: ratesLoading } = useTuitionRates();
  const { data: teachers, isLoading: teachersLoading } = useTeachers();
  const { data: instruments } = useInstruments();
  const { data: payoutWeek } = useTeacherPayoutWeek(null);

  const [target, setTarget] = useState<number | null>(() => readStored()?.target ?? null);
  const [assumptions, setAssumptions] = useState<PlanAssumptions>(() => ({ ...DEFAULT_ASSUMPTIONS, ...readStored()?.assumptions }));
  const [showAssumptions, setShowAssumptions] = useState(false);

  useEffect(() => {
    if (target === null) return;
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify({ target, assumptions }));
    } catch {
      // Gizli pencere / kapalı depolama: hedef bu oturumda kalır, sorun değil.
    }
  }, [target, assumptions]);

  const teacherRates = useMemo(
    () => Object.fromEntries((payoutWeek?.teachers ?? []).map((row) => [row.teacherId, row.ratePerLesson])),
    [payoutWeek],
  );
  const loading = duesLoading || ratesLoading || teachersLoading;

  const baselinePlan = useMemo(() => {
    if (loading || !dues || !rates || !teachers) return null;
    return buildRevenuePlan({ dues, currentMonth, rates, teachers, instruments: instruments ?? [], teacherRates, monthlyExpense, target: 0, assumptions });
  }, [loading, dues, rates, teachers, instruments, teacherRates, monthlyExpense, assumptions, currentMonth]);

  // Hedef girilmemişse bugünkü gelirin %20 fazlası önerilir (yuvarlak bir sayı).
  const suggested = baselinePlan ? Math.ceil((baselinePlan.baseline.expectedCash * 1.2) / 5000) * 5000 : 0;
  const effectiveTarget = target ?? suggested;

  const plan = useMemo(() => {
    if (!baselinePlan || !dues || !rates || !teachers || effectiveTarget <= 0) return null;
    return buildRevenuePlan({ dues, currentMonth, rates, teachers, instruments: instruments ?? [], teacherRates, monthlyExpense, target: effectiveTarget, assumptions });
  }, [baselinePlan, dues, rates, teachers, instruments, teacherRates, monthlyExpense, effectiveTarget, assumptions, currentMonth]);

  const baseline = plan?.baseline ?? baselinePlan?.baseline;
  const setAssumption = <K extends keyof PlanAssumptions>(key: K, value: PlanAssumptions[K]) => setAssumptions((current) => ({ ...current, [key]: value }));

  return (
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <Badge tone="warning">Deneysel</Badge>
          <span className="text-meta text-[.75rem]">Tahmindir; kapasite ve tempo varsayımlarını aşağıdan değiştirebilirsin. Hiçbir kayıt değişmez.</span>
        </div>

        {loading || !baseline ? (
          <div className="skeleton h-40 w-full rounded-xl" />
        ) : (
          <>
            <section className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1.3fr)]">
              <label className="block rounded-xl border border-[var(--line)] p-3">
                <span className="text-micro text-[var(--muted)]">Aylık gelir hedefi (tahsilat)</span>
                <div className="relative mt-1">
                  <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-sm font-bold text-[var(--muted)]">₺</span>
                  <input
                    type="number"
                    inputMode="numeric"
                    min={0}
                    step={5000}
                    value={effectiveTarget || ""}
                    onChange={(event) => setTarget(event.target.value === "" ? 0 : Math.max(0, Number(event.target.value)))}
                    className="field pl-7 text-lg font-bold tabular-nums"
                  />
                </div>
                <div className="mt-2 flex flex-wrap gap-1.5">
                  {[1.1, 1.25, 1.5].map((factor) => {
                    const value = Math.ceil((baseline.expectedCash * factor) / 5000) * 5000;
                    return <button key={factor} type="button" onClick={() => setTarget(value)} className="btn btn-quiet min-h-8 px-2.5 text-[.75rem]">+%{Math.round((factor - 1) * 100)} · {tl(value)}</button>;
                  })}
                </div>
              </label>
              <dl className="grid grid-cols-2 gap-x-3 gap-y-2 rounded-xl bg-[var(--surface-muted)] p-3 text-[.8125rem]">
                <Metric label={`Bugün (${monthName(baseline.period)} aidatı)`} value={tl(baseline.runRate)} hint={`${baseline.enrollments} kayıt · ${baseline.individual} birebir, ${baseline.group} grup`} />
                <Metric label="Beklenen aylık tahsilat" value={tl(baseline.expectedCash)} hint={`tahsilat oranı %${Math.round(baseline.collectionRate * 100)}`} />
                <Metric label="Aylık gider (ortalama)" value={tl(baseline.monthlyExpense)} hint="sabit + son 3 ayın tek seferlikleri" />
                <Metric label="Bugünkü net" value={tl(baseline.net)} tone={baseline.net < 0 ? "danger" : "success"} />
              </dl>
            </section>

            {plan && plan.gap <= 0 && (
              <p className="rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-xs font-semibold text-[var(--success-strong)]">
                Bu hedef bugünkü tahsilatla zaten karşılanıyor. Daha iddialı bir hedef dene (ör. +%25).
              </p>
            )}

            {plan && plan.gap > 0 && (
              <section aria-label="Yol haritası" className="space-y-3">
                <div className="flex flex-wrap items-end justify-between gap-2">
                  <div>
                    <p className="text-micro text-[var(--muted)]">Kapatılacak açık</p>
                    <p className="text-xl font-bold tabular-nums text-[var(--brand-strong)]">{tl(plan.gap)}<span className="text-sm font-semibold text-[var(--muted)]"> / ay</span></p>
                  </div>
                  {plan.reachMonth && (
                    <div className="text-right">
                      <p className="text-micro text-[var(--muted)]">Tahmini varış</p>
                      <p className="text-base font-bold">{monthName(plan.reachMonth)}</p>
                      <p className="text-meta text-[.72rem]">hedefe ulaşınca net ≈ {tl(plan.projectedNet)}/ay</p>
                    </div>
                  )}
                </div>

                <ProgressStack gap={plan.gap} steps={plan.steps} />

                <ol className="space-y-2">
                  {plan.steps.map((step, index) => (
                    <li key={step.key} className="rounded-xl border border-[var(--line)] p-3">
                      <div className="flex flex-wrap items-start justify-between gap-2">
                        <div className="flex min-w-0 items-start gap-2">
                          <span className="grid h-6 w-6 shrink-0 place-items-center rounded-full text-[.75rem] font-bold text-white" style={{ background: STEP_TONE[step.key] }}>{index + 1}</span>
                          <div className="min-w-0">
                            <p className="text-sm font-bold">{step.title}</p>
                            <p className="text-meta text-[.72rem]">{step.when}</p>
                          </div>
                        </div>
                        <div className="text-right">
                          <p className="font-bold tabular-nums text-[var(--success-strong)]">+{tl(step.impact)}/ay</p>
                          {step.cost > 0 && <p className="text-meta text-[.72rem] tabular-nums">öğretmen maliyeti −{tl(step.cost)}</p>}
                        </div>
                      </div>
                      <p className="mt-2 text-[.8125rem] leading-relaxed">{step.detail}</p>
                      {step.items.length > 0 && (
                        <ul className="mt-2 divide-y divide-[var(--line)] rounded-lg bg-[var(--surface-muted)] text-[.8125rem]">
                          {step.items.map((item) => (
                            <li key={item.label} className="flex flex-wrap items-baseline justify-between gap-x-3 px-2.5 py-1.5">
                              <span className="font-semibold">{item.label}</span>
                              <span className="text-[var(--muted)]">{item.value}</span>
                            </li>
                          ))}
                        </ul>
                      )}
                    </li>
                  ))}
                </ol>
              </section>
            )}

            {plan && plan.notes.length > 0 && (
              <section className="rounded-xl border border-[var(--line)] p-3">
                <p className="text-xs font-bold">Danışman notları</p>
                <ul className="mt-2 list-disc space-y-1 pl-5 text-[.8125rem]">
                  {plan.notes.map((note) => <li key={note}>{note}</li>)}
                </ul>
              </section>
            )}

            {plan && (
              <section className="grid gap-3 md:grid-cols-2">
                <div className="rounded-xl border border-[var(--line)] p-3">
                  <p className="text-xs font-bold">Bir hamlenin aylık değeri</p>
                  <ul className="mt-2 space-y-1 text-[.8125rem]">
                    {plan.sensitivity.map((row) => (
                      <li key={row.label} className="flex justify-between gap-2"><span>{row.label}</span><span className="font-semibold tabular-nums">+{tl(row.value)}</span></li>
                    ))}
                  </ul>
                </div>
                <div className="overflow-hidden rounded-xl border border-[var(--line)]">
                  <p className="border-b border-[var(--line)] px-3 py-2 text-xs font-bold">Enstrüman tablosu</p>
                  <ul className="max-h-48 overflow-auto text-[.8125rem]">
                    {plan.instruments.map((row) => (
                      <li key={row.id} className="flex items-baseline justify-between gap-2 px-3 py-1.5">
                        <span className="min-w-0 truncate"><span className="font-semibold">{row.name}</span> <span className="text-[var(--muted)]">· {row.individual + row.group} öğr. · {row.teachers.length} öğrt.</span></span>
                        <span className={`shrink-0 tabular-nums ${row.spare > 0 ? "text-[var(--success-strong)]" : "text-[var(--muted)]"}`}>{row.spare} boş saat</span>
                      </li>
                    ))}
                  </ul>
                </div>
              </section>
            )}

            <section className="rounded-xl border border-[var(--line)]">
              <button type="button" onClick={() => setShowAssumptions((value) => !value)} aria-expanded={showAssumptions} className="pressable flex w-full items-center justify-between px-3 py-2 text-left text-xs font-bold">
                Varsayımlar <span className="text-[var(--muted)]">{showAssumptions ? "Gizle" : "Değiştir"}</span>
              </button>
              {showAssumptions && (
                <div className="grid gap-3 border-t border-[var(--line)] p-3 sm:grid-cols-2">
                  <NumberField label="Öğretmen başı haftalık ders saati" value={assumptions.capacityPerTeacher} min={1} max={60} onChange={(value) => setAssumption("capacityPerTeacher", value)} />
                  <NumberField label="Grup sınıfı büyüklüğü" value={assumptions.groupSize} min={2} max={12} onChange={(value) => setAssumption("groupSize", value)} />
                  <NumberField label="Aylık gerçekçi yeni kayıt" value={assumptions.monthlyNewEnrollments} min={1} max={50} onChange={(value) => setAssumption("monthlyNewEnrollments", value)} />
                  <NumberField label="Azami zam (%)" value={assumptions.maxPriceIncreasePercent} min={0} max={50} onChange={(value) => setAssumption("maxPriceIncreasePercent", value)} />
                  <NumberField label="Hedef tahsilat oranı (%)" value={Math.round(assumptions.targetCollectionRate * 100)} min={50} max={100} onChange={(value) => setAssumption("targetCollectionRate", value / 100)} />
                  <button type="button" onClick={() => setAssumptions(DEFAULT_ASSUMPTIONS)} className="btn btn-quiet self-end">Varsayılana dön</button>
                </div>
              )}
            </section>
          </>
        )}
      </div>
  );
}

function Metric({ label, value, hint, tone }: { label: string; value: string; hint?: string; tone?: "success" | "danger" }) {
  return (
    <div className="min-w-0">
      <dt className="text-[.72rem] font-bold text-[var(--muted)]">{label}</dt>
      <dd className={`text-base font-bold tabular-nums ${tone === "danger" ? "text-[var(--danger-strong)]" : tone === "success" ? "text-[var(--success-strong)]" : ""}`}>{value}</dd>
      {hint && <dd className="text-meta text-[.72rem]">{hint}</dd>}
    </div>
  );
}

// Açığın hangi adımla ne kadar kapandığı: tek yatay şerit.
function ProgressStack({ gap, steps }: { gap: number; steps: PlanStep[] }) {
  return (
    <div>
      <div className="flex h-3 w-full overflow-hidden rounded-full bg-[var(--surface-muted)]" role="img" aria-label={steps.map((step) => `${step.title}: ${tl(step.impact)}`).join(", ")}>
        {steps.map((step) => <span key={step.key} style={{ width: `${Math.min(100, (step.impact / gap) * 100)}%`, background: STEP_TONE[step.key] }} />)}
      </div>
    </div>
  );
}

function NumberField({ label, value, min, max, onChange }: { label: string; value: number; min: number; max: number; onChange: (value: number) => void }) {
  return (
    <label className="block">
      <span className="text-micro text-[var(--muted)]">{label}</span>
      <input type="number" min={min} max={max} value={value} onChange={(event) => onChange(Math.min(max, Math.max(min, Number(event.target.value) || min)))} className="field mt-1 text-sm tabular-nums" />
    </label>
  );
}
