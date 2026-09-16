"use client";

import { useMemo, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { FormActions, FormMessage, Modal, SectionHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  COURSE_KIND_LABEL,
  useBillingPolicy,
  useCreateTuitionRate,
  useTuitionRates,
  useUpdateBillingPolicy,
  type CourseKind,
  type PrepayTier,
} from "@/lib/billing";

// Fiyat politikası ekranı. Eskiden burada dört kavram vardı: fiyat listesi (tarihli kap),
// fiyat kalemi (enstrüman × ders süresi × aylık/paket × paket ders sayısı), toplu zam
// önizlemesi ve her kurs kaydına ayrı açılan ücret planı. Okulun gerçek fiyat tablosu ise
// iki satır: Birebir 4 ders 6.000 TL, Grup 4 ders 4.500 TL - ve fiyat ne enstrümana ne de
// ders süresine göre değişiyor (docs/10-decisions.md H1).
//
// Bu yüzden ekran ikiye indi: YÜRÜRLÜKTEKİ TARİFE ve İNDİRİM POLİTİKASI. Zam ayrı bir
// "toplu güncelleme" işlemi değil - yeni yürürlük tarihiyle yeni satır açılır, öncekisi
// otomatik kapanır, yazılmış aidatlar değişmez.

function money(value: number, currency = "TRY") {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
}

function formatDate(value: string) {
  return new Date(`${value}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric" });
}

const COURSE_KINDS: CourseKind[] = ["Individual", "Group"];

type DiscountDraft = { multiCourse: number; sibling: number; dueDay: number; tiers: PrepayTier[] };

export function TuitionPolicySection() {
  const { data: rates, isLoading } = useTuitionRates();
  const [newRateFor, setNewRateFor] = useState<CourseKind | null>(null);

  const current = useMemo(() => {
    const map = new Map<CourseKind, NonNullable<typeof rates>[number]>();
    for (const rate of rates ?? []) if (rate.isCurrent) map.set(rate.courseKind, rate);
    return map;
  }, [rates]);

  const history = useMemo(() => (rates ?? []).filter((rate) => !rate.isCurrent), [rates]);

  return (
    <section className="space-y-4">
      <SectionHeader
        title="Ücret tarifesi"
        description="Aidat tutarı yalnızca dersin birebir mi grup mu olduğuna göre değişir. Zam, yeni yürürlük tarihiyle yeni bir satır açar; yazılmış aidatlar değişmez."
      />

      {isLoading && <div className="grid gap-3 sm:grid-cols-2">{[1, 2].map((item) => <div key={item} className="skeleton h-32 rounded-2xl" />)}</div>}

      {!isLoading && <div className="grid gap-3 sm:grid-cols-2">
        {COURSE_KINDS.map((kind) => {
          const rate = current.get(kind);
          return (
            <article key={kind} className="app-card p-4">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-micro text-[var(--brand-strong)]">{COURSE_KIND_LABEL[kind]}</p>
                  {rate
                    ? <>
                        <p className="mt-1 font-serif text-2xl font-bold tabular-nums tracking-[-.02em]">{money(rate.monthlyAmount, rate.currency)}</p>
                        <p className="text-meta mt-0.5">ayda {rate.lessonsPerMonth} ders · {formatDate(rate.effectiveFrom)} tarihinden beri</p>
                      </>
                    : <>
                        <p className="mt-1 font-serif text-lg font-bold text-[var(--muted)]">Tarife yok</p>
                        <p className="text-meta mt-0.5">Bu ders türünün aidatı oluşturulamaz.</p>
                      </>}
                </div>
                <span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl ${rate ? "bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "bg-[var(--warning-soft)] text-[var(--warning-strong)]"}`}>
                  <Icon name={rate ? "wallet" : "bell"} className="h-4 w-4" />
                </span>
              </div>
              <button type="button" onClick={() => setNewRateFor(kind)} className="btn btn-quiet mt-3 w-full">
                <Icon name="plus" className="h-4 w-4" />{rate ? "Yeni tarife (zam)" : "Tarife tanımla"}
              </button>
            </article>
          );
        })}
      </div>}

      {history.length > 0 && (
        <details className="app-card overflow-hidden">
          <summary className="cursor-pointer px-4 py-3 text-xs font-bold text-[var(--muted)]">Tarife geçmişi ({history.length})</summary>
          <ul className="divide-y divide-[var(--line)] border-t border-[var(--line)]">
            {history.map((rate) => (
              <li key={rate.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-2.5 text-xs">
                <span><strong>{COURSE_KIND_LABEL[rate.courseKind]}</strong><span className="text-meta"> · {formatDate(rate.effectiveFrom)} – {rate.effectiveUntil ? formatDate(rate.effectiveUntil) : "süresiz"}</span></span>
                <strong className="tabular-nums">{money(rate.monthlyAmount, rate.currency)}</strong>
              </li>
            ))}
          </ul>
        </details>
      )}

      <DiscountPolicyCard />

      {newRateFor && <NewRateModal courseKind={newRateFor} onClose={() => setNewRateFor(null)} />}
    </section>
  );
}

