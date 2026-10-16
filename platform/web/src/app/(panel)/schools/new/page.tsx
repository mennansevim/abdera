"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState, type ChangeEvent, type ReactNode } from "react";
import { Icon } from "@/components/icons";
import { IntegrationFields } from "@/components/integration-fields";
import { ModulePicker } from "@/components/module-picker";
import { SchoolAvatar } from "@/components/school-bits";
import { Badge, FormMessage, LoadError, PageHeader, Panel, ProgressBar, Skeleton } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { formatDate, readableTextOn } from "@/lib/format";
import { HOST_STATUS_LABEL, useHosts, type ServerHost } from "@/lib/hosts";
import { INTEGRATION_FIELDS, INTEGRATION_TITLES, MODULE_DESCRIPTIONS, OPS_ALERT_FIELD, isModuleOn, optionalOnly } from "@/lib/modules";
import { useReleaseCategories, useReleaseModules, useReleases, type CategoryEntry, type ModuleEntry } from "@/lib/releases";
import { checkSlug, useCreateSchool } from "@/lib/schools";
import {
  LOGO_TYPES,
  MAX_LOGO_BYTES,
  STEPS,
  buildRequest,
  customCategoryKey,
  initialState,
  integrationsFor,
  slugFormatError,
  suggestSlug,
  validateStep,
  type Errors,
  type StepKey,
  type TariffDraft,
  type ValidationContext,
  type WizardState,
} from "./wizard-state";

// Platformun okul başına ayırdığı bellek (Capacity:SchoolReserveMb varsayılanı). Yalnızca
// bilgi amaçlı tahmin; asıl seçimi kurulumun "Sunucu seç" adımı yapar.
const SCHOOL_RESERVE_MB = 768;

// `+` → yeni okul sihirbazı (ARCHITECTURE.md §4.2). On adım, durum bellekte; "İleri"
// adımın doğrulamasından geçmeden ilerlemez, "Geri" her zaman serbest. Son adım isteği
// gönderir ve okul detayına (canlı kurulum ilerlemesi) yönlendirir.
export default function NewSchoolPage() {
  const router = useRouter();
  const [state, setState] = useState<WizardState>(initialState);
  const [stepIndex, setStepIndex] = useState(0);
  const [showErrors, setShowErrors] = useState(false);
  const create = useCreateSchool();

  const update = (patch: Partial<WizardState>) => setState((current) => ({ ...current, ...patch }));

  // Kısaltma denetimi: yazmayı 400 ms bekler, sonra /check-slug.
  const [debouncedSlug, setDebouncedSlug] = useState("");
  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedSlug(state.slug), 400);
    return () => window.clearTimeout(timer);
  }, [state.slug]);
  const slugQuery = useQuery({
    queryKey: ["check-slug", debouncedSlug],
    queryFn: () => checkSlug(debouncedSlug),
    enabled: slugFormatError(debouncedSlug) === null,
    staleTime: 5_000,
  });
  const slugCheck = debouncedSlug === state.slug ? slugQuery.data : undefined;
  const slugChecking = debouncedSlug !== state.slug || slugQuery.isFetching;

  const releases = useReleases();
  const catalog = useReleaseModules(state.version || null);
  const categories = useReleaseCategories(state.version || null);

  const context: ValidationContext = { slugCheck, slugChecking, catalog: catalog.data };
  const step = STEPS[stepIndex];
  const errors = validateStep(step.key, state, context);
  const visibleErrors = showErrors ? errors : {};

  // Özet adımında bütün adımlar yeniden denetlenir: geri dönüp değiştirilen bir alan
  // sonraki bir adımı geçersiz kılmış olabilir (ör. Grup dersleri sonradan açıldı).
  const invalidSteps = useMemo(
    () => STEPS.slice(0, -1).filter((item) => Object.keys(validateStep(item.key, state, { slugCheck, slugChecking, catalog: catalog.data })).length > 0),
    [state, slugCheck, slugChecking, catalog.data],
  );

  function next() {
    if (Object.keys(errors).length > 0) {
      setShowErrors(true);
      return;
    }
    setShowErrors(false);
    setStepIndex((index) => Math.min(index + 1, STEPS.length - 1));
  }

  function back() {
    setShowErrors(false);
    setStepIndex((index) => Math.max(index - 1, 0));
  }

  function submit() {
    if (!catalog.data || invalidSteps.length > 0) return;
    create.mutate(buildRequest(state, catalog.data), {
      onSuccess: (result) => router.push(`/schools/${result.id}`),
    });
  }

  return (
    <div className="grid gap-5">
      <PageHeader
        title="Yeni okul"
        description="Okulu adım adım tanımla: kimlik, alanlar ve dersler, eklentiler, kurallar. Platform sunucuyu seçip kurulumu başlatır."
        actions={<Link href="/" className="btn btn-quiet"><Icon name="arrow-left" className="h-4 w-4" />Okullar</Link>}
      />

      <nav aria-label="Sihirbaz adımları" className="app-card overflow-x-auto p-2">
        <ol className="flex min-w-max gap-1">
          {STEPS.map((item, index) => {
            const state = index === stepIndex ? "current" : index < stepIndex ? "done" : "todo";
            return (
              <li key={item.key}>
                <button
                  type="button"
                  disabled={index > stepIndex}
                  onClick={() => { setShowErrors(false); setStepIndex(index); }}
                  aria-current={state === "current" ? "step" : undefined}
                  className={`pressable flex min-h-9 items-center gap-2 rounded-xl px-2.5 text-xs font-bold disabled:cursor-default ${state === "current" ? "bg-[var(--brand-soft)] text-[var(--brand-strong)]" : state === "done" ? "text-[var(--foreground)] hover:bg-[var(--surface-muted)]" : "text-[var(--muted)]"}`}
                >
                  <span className={`grid h-5 w-5 place-items-center rounded-full text-[.68rem] ${state === "done" ? "bg-[var(--success)] text-white" : state === "current" ? "bg-[var(--brand-strong)] text-white" : "bg-[var(--surface-muted)]"}`}>
                    {state === "done" ? <Icon name="check" className="h-3 w-3" /> : index + 1}
                  </span>
                  {item.title}
                </button>
              </li>
            );
          })}
        </ol>
      </nav>

      <Panel title={`${stepIndex + 1}. ${step.title}`} meta={`${stepIndex + 1} / ${STEPS.length}`}>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            if (step.key === "summary") submit();
            else next();
          }}
          className="grid gap-5"
          noValidate
        >
          <StepBody
            step={step.key}
            state={state}
            update={update}
            errors={visibleErrors}
            slugCheck={slugCheck}
            slugChecking={slugChecking}
            releases={releases}
            catalog={catalog}
            categories={categories}
            invalidSteps={invalidSteps.map((item) => item.title)}
            onGoTo={(key) => setStepIndex(STEPS.findIndex((item) => item.key === key))}
          />

          {showErrors && Object.keys(errors).length > 0 && (
            <FormMessage tone="error">Devam etmeden önce işaretli alanları düzelt.</FormMessage>
          )}
          {create.error && <FormMessage tone="error">{errorMessage(create.error)}</FormMessage>}

          <div className="flex flex-wrap items-center justify-between gap-2 border-t border-[var(--line)] pt-4">
            <button type="button" onClick={back} disabled={stepIndex === 0} className="btn btn-quiet">
              <Icon name="arrow-left" className="h-4 w-4" />Geri
            </button>
            {step.key === "summary" ? (
              <button type="submit" className="btn btn-primary" disabled={create.isPending || invalidSteps.length > 0 || !catalog.data}>
                <Icon name="rocket" className="h-4 w-4" />{create.isPending ? "Kurulum başlatılıyor…" : "Okulu kur"}
              </button>
            ) : (
              <button type="submit" className="btn btn-primary">
                İleri<Icon name="arrow-right" className="h-4 w-4" />
              </button>
            )}
          </div>
        </form>
      </Panel>
    </div>
  );
}

