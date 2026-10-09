"use client";

import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from "react";
import { Icon } from "@/components/icons";
import { Badge, EmptyState, FormActions, FormMessage, Modal, Panel, RowMenu, RowMenuItem, RowMenuSeparator, Segmented, type Tone } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { COURSE_KIND_LABEL, type CourseKind } from "@/lib/billing";
import { useInstruments } from "@/lib/people";
import {
  IDEA_KIND_LABEL, IDEA_STATUS_LABEL, VERDICT_LABEL, previewIdea, useDeleteIdea, useProfitability, useProfitCommentary,
  useRefreshProfitCommentary, useSaveIdea, useSetIdeaStatus,
  type GrowthIdea, type IdeaEvaluation, type IdeaInput, type IdeaKind, type IdeaStatus, type IdeaVerdict, type ProfitabilityOverview, type ProfitMonth,
} from "@/lib/profitability";

// Gelir ve gider ekranının Kârlılık sekmesi (docs/10-decisions.md V). Soru "ne kadar ciro"
// değil "ne kadar kâr kalıyor ve nasıl artar": sunucu her açılışta güncel veriden net kârı,
// öğretmen saati başına katkıyı, işleyiş eksiklerini ve yöneticinin fikirlerinin etkisini
// hesaplar; ayda bir de yapay zekâ yorumu üretir. Bu dosyada hesap yok, yalnızca gösterim.

const MONTHS = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];
const MONTHS_SHORT = ["Oca", "Şub", "Mar", "Nis", "May", "Haz", "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara"];
// "Ekim'de", "Mart'ta": ay adının bulunma eki ünlü uyumuna ve sert ünsüze göre değişir.
const MONTHS_IN = ["Ocak'ta", "Şubat'ta", "Mart'ta", "Nisan'da", "Mayıs'ta", "Haziran'da", "Temmuz'da", "Ağustos'ta", "Eylül'de", "Ekim'de", "Kasım'da", "Aralık'ta"];
const monthIndex = (period: string) => Number(period.slice(5, 7)) - 1;
const tl = (value: number) => `${value < 0 ? "−" : ""}₺${Math.abs(Math.round(value)).toLocaleString("tr-TR")}`;
const signedTl = (value: number) => `${value > 0 ? "+" : ""}${tl(value)}`;

// Grafik renkleri: gelir/gider anlamı taşıdığı için uygulamanın başarı/tehlike tonları; kimlik
// lejantta metinle de verilir.
const SERIES = { teacher: "var(--danger)", fixed: "var(--muted)", net: "var(--success)" };
const VERDICT_TONE: Record<IdeaVerdict, Tone> = { Recommended: "success", Uncertain: "warning", NotRecommended: "danger" };
const IDEA_KINDS: IdeaKind[] = ["GroupClass", "NewBranch", "Incentive", "NewTeacher", "PriceChange", "Custom"];

export function ProfitabilityView() {
  const { data, isLoading, error } = useProfitability();
  const [editing, setEditing] = useState<{ idea?: GrowthIdea; kind: IdeaKind } | null>(null);

  if (isLoading) return <div className="space-y-3"><div className="skeleton h-48 rounded-2xl" /><div className="skeleton h-40 rounded-2xl" /></div>;
  if (error || !data) return <FormMessage tone="error">{error instanceof ApiError ? error.detail ?? error.title : "Kârlılık verisi yüklenemedi."}</FormMessage>;

  return (
    <div className="space-y-4">
      <ProfitHero months={data.months} ratesMissing={data.unit.averageTeacherRate === null} />
      <AdvicePanel insights={data.insights} />
      {data.gaps.length > 0 && (
        <Panel title="Düzeltilmesi gerekenler" meta="hesabın doğru çıkması için">
          <ul className="space-y-2.5">{data.gaps.map((gap) => (
            <Bullet key={gap.key} icon="alert-triangle" tone={gap.severity === "Warning" ? "warning" : "muted"}>{gap.message}</Bullet>
          ))}</ul>
        </Panel>
      )}
      <IdeasPanel data={data} onAdd={(kind) => setEditing({ kind })} onEdit={(idea) => setEditing({ idea, kind: idea.kind })} />

      {editing && <IdeaFormModal key={editing.idea?.id ?? editing.kind} initial={editing.idea} initialKind={editing.kind} data={data} onClose={() => setEditing(null)} />}
    </div>
  );
}