function NewRateModal({ courseKind, onClose }: { courseKind: CourseKind; onClose: () => void }) {
  const createRate = useCreateTuitionRate();
  const [monthlyAmount, setMonthlyAmount] = useState<number | "">("");
  const [lessonsPerMonth, setLessonsPerMonth] = useState(4);
  const [effectiveFrom, setEffectiveFrom] = useState(() => new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createRate.mutateAsync({
        courseKind,
        lessonsPerMonth,
        monthlyAmount: Number(monthlyAmount),
        effectiveFrom,
      });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Tarife oluşturulamadı.");
    }
  }

  return (
    <Modal
      open
      title={`${COURSE_KIND_LABEL[courseKind]} tarifesi`}
      description="Bu tarih ve sonrasında oluşturulan aidatlar bu tutardan hesaplanır. Önceki tarife bir gün öncesinde otomatik kapanır; yazılmış aidatlar değişmez."
      onClose={onClose}
    >
      <form onSubmit={handleSubmit} className="space-y-3.5">
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="form-label">Aylık tutar (₺)
            <input type="number" min={0} step={0.01} value={monthlyAmount} onChange={(event) => setMonthlyAmount(event.target.value === "" ? "" : Number(event.target.value))} required autoFocus className="field text-sm" placeholder="6000" />
          </label>
          <label className="form-label">Ayda kaç ders
            <input type="number" min={1} max={31} value={lessonsPerMonth} onChange={(event) => setLessonsPerMonth(Number(event.target.value))} required className="field text-sm" />
          </label>
        </div>
        <label className="form-label">Yürürlük başlangıcı
          <input type="date" value={effectiveFrom} onChange={(event) => setEffectiveFrom(event.target.value)} required className="field text-sm" />
        </label>
        {error && <FormMessage tone="error">{error}</FormMessage>}
        <FormActions onCancel={onClose} submitLabel="Tarifeyi yürürlüğe al" pending={createRate.isPending} pendingLabel="Kaydediliyor…" />
      </form>
    </Modal>
  );
}