// --- Ortak alan sarmalayıcısı ---

function Field({ label, error, hint, children, className = "" }: { label: ReactNode; error?: string; hint?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <label className={`form-label ${className}`}>
      {label}
      {children}
      {error ? <span className="form-error">{error}</span> : hint ? <span className="form-hint">{hint}</span> : null}
    </label>
  );
}

interface StepProps {
  step: StepKey;
  state: WizardState;
  update: (patch: Partial<WizardState>) => void;
  errors: Errors;
  slugCheck: ReturnType<typeof useQuery<Awaited<ReturnType<typeof checkSlug>>>>["data"];
  slugChecking: boolean;
  releases: ReturnType<typeof useReleases>;
  catalog: ReturnType<typeof useReleaseModules>;
  categories: ReturnType<typeof useReleaseCategories>;
  invalidSteps: string[];
  onGoTo: (key: StepKey) => void;
}

function StepBody(props: StepProps) {
  switch (props.step) {
    case "identity": return <IdentityStep {...props} />;
    case "version": return <VersionStep {...props} />;
    case "branches": return <BranchesStep {...props} />;
    case "modules": return <ModulesStep {...props} />;
    case "admin": return <AdminStep {...props} />;
    case "policy": return <PolicyStep {...props} />;
    case "billing": return <BillingStep {...props} />;
    case "integrations": return <IntegrationsStep {...props} />;
    case "host": return <HostStep />;
    case "summary": return <SummaryStep {...props} />;
  }
}

// --- 1. Kimlik ---