function Bullet({ icon, tone, children }: { icon: "sparkles" | "alert-triangle"; tone: Tone; children: ReactNode }) {
  const color = { brand: "text-[var(--brand)]", warning: "text-[var(--warning)]", muted: "text-[var(--muted)]", success: "text-[var(--success)]", danger: "text-[var(--danger)]" }[tone];
  return (
    <li className="flex gap-2.5 text-[.875rem] leading-relaxed">
      <Icon name={icon} className={`mt-1 h-4 w-4 shrink-0 ${color}`} />
      <span>{children}</span>
    </li>
  );
}

// Tek soru: "bu ay cebimde ne kalıyor?" Büyük rakam, bir cümlelik döküm ve son 6 ayın
// sade grafiği. Döküm terimsiz: aidat girdi, öğretmene ve diğer giderlere şu kadar gitti.
function ProfitHero({ months, ratesMissing }: { months: ProfitMonth[]; ratesMissing: boolean }) {
  const current = months[months.length - 1];
  const previous = months.length > 1 ? months[months.length - 2] : null;
  const change = previous ? current.net - previous.net : 0;
  const max = Math.max(1, ...months.map((month) => Math.abs(month.net)));
  const hasNegative = months.some((month) => month.net < 0);
  const width = 560;
  const height = 150;
  const labelSpace = 18;
  const plot = height - labelSpace - 18;
  const zero = hasNegative ? 18 + plot / 2 : 18 + plot;
  const scale = hasNegative ? plot / 2 : plot;
  const slot = width / months.length;
  const bar = Math.min(48, slot * 0.55);

  return (
    <section className="app-card p-4 sm:p-5" aria-label="Bu ay cebinde kalan">
      <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)] lg:items-center">
        <div>
          <p className="text-sm font-bold text-[var(--muted)]">{MONTHS_IN[monthIndex(current.period)]} cebinde kalan</p>
          <p className={`mt-1 font-serif text-[2.6rem] font-bold leading-none tabular-nums ${current.net < 0 ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>{tl(current.net)}</p>
          {previous && (
            <p className="mt-2 text-sm font-semibold">
              {change === 0 ? "Geçen ayla aynı" : <>Geçen aydan <span className={change > 0 ? "text-[var(--success-strong)]" : "text-[var(--danger-strong)]"}>{tl(Math.abs(change))} {change > 0 ? "fazla" : "az"}</span></>}
            </p>
          )}
          <p className="text-meta mt-3 leading-relaxed">
            Aidatlardan <b className="text-[var(--foreground)]">{tl(current.revenue)}</b> geliyor. Öğretmenlere <b className="text-[var(--foreground)]">{tl(current.teacherCost)}</b>,
            kira ve diğer giderlere <b className="text-[var(--foreground)]">{tl(current.fixedCost)}</b> gidiyor.
            {ratesMissing
              ? " Öğretmen ücretleri girilmediği için öğretmene giden para hesaba katılmadı; gerçek rakam daha düşük."
              : current.teacherCostEstimated ? " Ücreti girilmemiş öğretmenler ortalama ücretle hesaplandı." : ""}
          </p>
        </div>
        <div>
          <p className="text-micro mb-1 text-[var(--muted)]">Son 6 ay, her ay cebinde kalan</p>
          <svg viewBox={`0 0 ${width} ${height}`} className="h-auto w-full" role="img"
            aria-label={`Son 6 ay cebinde kalan: ${months.map((month) => `${MONTHS[monthIndex(month.period)]} ${tl(month.net)}`).join(", ")}`}>
            <line x1="0" x2={width} y1={zero} y2={zero} stroke="var(--line)" />
            {months.map((month, index) => {
              const x = index * slot + (slot - bar) / 2;
              const h = Math.max(1, (Math.abs(month.net) / max) * scale);
              const up = month.net >= 0;
              return (
                <g key={month.period}>
                  <title>{`${MONTHS[monthIndex(month.period)]}: aidat ${tl(month.revenue)}, öğretmen ${tl(month.teacherCost)}, diğer giderler ${tl(month.fixedCost)}, kalan ${tl(month.net)}`}</title>
                  <rect x={x} y={up ? zero - h : zero} width={bar} height={h} rx="4"
                    fill={up ? "var(--success)" : "var(--danger)"} opacity={month.isCurrent ? 1 : 0.45} />
                  <text x={x + bar / 2} y={up ? zero - h - 5 : zero + h + 12} textAnchor="middle" fontSize="12" fontWeight="700" fill="var(--foreground)">{compact(month.net)}</text>
                  <text x={x + bar / 2} y={height - 3} textAnchor="middle" fontSize="12" fill="var(--muted)" fontWeight={month.isCurrent ? 700 : 400}>{MONTHS_SHORT[monthIndex(month.period)]}</text>
                </g>
              );
            })}
          </svg>
        </div>
      </div>
    </section>
  );
}

const compact = (value: number) => {
  const abs = Math.abs(value);
  const text = abs >= 1000 ? `${(abs / 1000).toLocaleString("tr-TR", { maximumFractionDigits: abs >= 100000 ? 0 : 1 })} bin` : `${Math.round(abs)}`;
  return `${value < 0 ? "−" : ""}₺${text}`;
};

// "Bu ay ne yapmalı?": üstte ayda bir yazılan yapay zekâ yorumu (varsa), altında okulun
// verisinden hesaplanan kısa tavsiyeler. Yorum kapalıysa yalnızca tavsiyeler görünür.
function AdvicePanel({ insights }: { insights: string[] }) {
  const { data, isLoading, refetch } = useProfitCommentary();
  const refresh = useRefreshProfitCommentary();
  const next = data ? new Date(`${data.nextAutomaticOn}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long" }) : "";

  return (
    <Panel
      title="Bu ay ne yapmalı?"
      footer={data?.status === "Ready" ? (
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="text-meta text-[.72rem]">Yorum ayda bir yazılır · sonraki {next}</span>
          <button type="button" className="btn btn-quiet min-h-8 px-2.5 text-[.75rem]" disabled={refresh.isPending || data.refreshesLeft === 0}
            title="Bir bilgiyi düzelttiysen (ör. öğretmen ücretleri) yorumu yeniden yazdır"
            onClick={() => refresh.mutate()}>
            <Icon name="sparkles" className="h-3.5 w-3.5" />
            {refresh.isPending ? "Yazılıyor…" : data.refreshesLeft > 0 ? `Yeniden yaz (${data.refreshesLeft} hak)` : "Bu ayın hakkı doldu"}
          </button>
        </div>
      ) : undefined}
    >
      {isLoading ? (
        <div className="mb-4 space-y-2"><div className="skeleton h-4 w-full rounded" /><div className="skeleton h-4 w-11/12 rounded" /><div className="skeleton h-4 w-3/4 rounded" /></div>
      ) : data?.status === "Ready" ? (
        <div className="mb-4 rounded-xl bg-[var(--brand-soft)] p-3.5">
          <p className="text-micro mb-1 flex items-center gap-1.5 text-[var(--brand-strong)]"><Icon name="sparkles" className="h-3.5 w-3.5" />Yapay zekâ yorumu</p>
          <p className="font-serif text-[1rem] leading-relaxed">{data.text}</p>
        </div>
      ) : data?.status === "Failed" ? (
        <p className="text-meta mb-3">Yapay zekâ yorumu şu an yazılamadı. <button type="button" className="font-bold text-[var(--brand-strong)] underline" onClick={() => refetch()}>Tekrar dene</button></p>
      ) : null}
      {insights.length === 0
        ? <p className="text-meta">Tavsiye için henüz yeterli veri yok.</p>
        : <ul className="space-y-2.5">{insights.map((insight) => <Bullet key={insight} icon="sparkles" tone="brand">{insight}</Bullet>)}</ul>}
      {refresh.error && <div className="mt-3"><FormMessage tone="error">{refresh.error instanceof ApiError ? refresh.error.detail ?? refresh.error.title : "Yorum yenilenemedi."}</FormMessage></div>}
    </Panel>
  );
}