// İndirimler ayrı bir tablo değil, kurum geneli bir politika: "2 kursa katılanlar &
// kardeşler için %5" ve "toplu ödemelerde indirim". Birlikte anlam taşıdıkları için tek
// formda ve tek kaydetmede düzenlenirler - yarım kalmış bir politika bırakmasın.
function DiscountPolicyCard() {
  const { data: policy, isLoading } = useBillingPolicy();
  const updatePolicy = useUpdateBillingPolicy();
  // Sunucudaki politika tek kaynak; düzenlenmemiş alanlar ondan TÜRETİLİR. Taslağı bir
  // effect'te state'e yazmak fazladan bir render turu ve veri geç geldiğinde "önce boş,
  // sonra dolu" titremesi üretirdi (aidat listesindeki varsayılan dönem ile aynı gerekçe).
  const [edits, setEdits] = useState<DiscountDraft | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const draft = edits ?? (policy ? {
    multiCourse: policy.multiCourseDiscountPercent,
    sibling: policy.siblingDiscountPercent,
    dueDay: policy.dueDayOfMonth,
    tiers: policy.prepayTiers.map((tier) => ({ minMonths: tier.minMonths, percent: tier.percent })),
  } : null);

  if (isLoading || !draft) return <div className="skeleton h-56 rounded-2xl" />;

  function patch(next: Partial<DiscountDraft>) {
    setEdits({ ...draft!, ...next });
    setSaved(false);
    setError(null);
  }

  function patchTier(index: number, next: Partial<PrepayTier>) {
    patch({ tiers: draft!.tiers.map((tier, i) => (i === index ? { ...tier, ...next } : tier)) });
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await updatePolicy.mutateAsync({
        multiCourseDiscountPercent: draft!.multiCourse,
        siblingDiscountPercent: draft!.sibling,
        dueDayOfMonth: draft!.dueDay,
        prepayTiers: [...draft!.tiers].sort((a, b) => a.minMonths - b.minMonths),
      });
      // Kaydedildikten sonra taslak bırakılır; ekran tazelenen sunucu politikasından
      // yeniden türer, böylece sunucunun sıraladığı/temizlediği hâli görünür.
      setEdits(null);
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "İndirim politikası kaydedilemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="app-card space-y-4 p-4">
      <div>
        <h3 className="text-title">İndirim politikası</h3>
        <p className="text-meta mt-0.5">Bir öğrenci birden fazla indirime uyuyorsa <strong>en yükseği</strong> uygulanır, toplanmaz. Bir kurs kaydına elle indirim girilmişse otomatik kurallar yerine o geçerlidir.</p>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <label className="form-label">2 kursa katılan (%)
          <input type="number" min={0} max={100} step={0.5} value={draft.multiCourse} onChange={(event) => patch({ multiCourse: Number(event.target.value) })} className="field text-sm" />
        </label>
        <label className="form-label">Kardeş (%)
          <input type="number" min={0} max={100} step={0.5} value={draft.sibling} onChange={(event) => patch({ sibling: Number(event.target.value) })} className="field text-sm" />
        </label>
        <label className="form-label">Vade günü
          <input type="number" min={1} max={28} value={draft.dueDay} onChange={(event) => patch({ dueDay: Number(event.target.value) })} className="field text-sm" />
        </label>
      </div>

      <div className="rounded-2xl border border-[var(--line)] p-3">
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <p className="text-xs font-bold">Toplu ödeme kademeleri</p>
          <p className="text-meta">Ödenen ay sayısına uyan <strong>en yüksek</strong> kademe uygulanır.</p>
        </div>

        <div className="mt-2.5 space-y-2">
          {draft.tiers.map((tier, index) => (
            <div key={index} className="flex flex-wrap items-end gap-2">
              <label className="form-label w-28">En az ay
                <input type="number" min={2} max={24} value={tier.minMonths} onChange={(event) => patchTier(index, { minMonths: Number(event.target.value) })} className="field min-h-10 text-sm" />
              </label>
              <label className="form-label w-28">İndirim (%)
                <input type="number" min={0} max={100} step={0.5} value={tier.percent} onChange={(event) => patchTier(index, { percent: Number(event.target.value) })} className="field min-h-10 text-sm" />
              </label>
              <p className="text-meta min-w-0 flex-1 pb-2.5">{tier.minMonths} ay ve üzeri peşin ödeyene %{tier.percent} indirim</p>
              <button type="button" onClick={() => patch({ tiers: draft.tiers.filter((_, i) => i !== index) })} aria-label={`${tier.minMonths} aylık kademeyi sil`} className="icon-btn icon-btn-quiet mb-1 h-10 w-10 hover:border-[var(--danger)] hover:text-[var(--danger-strong)]">
                <Icon name="x" className="h-3.5 w-3.5" />
              </button>
            </div>
          ))}
          {draft.tiers.length === 0 && <p className="text-meta">Kademe yok - peşin ödemeye indirim uygulanmaz.</p>}
        </div>

        <button type="button" onClick={() => patch({ tiers: [...draft.tiers, { minMonths: Math.min(24, (draft.tiers.at(-1)?.minMonths ?? 2) + 2), percent: 0 }] })} className="btn btn-quiet mt-2 w-full border-dashed">
          <Icon name="plus" className="h-4 w-4" />Kademe ekle
        </button>
      </div>

      {error && <FormMessage tone="error">{error}</FormMessage>}
      {saved && !error && <FormMessage tone="success">İndirim politikası kaydedildi. Bundan sonra oluşturulan aidatlar bu kurallara göre hesaplanır.</FormMessage>}

      <div className="flex justify-end">
        <button type="submit" disabled={updatePolicy.isPending} className="btn btn-primary">{updatePolicy.isPending ? "Kaydediliyor…" : "Politikayı kaydet"}</button>
      </div>
    </form>
  );
}