function IdentityStep({ state, update, errors, slugCheck, slugChecking }: StepProps) {
  const [logoError, setLogoError] = useState<string | null>(null);
  const slugError = slugFormatError(state.slug);
  const defaultDomain = slugCheck?.domain ?? "";
  const domain = state.customDomain.trim().toLowerCase() || defaultDomain;

  function onNameChange(value: string) {
    update(state.slugTouched ? { displayName: value } : { displayName: value, slug: suggestSlug(value) });
  }

  function onLogo(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) return;
    if (!LOGO_TYPES.includes(file.type)) {
      setLogoError("Logo PNG, JPEG ya da WebP olmalı.");
      return;
    }
    if (file.size > MAX_LOGO_BYTES) {
      setLogoError("Logo en fazla 512 KB olabilir.");
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      const dataUrl = String(reader.result);
      setLogoError(null);
      update({ logoBase64: dataUrl.split(",")[1] ?? null, logoPreview: dataUrl, logoName: file.name });
    };
    reader.onerror = () => setLogoError("Dosya okunamadı.");
    reader.readAsDataURL(file);
  }

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <Field label="Okul adı" error={errors.displayName}>
        <input className="field" value={state.displayName} onChange={(event) => onNameChange(event.target.value)} maxLength={120} aria-invalid={Boolean(errors.displayName)} autoFocus />
      </Field>
      <Field label="Kısa ad" error={errors.shortName} hint="Menüde ve mesajlarda görünür (örn. “Do Re Mi”).">
        <input className="field" value={state.shortName} onChange={(event) => update({ shortName: event.target.value })} maxLength={40} aria-invalid={Boolean(errors.shortName)} />
      </Field>

      <Field
        label="Kısaltma (alt alan adı)"
        error={errors.slug && errors.slug !== "Kısaltma denetleniyor…" ? errors.slug : undefined}
        hint={
          slugError && state.slug ? <span className="text-[var(--danger-strong)]">{slugError}</span>
            : slugChecking ? "Denetleniyor…"
            : slugCheck ? (slugCheck.available ? <span className="font-semibold text-[var(--success-strong)]">Uygun · {slugCheck.domain}</span> : <span className="font-semibold text-[var(--danger-strong)]">{slugCheck.reason}</span>)
            : "Küçük harf, rakam ve tire; 3-31 karakter."
        }
      >
        <input
          className="field font-mono"
          value={state.slug}
          onChange={(event) => update({ slug: event.target.value.toLowerCase().replace(/[^a-z0-9-]/g, ""), slugTouched: true })}
          maxLength={31}
          spellCheck={false}
          autoCapitalize="off"
          aria-invalid={Boolean(errors.slug)}
        />
      </Field>
      <Field label={<>Özel alan adı <span className="font-medium">(isteğe bağlı)</span></>} error={errors.customDomain} hint="Boşsa okul alt alan adında açılır. Özel alan adı için okulun DNS'inde CNAME kaydı gerekir.">
        <input className="field font-mono" value={state.customDomain} onChange={(event) => update({ customDomain: event.target.value })} placeholder="panel.okulum.com" spellCheck={false} autoCapitalize="off" aria-invalid={Boolean(errors.customDomain)} />
      </Field>

      {domain && (
        <p className="sm:col-span-2 rounded-xl bg-[var(--surface-muted)] px-3 py-2 text-xs">
          Okulun adresi: <span className="font-mono font-bold">https://{domain}</span>
        </p>
      )}

      <Field label="Ana renk" error={errors.primaryColor} hint="Menü ve düğmelerde kullanılır.">
        <span className="mt-[.3rem] flex items-center gap-2">
          <input type="color" value={/^#[0-9a-f]{6}$/i.test(state.primaryColor) ? state.primaryColor : "#a84e1f"} onChange={(event) => update({ primaryColor: event.target.value })} className="h-10 w-12 shrink-0 cursor-pointer rounded-lg border border-[var(--line)] bg-transparent p-1" aria-label="Renk seç" />
          <input className="field font-mono" value={state.primaryColor} onChange={(event) => update({ primaryColor: event.target.value.trim() })} maxLength={7} aria-invalid={Boolean(errors.primaryColor)} />
        </span>
      </Field>

      <div className="form-label">
        Logo <span className="font-medium">(isteğe bağlı)</span>
        <div className="mt-[.3rem] flex items-center gap-3">
          {state.logoPreview ? (
            // Önizleme yerel data: URL'idir; next/image burada gereksiz.
            // eslint-disable-next-line @next/next/no-img-element
            <img src={state.logoPreview} alt="" className="h-12 w-12 shrink-0 rounded-xl border border-[var(--line)] bg-white object-contain" />
          ) : (
            <SchoolAvatar name={state.shortName || state.displayName || "?"} color={state.primaryColor} />
          )}
          <label className="btn btn-quiet cursor-pointer">
            <Icon name="upload" className="h-4 w-4" />{state.logoName ? "Değiştir" : "Yükle"}
            <input type="file" accept={LOGO_TYPES.join(",")} onChange={onLogo} className="sr-only" />
          </label>
          {state.logoName && (
            <button type="button" className="btn btn-quiet" onClick={() => update({ logoBase64: null, logoPreview: null, logoName: null })}>Kaldır</button>
          )}
        </div>
        {logoError ? <span className="form-error">{logoError}</span> : <span className="form-hint">{state.logoName ?? "PNG, JPEG ya da WebP; en fazla 512 KB, kare önerilir."}</span>}
      </div>

      <div className="sm:col-span-2 flex items-center gap-3 rounded-xl border border-[var(--line)] p-3">
        <span className="text-meta">Önizleme:</span>
        <span className="rounded-lg px-3 py-1.5 text-xs font-bold" style={{ background: state.primaryColor, color: readableTextOn(state.primaryColor) }}>
          {state.shortName || "Kısa ad"}
        </span>
      </div>

      <Field label={<>Resmî unvan <span className="font-medium">(isteğe bağlı)</span></>} error={errors.legalName} hint="KVKK metninde veri sorumlusu olarak görünür.">
        <input className="field" value={state.legalName} onChange={(event) => update({ legalName: event.target.value })} maxLength={200} />
      </Field>
      <Field label={<>İletişim e-postası <span className="font-medium">(isteğe bağlı)</span></>} error={errors.contactEmail} hint="KVKK başvuruları için.">
        <input className="field" type="email" value={state.contactEmail} onChange={(event) => update({ contactEmail: event.target.value })} aria-invalid={Boolean(errors.contactEmail)} />
      </Field>
    </div>
  );
}

// --- 2. Sürüm ---

function VersionStep({ state, update, errors, releases }: StepProps) {
  const available = (releases.data ?? []).filter((release) => !release.isWithdrawn);
  if (releases.isLoading) return <Skeleton className="h-32" />;
  if (releases.error) return <LoadError message={errorMessage(releases.error)} onRetry={() => releases.refetch()} />;
  if (available.length === 0) {
    return <FormMessage tone="error">Kayıtlı (geri çekilmemiş) sürüm yok. Önce Sürümler ekranından CI&apos;ın ürettiği bir sürümü kaydet.</FormMessage>;
  }
  return (
    <div className="grid gap-4">
      <fieldset className="grid gap-2">
        <legend className="form-label mb-2">Kurulacak sürüm</legend>
        {available.map((release, index) => (
          <label key={release.version} className={`flex cursor-pointer items-start gap-3 rounded-2xl border p-3 ${state.version === release.version ? "border-[var(--brand)] bg-[var(--brand-soft)]/40" : "border-[var(--line)]"}`}>
            <input type="radio" name="version" className="mt-1 accent-[var(--brand-strong)]" checked={state.version === release.version} onChange={() => update({ version: release.version })} />
            <span className="min-w-0 flex-1">
              <span className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-sm font-bold">v{release.version}</span>
                {index === 0 && <Badge tone="success">En yeni</Badge>}
                <span className="text-meta">{formatDate(release.releasedAt)} · {release.schoolCount} okulda</span>
              </span>
              {release.notes && <span className="text-meta mt-1 block whitespace-pre-line">{release.notes}</span>}
            </span>
          </label>
        ))}
      </fieldset>
      {errors.version && <span className="form-error">{errors.version}</span>}
      <label className="flex items-start gap-3 rounded-2xl border border-[var(--line)] p-3">
        <input type="checkbox" className="mt-1 accent-[var(--brand-strong)]" checked={state.isCanary} onChange={(event) => update({ isCanary: event.target.checked })} />
        <span>
          <span className="block text-sm font-bold">Kanarya okul</span>
          <span className="text-meta block">Filo güncellemelerinde ilk dalgada güncellenir; yeni sürümü önce bu okul dener.</span>
        </span>
      </label>
    </div>
  );
}