function IdeasPanel({ data, onAdd, onEdit }: { data: ProfitabilityOverview; onAdd: (kind: IdeaKind) => void; onEdit: (idea: GrowthIdea) => void }) {
  const setStatus = useSetIdeaStatus();
  const remove = useDeleteIdea();
  const scale = Math.max(1, ...data.ideas.map((idea) => Math.abs(idea.evaluation.monthlyNet)));

  return (
    <Panel
      title="Fikirlerim"
      meta="aklındakini yaz, ne kazandıracağını gör"
      flush
      actions={<button type="button" className="btn btn-primary min-h-9" onClick={() => onAdd("GroupClass")}><Icon name="plus" className="h-4 w-4" />Fikir ekle</button>}
    >
      {data.ideas.length === 0 ? (
        <div className="p-4">
          <EmptyState icon="target" title="İlk fikrini tart" description="Grup dersi, İngilizce sınıfı, indirim kampanyası... Türünü seç, birkaç sayı gir; ayda ne kazandıracağını gör." />
          <div className="flex flex-wrap justify-center gap-1.5">
            {IDEA_KINDS.map((kind) => <button key={kind} type="button" className="btn btn-quiet min-h-8 px-2.5 text-[.75rem]" onClick={() => onAdd(kind)}>{IDEA_KIND_LABEL[kind]}</button>)}
          </div>
        </div>
      ) : (
        <ul className="divide-y divide-[var(--line)]">
          {data.ideas.map((idea) => {
            const value = idea.evaluation.monthlyNet;
            const dropped = idea.status === "Dropped";
            return (
              <li key={idea.id} className={`grid grid-cols-[minmax(0,1fr)_auto] items-start gap-3 px-4 py-3 ${dropped ? "opacity-60" : ""}`}>
                <button type="button" className="min-w-0 text-left" onClick={() => onEdit(idea)}>
                  <span className="flex flex-wrap items-center gap-1.5">
                    <span className="text-sm font-bold">{idea.title}</span>
                    <span className="text-meta text-[.72rem]">{IDEA_KIND_LABEL[idea.kind]}</span>
                    {idea.status !== "Idea" && <Badge tone={idea.status === "Applied" ? "success" : idea.status === "Trying" ? "brand" : "muted"}>{IDEA_STATUS_LABEL[idea.status]}</Badge>}
                  </span>
                  <span className="text-meta mt-0.5 block text-[.75rem] leading-snug">{idea.evaluation.reasons[0]}</span>
                  <span aria-hidden className="mt-2 block h-1.5 rounded-r-full" style={{ width: `${Math.max(2, (Math.abs(value) / scale) * 100)}%`, background: value < 0 ? SERIES.teacher : idea.evaluation.verdict === "NotRecommended" ? SERIES.fixed : SERIES.net }} />
                </button>
                <div className="flex items-start gap-1">
                  <div className="text-right">
                    <p className={`text-sm font-bold tabular-nums ${value < 0 ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>ayda {signedTl(value)}</p>
                    <Badge tone={VERDICT_TONE[idea.evaluation.verdict]} className="mt-1">{VERDICT_LABEL[idea.evaluation.verdict]}</Badge>
                  </div>
                  <RowMenu label={`${idea.title} işlemleri`}>
                    {(close) => (
                      <>
                        <RowMenuItem icon="pencil" onClick={() => { close(); onEdit(idea); }}>Düzenle</RowMenuItem>
                        {(["Idea", "Trying", "Applied", "Dropped"] as IdeaStatus[]).filter((status) => status !== idea.status).map((status) => (
                          <RowMenuItem key={status} onClick={() => { close(); setStatus.mutate({ id: idea.id, status }); }}>
                            {status === "Idea" ? "Fikre geri al" : `${IDEA_STATUS_LABEL[status]} olarak işaretle`}
                          </RowMenuItem>
                        ))}
                        <RowMenuSeparator />
                        <RowMenuItem icon="x" tone="danger" onClick={() => {
                          close();
                          if (window.confirm(`"${idea.title}" fikri silinsin mi?`)) remove.mutate(idea.id);
                        }}>Sil</RowMenuItem>
                      </>
                    )}
                  </RowMenu>
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </Panel>
  );
}

// --- Fikir formu: tür seç, birkaç sayı gir, sunucu okulun verisiyle anında tartsın ---

interface Draft {
  kind: IdeaKind;
  title: string;
  note: string;
  instrumentId: string;
  branchName: string;
  courseKind: CourseKind;
  students: string;
  teacherRatePerLesson: string;
  discountPercent: string;
  discountMonths: string;
  alreadyComingPercent: string;
  priceChangePercent: string;
  lostStudents: string;
  monthlyAmount: string;
  oneTimeCost: string;
}

const text = (value: number | null | undefined, fallback = "") => value === null || value === undefined ? fallback : String(value);
const num = (value: string) => value.trim() === "" ? null : Number(value.replace(",", "."));

function toDraft(idea: GrowthIdea | undefined, kind: IdeaKind, firstInstrument: string): Draft {
  return {
    kind: idea?.kind ?? kind,
    title: idea?.title ?? "",
    note: idea?.note ?? "",
    instrumentId: idea?.instrumentId ?? firstInstrument,
    branchName: idea?.branchName ?? "",
    courseKind: idea?.courseKind ?? "Individual",
    students: text(idea?.students, kind === "GroupClass" ? "4" : "3"),
    teacherRatePerLesson: text(idea?.teacherRatePerLesson),
    discountPercent: text(idea?.discountPercent, "20"),
    discountMonths: text(idea?.discountMonths, "3"),
    alreadyComingPercent: text(idea?.alreadyComingPercent, "30"),
    priceChangePercent: text(idea?.priceChangePercent, "5"),
    lostStudents: text(idea?.lostStudents, "0"),
    monthlyAmount: text(idea?.monthlyAmount),
    oneTimeCost: text(idea?.oneTimeCost),
  };
}

function toInput(draft: Draft, title: string): IdeaInput {
  return {
    kind: draft.kind,
    title,
    note: draft.note.trim() || null,
    instrumentId: draft.instrumentId || null,
    branchName: draft.branchName.trim() || null,
    courseKind: draft.courseKind,
    students: num(draft.students),
    teacherRatePerLesson: num(draft.teacherRatePerLesson),
    discountPercent: num(draft.discountPercent),
    discountMonths: num(draft.discountMonths),
    alreadyComingPercent: num(draft.alreadyComingPercent),
    priceChangePercent: num(draft.priceChangePercent),
    lostStudents: num(draft.lostStudents),
    monthlyAmount: num(draft.monthlyAmount),
    oneTimeCost: num(draft.oneTimeCost),
  };
}

// Başlık boş bırakılırsa parametrelerden okunur bir ad türetilir.
function suggestTitle(draft: Draft, instrumentName: string | undefined) {
  switch (draft.kind) {
    case "NewTeacher": return `${instrumentName ?? "Branş"} için yeni öğretmen`;
    case "GroupClass": return `${instrumentName ?? "Branş"} · ${draft.students || "?"} kişilik grup`;
    case "NewBranch": return `${draft.branchName.trim() || "Yeni branş"} · ${COURSE_KIND_LABEL[draft.courseKind].toLowerCase()}`;
    case "Incentive": return `İlk ${draft.discountMonths || "?"} ay %${draft.discountPercent || "?"} indirim`;
    case "PriceChange": return `Tarife %${draft.priceChangePercent || "?"}`;
    default: return "Serbest fikir";
  }
}

function IdeaFormModal({ initial, initialKind, data, onClose }: { initial?: GrowthIdea; initialKind: IdeaKind; data: ProfitabilityOverview; onClose: () => void }) {
  const { data: allInstruments } = useInstruments();
  const instruments = useMemo(() => {
    const known = new Map(data.instruments.map((row) => [row.id, row.name]));
    for (const instrument of allInstruments ?? []) if (!known.has(instrument.id)) known.set(instrument.id, instrument.name);
    return Array.from(known, ([id, name]) => ({ id, name }));
  }, [data.instruments, allInstruments]);
  const [draft, setDraft] = useState<Draft>(() => toDraft(initial, initialKind, data.instruments[0]?.id ?? ""));
  const [preview, setPreview] = useState<IdeaEvaluation | null>(initial?.evaluation ?? null);
  const [previewError, setPreviewError] = useState<Record<string, string[]> | null>(null);
  const save = useSaveIdea();
  const instrumentName = instruments.find((row) => row.id === draft.instrumentId)?.name;
  const title = draft.title.trim() || suggestTitle(draft, instrumentName);
  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => setDraft((current) => ({ ...current, [key]: value }));
  const averageRate = data.unit.averageTeacherRate;

  // Canlı önizleme: yazmayı bitirince (400 ms) sunucu fikri okulun verisiyle tartar.
  // Etki alanı nesnenin kimliğine değil içeriğine bağlı: aynı değerlerle tekrar istek atılmaz.
  const input = toInput(draft, title);
  const inputKey = JSON.stringify(input);
  useEffect(() => {
    // Geç dönen eski bir yanıt (ör. alan boşken atılan istek) yenisinin üstüne yazmasın.
    let active = true;
    const handle = window.setTimeout(() => {
      previewIdea(JSON.parse(inputKey) as IdeaInput)
        .then((result) => { if (active) { setPreview(result); setPreviewError(null); } })
        .catch((error) => { if (active) setPreviewError(error instanceof ApiError && error.errors ? error.errors : { "": ["Önizleme hesaplanamadı."] }); });
    }, 400);
    return () => { active = false; window.clearTimeout(handle); };
  }, [inputKey]);

  async function submit(event: FormEvent) {
    event.preventDefault();
    try {
      await save.mutateAsync({ ...input, id: initial?.id });
      onClose();
    } catch {
      // Hata aşağıda gösterilir.
    }
  }

  // Önizlemenin hatası yalnızca doldurulmuş alanda gösterilir: formu açar açmaz boş alanlar için
  // "zorunlu" uyarısı çıkmasın; boş zorunlu alan kaydederken sunucunun hatasıyla görünür.
  const filled = (field: string) => String((draft as unknown as Record<string, unknown>)[field] ?? "").trim() !== "";
  const fieldError = (field: string) =>
    (filled(field) ? previewError?.[field]?.[0] : undefined) ?? (save.error instanceof ApiError ? save.error.errors?.[field]?.[0] : undefined);

  return (
    <Modal open title={initial ? "Fikri düzenle" : "Fikir ekle"} description="Birkaç sayı gir, ne kazandıracağı okulun bugünkü rakamlarıyla hesaplansın. Hiçbir kayıt değişmez." onClose={onClose} size="lg">
      <form onSubmit={submit} className="space-y-4">
        <Segmented<IdeaKind> label="Fikir türü" value={draft.kind} onChange={(kind) => set("kind", kind)}
          options={IDEA_KINDS.map((kind) => ({ value: kind, label: IDEA_KIND_LABEL[kind] }))} />

        <div className="grid gap-3 sm:grid-cols-2">
          {(draft.kind === "NewTeacher" || draft.kind === "GroupClass") && (
            <Field label="Branş" error={fieldError("instrumentId")}>
              <select className="field" value={draft.instrumentId} onChange={(event) => set("instrumentId", event.target.value)}>
                {instruments.map((row) => <option key={row.id} value={row.id}>{row.name}</option>)}
              </select>
            </Field>
          )}
          {draft.kind === "NewBranch" && (
            <>
              <Field label="Branş adı" error={fieldError("branchName")}>
                <input className="field" value={draft.branchName} maxLength={60} placeholder="İngilizce" onChange={(event) => set("branchName", event.target.value)} />
              </Field>
              <Field label="Birebir mi grup mu" hint="aidat mevcut fiyattan">
                <Segmented<CourseKind> label="Ders türü" value={draft.courseKind} onChange={(kind) => set("courseKind", kind)}
                  options={(["Individual", "Group"] as CourseKind[]).map((kind) => ({ value: kind, label: `${COURSE_KIND_LABEL[kind]} · ${tl(kind === "Group" ? data.unit.groupPrice : data.unit.individualPrice)}` }))} />
              </Field>
            </>
          )}
          {draft.kind !== "PriceChange" && draft.kind !== "Custom" && (
            <NumberField label={draft.kind === "GroupClass" ? "Grup kaç kişi" : draft.kind === "Incentive" ? "Kaç yeni öğrenci gelir" : "Kaç öğrenci"}
              value={draft.students} onChange={(value) => set("students", value)} error={fieldError("students")} min={1} />
          )}
          {(draft.kind === "NewTeacher" || draft.kind === "GroupClass" || draft.kind === "NewBranch") && (
            <NumberField label="Öğretmene ders başı ödeme" prefix="₺" value={draft.teacherRatePerLesson} onChange={(value) => set("teacherRatePerLesson", value)}
              placeholder={averageRate ? String(Math.round(averageRate)) : "girilmemiş"} hint={draft.teacherRatePerLesson.trim() ? undefined : averageRate ? "boş bırakırsan ortalama ücret" : "öğretmen ücretleri girilmediği için boşsa sıfır sayılır"} error={fieldError("teacherRatePerLesson")} />
          )}
          {draft.kind === "Incentive" && (
            <>
              <NumberField label="İndirim" prefix="%" value={draft.discountPercent} onChange={(value) => set("discountPercent", value)} error={fieldError("discountPercent")} min={1} />
              <NumberField label="Kaç ay indirimli" value={draft.discountMonths} onChange={(value) => set("discountMonths", value)} error={fieldError("discountMonths")} min={1} />
              <NumberField label="Bunların kaçı indirimsiz de gelirdi" prefix="%" value={draft.alreadyComingPercent} onChange={(value) => set("alreadyComingPercent", value)}
                hint="tahminen; onlara verilen indirim boşa gider" error={fieldError("alreadyComingPercent")} min={0} />
            </>
          )}
          {draft.kind === "PriceChange" && (
            <>
              <NumberField label="Fiyat ne kadar değişsin" prefix="%" value={draft.priceChangePercent} onChange={(value) => set("priceChangePercent", value)}
                hint="zam için 10, indirim için −10 gibi" error={fieldError("priceChangePercent")} />
              <NumberField label="Kaç öğrenci ayrılabilir" value={draft.lostStudents} onChange={(value) => set("lostStudents", value)} error={fieldError("lostStudents")} min={0} />
            </>
          )}
          {draft.kind === "Custom" && (
            <NumberField label="Ayda ne kazandırır (tahminin)" prefix="₺" value={draft.monthlyAmount} onChange={(value) => set("monthlyAmount", value)} error={fieldError("monthlyAmount")} />
          )}
          {draft.kind !== "PriceChange" && (
            <NumberField label="Tek seferlik maliyet" prefix="₺" value={draft.oneTimeCost} onChange={(value) => set("oneTimeCost", value)}
              hint="reklam, malzeme gibi bir kerelik masraf" placeholder="0" error={fieldError("oneTimeCost")} min={0} />
          )}
        </div>

        <Field label="Başlık" error={fieldError("title")}>
          <input className="field" value={draft.title} maxLength={120} placeholder={suggestTitle(draft, instrumentName)} onChange={(event) => set("title", event.target.value)} />
        </Field>
        <Field label="Not (isteğe bağlı)">
          <textarea className="field min-h-16" value={draft.note} maxLength={1000} onChange={(event) => set("note", event.target.value)} />
        </Field>

        <PreviewCard evaluation={preview} stale={previewError !== null} />
        {previewError?.[""] && <FormMessage tone="error">{previewError[""][0]}</FormMessage>}
        {save.error && !(save.error instanceof ApiError && save.error.errors) && (
          <FormMessage tone="error">{save.error instanceof ApiError ? save.error.detail ?? save.error.title : "Fikir kaydedilemedi."}</FormMessage>
        )}
        <FormActions onCancel={onClose} submitLabel={initial ? "Kaydet" : "Fikri kaydet"} pending={save.isPending} />
      </form>
    </Modal>
  );
}

function PreviewCard({ evaluation, stale }: { evaluation: IdeaEvaluation | null; stale: boolean }) {
  if (!evaluation) return <div className="skeleton h-36 rounded-xl" />;
  const max = Math.max(1, ...evaluation.series.map(Math.abs));
  const hasNegative = evaluation.series.some((value) => value < 0);
  const height = 56;
  const zero = hasNegative ? height / 2 : height;
  const scale = hasNegative ? height / 2 : height;
  const payback = evaluation.paybackMonth
    ? `masrafını ${evaluation.paybackMonth}. ayda çıkarır`
    : evaluation.series[0] < 0 ? "bir yılda masrafını çıkarmıyor" : "ilk aydan kazandırır";

  return (
    <section aria-label="Ne kazandırır" className={`rounded-xl border border-[var(--line)] p-3 ${stale ? "opacity-50" : ""}`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <p className="text-micro text-[var(--muted)]">Ayda kazandıracağı</p>
          <p className={`text-xl font-bold tabular-nums ${evaluation.monthlyNet < 0 ? "text-[var(--danger-strong)]" : "text-[var(--success-strong)]"}`}>{signedTl(evaluation.monthlyNet)}</p>
          <p className="text-meta text-[.75rem]">Bir yılda {signedTl(evaluation.twelveMonthNet)} · {payback}</p>
        </div>
        <Badge tone={VERDICT_TONE[evaluation.verdict]}>{VERDICT_LABEL[evaluation.verdict]}</Badge>
      </div>
      <svg viewBox={`0 0 480 ${height + 14}`} className="mt-2 h-auto w-full" role="img"
        aria-label={`Önümüzdeki 12 ay her ay kazandıracağı: ${evaluation.series.map((value, index) => `${index + 1}. ay ${tl(value)}`).join(", ")}`}>
        <line x1="0" x2="480" y1={zero} y2={zero} stroke="var(--line)" />
        {evaluation.series.map((value, index) => {
          const h = (Math.abs(value) / max) * scale;
          return (
            <g key={index}>
              <title>{`${index + 1}. ay: ${tl(value)}`}</title>
              <rect x={index * 40 + 8} y={value >= 0 ? zero - h : zero} width="24" height={Math.max(0.5, h)} rx="3" fill={value < 0 ? SERIES.teacher : SERIES.net} />
              <text x={index * 40 + 20} y={height + 12} textAnchor="middle" fontSize="10" fill="var(--muted)">{index + 1}. ay</text>
            </g>
          );
        })}
      </svg>
      <ul className="mt-2 space-y-1">
        {evaluation.reasons.map((reason) => <li key={reason} className="text-[.75rem] leading-snug text-[var(--muted)]">• {reason}</li>)}
      </ul>
    </section>
  );
}

function Field({ label, hint, error, children }: { label: string; hint?: string; error?: string; children: ReactNode }) {
  return (
    <label className="block min-w-0">
      <span className="text-micro text-[var(--muted)]">{label}</span>
      <div className="mt-1">{children}</div>
      {error ? <span className="mt-1 block text-[.72rem] font-semibold text-[var(--danger-strong)]">{error}</span>
        : hint ? <span className="text-meta mt-1 block text-[.72rem]">{hint}</span> : null}
    </label>
  );
}

function NumberField({ label, value, onChange, prefix, placeholder, hint, error, min }: {
  label: string; value: string; onChange: (value: string) => void; prefix?: string; placeholder?: string; hint?: string; error?: string; min?: number;
}) {
  return (
    <Field label={label} hint={hint} error={error}>
      <div className="relative">
        {prefix && <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-sm font-bold text-[var(--muted)]">{prefix}</span>}
        <input type="number" inputMode="decimal" min={min} step="any" className={`field tabular-nums ${prefix ? "pl-7" : ""}`}
          value={value} placeholder={placeholder} onChange={(event) => onChange(event.target.value)} />
      </div>
    </Field>
  );
}