// --- 3. Modüller ---

// --- 3. Alanlar ve dersler (§5.6) ---
// Kategori seç (Müzik, Dans, Spor...), altındaki önerilerden ekle ya da kendi adını yaz. Okul yöneticisi
// sonradan Ayarlar → Eğitim alanları'ndan değiştirebilir. Ekranlardaki terim buna göre değişir.

function BranchesStep({ state, update, errors, categories }: StepProps) {
  const [custom, setCustom] = useState("");
  const [customCategory, setCustomCategory] = useState<string>("");
  const [newCategory, setNewCategory] = useState<{ name: string; unitLabel: string } | null>(null);
  const [newCategoryError, setNewCategoryError] = useState<string | null>(null);
  if (categories.isLoading) return <Skeleton className="h-48" />;
  if (categories.error) return <LoadError message={errorMessage(categories.error)} onRetry={() => categories.refetch()} />;
  // Kataloğu olmayan eski sürüm: yalnızca serbest ad, kategori "Diğer".
  const releaseCatalog: CategoryEntry[] = categories.data && categories.data.length > 0
    ? categories.data
    : [{ key: "other", name: "Diğer", unitLabel: "Ders", unitLabelPlural: "Dersler", suggestions: [] }];
  // Elle açılan alanlar katalogdan sonra; önerisi yok, dersleri serbest yazılır.
  const catalog: CategoryEntry[] = [
    ...releaseCatalog,
    ...state.customCategories.map((c) => ({ key: c.key, name: c.name, unitLabel: c.unitLabel || "Ders", unitLabelPlural: c.unitLabel || "Ders", suggestions: [] })),
  ];
  const isCustom = (key: string) => state.customCategories.some((c) => c.key === key);

  function createCategory() {
    const name = newCategory?.name.trim() ?? "";
    if (!name) return;
    const lower = name.toLocaleLowerCase("tr-TR");
    if (catalog.some((c) => c.name.toLocaleLowerCase("tr-TR") === lower)) { setNewCategoryError(`“${name}” alanı zaten var.`); return; }
    let key = customCategoryKey(name);
    for (let n = 2; catalog.some((c) => c.key === key); n += 1) key = `${customCategoryKey(name).slice(0, 36)}-${n}`;
    const next = new Set(state.categories);
    next.add(key);
    update({ categories: next, customCategories: [...state.customCategories, { key, name, unitLabel: newCategory!.unitLabel.trim() || "Ders" }] });
    setCustomCategory(key);
    setNewCategory(null);
    setNewCategoryError(null);
  }

  function removeCustomCategory(key: string) {
    const next = new Set(state.categories);
    next.delete(key);
    update({
      categories: next,
      customCategories: state.customCategories.filter((c) => c.key !== key),
      branches: state.branches.filter((b) => b.category !== key),
    });
  }
  const has = (name: string) => state.branches.some((b) => b.name.trim().toLocaleLowerCase("tr-TR") === name.trim().toLocaleLowerCase("tr-TR"));
  const selectedCategories = catalog.filter((category) => state.categories.has(category.key));
  const targetCategory = customCategory && state.categories.has(customCategory) ? customCategory : selectedCategories[0]?.key ?? catalog[0].key;
  const unitOf = (key: string) => catalog.find((c) => c.key === key)?.unitLabel ?? "Ders";
  const term = new Set(state.branches.map((b) => b.category)).size === 1 ? unitOf(state.branches[0].category) : "Branş";

  function toggleCategory(key: string) {
    const next = new Set(state.categories);
    if (next.has(key)) {
      next.delete(key);
      update({ categories: next, branches: state.branches.filter((b) => b.category !== key) });
    } else {
      next.add(key);
      update({ categories: next });
    }
  }

  function toggleBranch(category: string, name: string) {
    update({ branches: has(name) ? state.branches.filter((b) => b.name.trim().toLocaleLowerCase("tr-TR") !== name.toLocaleLowerCase("tr-TR")) : [...state.branches, { category, name }] });
  }

  function addCustom() {
    const name = custom.trim();
    if (!name || has(name)) return;
    const next = new Set(state.categories);
    next.add(targetCategory);
    update({ categories: next, branches: [...state.branches, { category: targetCategory, name }] });
    setCustom("");
  }

  return (
    <div className="grid gap-4">
      <p className="text-meta">Okul ne öğretiyor? Bir ya da birden fazla alan seç; her alanın altındaki derslerden ekle ya da kendi dersini yaz. Bireysel ve grup (çoklu) ders her alanda standart.</p>
      <div>
        <h3 className="text-micro text-[var(--muted)]">Alanlar</h3>
        <div className="mt-2 flex flex-wrap gap-2" role="group" aria-label="Alanlar">
          {catalog.map((category) => {
            const on = state.categories.has(category.key);
            return (
              <button key={category.key} type="button" onClick={() => (isCustom(category.key) ? removeCustomCategory(category.key) : toggleCategory(category.key))} aria-pressed={on}
                title={isCustom(category.key) ? "Elle açılan alan - kaldırmak için tıkla" : undefined}
                className={`pressable rounded-2xl border px-3 py-2 text-left ${on ? "border-[var(--brand)] bg-[var(--brand-soft)]/50" : "border-[var(--line)]"}`}>
                <span className="block text-sm font-bold">{category.name}</span>
                <span className="text-meta">{isCustom(category.key) ? `Yeni alan · ${category.unitLabel}` : category.unitLabelPlural}</span>
              </button>
            );
          })}
          <button type="button" onClick={() => { setNewCategory(newCategory ? null : { name: "", unitLabel: "Ders" }); setNewCategoryError(null); }} aria-expanded={newCategory !== null}
            className="pressable rounded-2xl border border-dashed border-[var(--line-strong)] px-3 py-2 text-left text-[var(--brand-strong)]">
            <span className="block text-sm font-bold">+ Yeni alan</span>
            <span className="text-meta">Listede yoksa oluştur</span>
          </button>
        </div>
        {newCategory && (
          <div className="mt-3 grid gap-2 rounded-2xl border border-[var(--line)] p-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
            <Field label="Alan adı" hint="ör. Yoga ve meditasyon, Mutfak atölyesi">
              <input className="field" value={newCategory.name} maxLength={60} autoFocus onChange={(event) => setNewCategory({ ...newCategory, name: event.target.value })}
                onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); createCategory(); } }} />
            </Field>
            <Field label="Alt birim adı" hint="Ekranlarda görünecek terim: Ders, Atölye, Seans…">
              <input className="field" value={newCategory.unitLabel} maxLength={40} onChange={(event) => setNewCategory({ ...newCategory, unitLabel: event.target.value })} />
            </Field>
            <button type="button" onClick={createCategory} disabled={!newCategory.name.trim()} className="btn btn-primary">Alanı ekle</button>
            {newCategoryError && <span className="form-error sm:col-span-3">{newCategoryError}</span>}
          </div>
        )}
      </div>

      {selectedCategories.map((category) => (
        <div key={category.key} className="rounded-2xl border border-[var(--line)] p-3">
          <p className="text-sm font-bold">{category.name} · {category.unitLabelPlural}</p>
          <div className="mt-2 flex flex-wrap gap-1.5">
            {category.suggestions.map((name) => {
              const on = has(name);
              return (
                <button key={name} type="button" onClick={() => toggleBranch(category.key, name)} aria-pressed={on}
                  className={`pressable rounded-full border px-3 py-1 text-sm font-semibold ${on ? "border-[var(--brand)] bg-[var(--brand)] text-white" : "border-dashed border-[var(--line)]"}`}>
                  {on ? <Icon name="check" className="mr-1 inline h-3.5 w-3.5" /> : "+ "}{name}
                </button>
              );
            })}
            {state.branches.filter((b) => b.category === category.key && !category.suggestions.some((s) => s.toLocaleLowerCase("tr-TR") === b.name.trim().toLocaleLowerCase("tr-TR"))).map((b) => (
              <button key={b.name} type="button" onClick={() => toggleBranch(category.key, b.name)} aria-pressed
                className="pressable rounded-full border border-[var(--brand)] bg-[var(--brand)] px-3 py-1 text-sm font-semibold text-white" title="Kaldırmak için tıkla">
                <Icon name="check" className="mr-1 inline h-3.5 w-3.5" />{b.name}
              </button>
            ))}
          </div>
        </div>
      ))}

      <div className="grid gap-2 sm:grid-cols-[1fr_auto_auto] sm:items-end">
        <Field label="Listede olmayan bir ders ekle" hint="Ör. Yaratıcı drama, İngilizce - Konuşma, Satranç">
          <input className="field" value={custom} maxLength={100} onChange={(event) => setCustom(event.target.value)}
            onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); addCustom(); } }} />
        </Field>
        <Field label="Alan">
          <select className="field" value={targetCategory} onChange={(event) => setCustomCategory(event.target.value)}>
            {catalog.map((category) => <option key={category.key} value={category.key}>{category.name}</option>)}
          </select>
        </Field>
        <button type="button" onClick={addCustom} disabled={!custom.trim()} className="btn btn-quiet">Ekle</button>
      </div>

      <p className="text-meta">
        {state.branches.length === 0 ? "Henüz ders eklenmedi." : `${state.branches.length} ders seçildi. Okulun ekranlarında “${term}” olarak görünecek.`}
      </p>
      {errors.branches && <span className="form-error">{errors.branches}</span>}
    </div>
  );
}

// --- 4. Eklentiler ---

function ModulesStep({ state, update, errors, catalog }: StepProps) {
  if (catalog.isLoading) return <Skeleton className="h-48" />;
  if (catalog.error) return <LoadError message={errorMessage(catalog.error)} onRetry={() => catalog.refetch()} />;
  if (!catalog.data) return <span className="form-error">{errors.modules}</span>;
  return (
    <div className="grid gap-3">
      <p className="text-meta">Standart paket her okulda açık. Yalnızca ücretli eklentiler (dış sağlayıcı maliyeti olanlar) seçilir; sonradan okul detayından açılıp kapatılabilir, kapatmak veriyi silmez.</p>
      <ModulePicker catalog={catalog.data} selected={state.modules} onChange={(modules) => update({ modules })} />
    </div>
  );
}

// --- 4. Yönetici ---

function AdminStep({ state, update, errors }: StepProps) {
  return (
    <div className="grid gap-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Yönetici e-postası" error={errors.adminEmail}>
          <input className="field" type="email" value={state.adminEmail} onChange={(event) => update({ adminEmail: event.target.value })} aria-invalid={Boolean(errors.adminEmail)} autoFocus />
        </Field>
        <Field label={<>Görünen ad <span className="font-medium">(isteğe bağlı)</span></>} error={errors.adminDisplayName}>
          <input className="field" value={state.adminDisplayName} onChange={(event) => update({ adminDisplayName: event.target.value })} maxLength={100} />
        </Field>
      </div>
      <FormMessage tone="info">
        Geçici parolayı platform üretir ve şifreli saklar. Kurulumdan sonra okul detayındaki “Geçici parolayı göster” ile görebilirsin (her görüntüleme denetim kaydına yazılır). Yönetici ilk girişte parolasını değiştirmek zorundadır.
      </FormMessage>
    </div>
  );
}

// --- 5. Okul kuralları ---

function NumberField({ label, value, onChange, error, hint, suffix }: { label: string; value: string; onChange: (value: string) => void; error?: string; hint?: string; suffix?: string }) {
  return (
    <Field label={label} error={error} hint={hint}>
      <span className="relative mt-[.3rem] block">
        <input className={`field ${suffix ? "pr-14" : ""}`} inputMode="decimal" value={value} onChange={(event) => onChange(event.target.value)} aria-invalid={Boolean(error)} />
        {suffix && <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-xs font-bold text-[var(--muted)]">{suffix}</span>}
      </span>
    </Field>
  );
}

function PolicyStep({ state, update, errors, catalog }: StepProps) {
  const groups = isModuleOn("groups", state.modules, catalog.data);
  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {groups && (
        <NumberField label="Grup dersi öğretmen payı" suffix="%" value={state.groupLessonSharePercent} onChange={(value) => update({ groupLessonSharePercent: value })} error={errors.groupLessonSharePercent} hint="Grup aidatlarının öğretmene aylık giden payı." />
      )}
      <NumberField label="Haftalık ders üst sınırı" suffix="ders" value={state.maxWeeklyLessons} onChange={(value) => update({ maxWeeklyLessons: value })} error={errors.maxWeeklyLessons} hint="Bir öğrencinin haftada alabileceği en çok ders." />
      <NumberField label="Telafi için iptal bildirimi" suffix="saat" value={state.makeupNoticeHours} onChange={(value) => update({ makeupNoticeHours: value })} error={errors.makeupNoticeHours} hint="Dersten en az bu kadar önce iptal edilirse telafi hakkı doğar." />
      <NumberField label="Telafi hakkı geçerliliği" suffix="gün" value={state.makeupCreditValidDays} onChange={(value) => update({ makeupCreditValidDays: value })} error={errors.makeupCreditValidDays} />
      <NumberField label="Mazeretli telafi penceresi" suffix="gün" value={state.excusedMakeupWindowDays} onChange={(value) => update({ excusedMakeupWindowDays: value })} error={errors.excusedMakeupWindowDays} hint="Mazeretli yoklamadan sonra telafinin planlanabileceği süre." />
      <div className="grid grid-cols-2 gap-3">
        <Field label="Okul günü başlangıcı" error={errors.schoolDayStart}>
          <input className="field" type="time" value={state.schoolDayStart} onChange={(event) => update({ schoolDayStart: event.target.value })} aria-invalid={Boolean(errors.schoolDayStart)} />
        </Field>
        <Field label="Okul günü bitişi" error={errors.schoolDayEnd}>
          <input className="field" type="time" value={state.schoolDayEnd} onChange={(event) => update({ schoolDayEnd: event.target.value })} aria-invalid={Boolean(errors.schoolDayEnd)} />
        </Field>
      </div>
      <p className="text-meta sm:col-span-2">Varsayılanlar önerilen başlangıç değerleridir; okul yöneticisi bunları sonradan Ayarlar&apos;dan değiştirebilir.</p>
    </div>
  );
}

// --- 6. Aidat ---

function TariffCard({ title, prefix, draft, onChange, errors }: { title: string; prefix: string; draft: TariffDraft; onChange: (draft: TariffDraft) => void; errors: Errors }) {
  return (
    <fieldset className="grid gap-3 rounded-2xl border border-[var(--line)] p-3 sm:grid-cols-3 sm:p-4">
      <legend className="px-1 text-sm font-bold">{title}</legend>
      <NumberField label="Aylık ders" suffix="ders" value={draft.lessonsPerMonth} onChange={(value) => onChange({ ...draft, lessonsPerMonth: value })} error={errors[`${prefix}.lessonsPerMonth`]} />
      <NumberField label="Aylık tutar" suffix="TRY" value={draft.monthlyAmount} onChange={(value) => onChange({ ...draft, monthlyAmount: value })} error={errors[`${prefix}.monthlyAmount`]} />
      <Field label="Geçerlilik başlangıcı" error={errors[`${prefix}.effectiveFrom`]}>
        <input className="field" type="date" value={draft.effectiveFrom} onChange={(event) => onChange({ ...draft, effectiveFrom: event.target.value })} />
      </Field>
    </fieldset>
  );
}

function BillingStep({ state, update, errors, catalog }: StepProps) {
  const groups = isModuleOn("groups", state.modules, catalog.data);
  return (
    <div className="grid gap-4">
      <div className="grid gap-4 sm:grid-cols-3">
        <NumberField label="Çoklu kurs indirimi" suffix="%" value={state.multiCourseDiscountPercent} onChange={(value) => update({ multiCourseDiscountPercent: value })} error={errors.multiCourseDiscountPercent} />
        <NumberField label="Kardeş indirimi" suffix="%" value={state.siblingDiscountPercent} onChange={(value) => update({ siblingDiscountPercent: value })} error={errors.siblingDiscountPercent} />
        <NumberField label="Vade günü" suffix=". gün" value={state.dueDayOfMonth} onChange={(value) => update({ dueDayOfMonth: value })} error={errors.dueDayOfMonth} hint="Ayın kaçında ödenir (1-28)." />
      </div>
      <p className="text-meta">Birden fazla indirim uygulanabiliyorsa en yükseği alınır, toplanmaz. Aidat tutarı yalnızca ders türüne (Birebir/Grup) göre belirlenir.</p>
      <TariffCard title="Birebir tarife" prefix="individual" draft={state.individual} onChange={(individual) => update({ individual })} errors={errors} />
      {groups
        ? <TariffCard title="Grup tarifesi" prefix="group" draft={state.group} onChange={(group) => update({ group })} errors={errors} />
        : <p className="text-meta">Grup tarifesi yalnızca “Grup dersleri” modülü seçiliyse açılır.</p>}
    </div>
  );
}

// --- 7. Entegrasyonlar ---

function IntegrationsStep({ state, update, errors, catalog }: StepProps) {
  const errorList = Object.values(errors);
  return (
    <div className="grid gap-4">
      <FormMessage tone="info">
        Değerler platformun sır kasasında şifreli saklanır ve bir daha ekranda gösterilmez. Boş bırakılan sağlayıcı kapalı kalır; okul o modülü “devre dışı” olarak çalıştırır. Sonradan okul detayından eklenebilir.
      </FormMessage>
      <IntegrationFields
        modules={new Set([...state.modules, ...(catalog.data ?? []).filter((m) => m.isCore).map((m) => m.key)])}
        values={state.integrations}
        onChange={(key, value) => update({ integrations: { ...state.integrations, [key]: value } })}
      />
      {errorList.length > 0 && <span className="form-error">{errorList.join(" ")}</span>}
    </div>
  );
}

// --- 8. Sunucu ---

function predictedHost(hosts: ServerHost[]): ServerHost | undefined {
  return [...hosts]
    .filter((host) => host.status === "Active" && host.reservedMemoryMb + SCHOOL_RESERVE_MB <= host.capacityMb)
    .sort((a, b) => a.reservedMemoryMb - b.reservedMemoryMb)[0];
}

function HostStep() {
  const hosts = useHosts();
  if (hosts.isLoading) return <Skeleton className="h-32" />;
  if (hosts.error) return <LoadError message={errorMessage(hosts.error)} onRetry={() => hosts.refetch()} />;
  const list = hosts.data ?? [];
  const pick = predictedHost(list);
  return (
    <div className="grid gap-4">
      <p className="text-meta">
        Sunucuyu platform seçer: kurulumun “Sunucu seç” adımında etkin sunucular arasından en az rezerv belleği olana (en çok boş kapasite) yerleşir. Okul başına yaklaşık {SCHOOL_RESERVE_MB} MB ayrılır.
      </p>
      {!pick && <FormMessage tone="error">Şu an hiçbir etkin sunucuda yer yok. Kurulum “Sunucu seç” adımında duracak; önce Sunucular ekranından sunucu ekle.</FormMessage>}
      <div className="grid gap-2">
        {list.map((host) => (
          <div key={host.id} className={`rounded-2xl border p-3 ${pick?.id === host.id ? "border-[var(--brand)]" : "border-[var(--line)]"}`}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="flex items-center gap-2 text-sm font-bold"><Icon name="server" className="h-4 w-4" />{host.name}</span>
              <span className="flex items-center gap-1.5">
                {pick?.id === host.id && <Badge tone="brand">Muhtemel seçim</Badge>}
                <Badge tone={host.status === "Active" ? "success" : host.status === "Draining" ? "warning" : "muted"}>{HOST_STATUS_LABEL[host.status]}</Badge>
              </span>
            </div>
            <div className="mt-2"><ProgressBar value={host.reservedMemoryMb} max={host.capacityMb} tone={host.reservedMemoryMb / Math.max(1, host.capacityMb) > 0.85 ? "danger" : "brand"} label={`${host.name} kapasite`} /></div>
            <p className="text-meta mt-1">{host.reservedMemoryMb} / {host.capacityMb} MB ayrılmış · {host.schoolCount} okul · boş {Math.max(0, host.capacityMb - host.reservedMemoryMb)} MB</p>
          </div>
        ))}
        {list.length === 0 && <p className="text-meta">Kayıtlı sunucu yok.</p>}
      </div>
    </div>
  );
}

// --- 9. Özet ---

function SummaryRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-0.5 border-b border-[var(--line)] py-2 last:border-b-0 sm:grid-cols-[12rem_minmax(0,1fr)] sm:gap-3">
      <dt className="text-xs font-bold text-[var(--muted)]">{label}</dt>
      <dd className="min-w-0 break-words text-sm">{children}</dd>
    </div>
  );
}

function SummarySection({ title, step, onGoTo, children }: { title: string; step: StepKey; onGoTo: (key: StepKey) => void; children: ReactNode }) {
  return (
    <section className="rounded-2xl border border-[var(--line)] px-3 py-2 sm:px-4">
      <div className="flex items-center justify-between gap-2 py-1">
        <h3 className="text-sm font-bold">{title}</h3>
        <button type="button" onClick={() => onGoTo(step)} className="text-xs font-bold text-[var(--brand-strong)] hover:underline">Düzenle</button>
      </div>
      <dl>{children}</dl>
    </section>
  );
}

function SummaryStep({ state, slugCheck, catalog, invalidSteps, onGoTo }: StepProps) {
  const entries: ModuleEntry[] = catalog.data ?? [];
  const optional = optionalOnly(state.modules, entries);
  const groups = isModuleOn("groups", state.modules, entries);
  const integrations = integrationsFor(state, entries);
  const allFields = [...Object.values(INTEGRATION_FIELDS).flat(), OPS_ALERT_FIELD];
  const domain = state.customDomain.trim().toLowerCase() || slugCheck?.domain || state.slug;
  const name = (key: string) => entries.find((m) => m.key === key)?.name ?? key;
  const money = (value: string) => `${value.replace(".", ",")} TRY`;

  return (
    <div className="grid gap-3">
      {invalidSteps.length > 0 && (
        <FormMessage tone="error">Şu adımlarda eksik/hatalı alan var: {invalidSteps.join(", ")}. Düzeltmeden kurulum başlatılamaz.</FormMessage>
      )}
      <SummarySection title="Kimlik" step="identity" onGoTo={onGoTo}>
        <SummaryRow label="Okul">
          <span className="flex items-center gap-2"><SchoolAvatar name={state.shortName || state.displayName} color={state.primaryColor} /><span><b>{state.displayName}</b> · {state.shortName}</span></span>
        </SummaryRow>
        <SummaryRow label="Adres"><span className="font-mono">https://{domain}</span> <span className="text-meta">({state.slug})</span></SummaryRow>
        <SummaryRow label="Renk / logo"><span className="font-mono">{state.primaryColor}</span> · {state.logoName ?? "logo yok"}</SummaryRow>
        <SummaryRow label="Unvan / iletişim">{state.legalName || "—"} · {state.contactEmail || "—"}</SummaryRow>
      </SummarySection>
      <SummarySection title="Alanlar ve dersler" step="branches" onGoTo={onGoTo}>
        {state.customCategories.some((c) => state.branches.some((b) => b.category === c.key)) && (
          <SummaryRow label="Yeni alanlar">{state.customCategories.filter((c) => state.branches.some((b) => b.category === c.key)).map((c) => `${c.name} (${c.unitLabel})`).join(", ")}</SummaryRow>
        )}
        <SummaryRow label="Dersler">
          <span className="flex flex-wrap gap-1">{state.branches.map((b) => <Badge key={b.name} tone="brand">{b.name}</Badge>)}</span>
        </SummaryRow>
      </SummarySection>
      <SummarySection title="Sürüm ve eklentiler" step="modules" onGoTo={onGoTo}>
        <SummaryRow label="Sürüm"><span className="font-mono">v{state.version}</span>{state.isCanary && <Badge tone="warning" className="ml-2">Kanarya</Badge>}</SummaryRow>
        <SummaryRow label="Ücretli eklentiler">
          {optional.length === 0 ? "Yok (standart paket)" : (
            <span className="flex flex-wrap gap-1">{optional.map((key) => <Badge key={key} tone="brand"><span title={MODULE_DESCRIPTIONS[key]}>{name(key)}</span></Badge>)}</span>
          )}
        </SummaryRow>
      </SummarySection>
      <SummarySection title="Yönetici" step="admin" onGoTo={onGoTo}>
        <SummaryRow label="E-posta">{state.adminEmail}</SummaryRow>
        <SummaryRow label="Görünen ad">{state.adminDisplayName || "—"}</SummaryRow>
        <SummaryRow label="Parola">Platform üretir (geçici)</SummaryRow>
      </SummarySection>
      <SummarySection title="Okul kuralları" step="policy" onGoTo={onGoTo}>
        {groups && <SummaryRow label="Grup öğretmen payı">%{state.groupLessonSharePercent}</SummaryRow>}
        <SummaryRow label="Haftalık üst sınır">{state.maxWeeklyLessons} ders</SummaryRow>
        <SummaryRow label="Telafi">{state.makeupNoticeHours} sa önceden iptal · {state.makeupCreditValidDays} gün geçerli · mazeretli {state.excusedMakeupWindowDays} gün</SummaryRow>
        <SummaryRow label="Okul günü">{state.schoolDayStart} – {state.schoolDayEnd}</SummaryRow>
      </SummarySection>
      <SummarySection title="Aidat" step="billing" onGoTo={onGoTo}>
        <SummaryRow label="İndirimler">Çoklu kurs %{state.multiCourseDiscountPercent} · kardeş %{state.siblingDiscountPercent} · vade ayın {state.dueDayOfMonth}. günü</SummaryRow>
        <SummaryRow label="Birebir">{state.individual.lessonsPerMonth} ders/ay · {money(state.individual.monthlyAmount)} · {formatDate(state.individual.effectiveFrom)}</SummaryRow>
        {groups && <SummaryRow label="Grup">{state.group.lessonsPerMonth} ders/ay · {money(state.group.monthlyAmount)} · {formatDate(state.group.effectiveFrom)}</SummaryRow>}
      </SummarySection>
      <SummarySection title="Entegrasyonlar" step="integrations" onGoTo={onGoTo}>
        {Object.keys(integrations).length === 0 ? <SummaryRow label="Durum">Hiçbiri girilmedi (sağlayıcılar kapalı)</SummaryRow> : (
          Object.entries(INTEGRATION_TITLES).filter(([key]) => isModuleOn(key, state.modules, entries)).map(([key, title]) => {
            const filled = INTEGRATION_FIELDS[key].filter((field) => integrations[field.key]);
            return <SummaryRow key={key} label={title}>{filled.length > 0 ? `${filled.length}/${INTEGRATION_FIELDS[key].length} alan girildi` : "Girilmedi — kapalı kalır"}</SummaryRow>;
          })
        )}
        {integrations[OPS_ALERT_FIELD.key] && <SummaryRow label="Alarm alıcıları">{integrations[OPS_ALERT_FIELD.key]}</SummaryRow>}
        <SummaryRow label="Not">Değerler gösterilmez; {allFields.filter((field) => integrations[field.key]).length} değer şifreli saklanacak.</SummaryRow>
      </SummarySection>
      <p className="text-meta">“Okulu kur” kurulum işlemini sıraya alır; okul detayında adım adım canlı ilerlemeyi izleyebilirsin. Hata olursa oradan “Yeniden dene” ya da “İptal et ve temizle”.</p>
    </div>
  );
}
