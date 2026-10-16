"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Icon } from "@/components/icons";
import { IntegrationFields } from "@/components/integration-fields";
import { ModulePicker } from "@/components/module-picker";
import { FormMessage, LoadError, PageHeader, Panel, Skeleton } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { optionalOnly } from "@/lib/modules";
import { useReleaseModules, useReleases } from "@/lib/releases";
import { DUMP_NAME_PATTERN, checkSlug, useAdoptSchool } from "@/lib/schools";
import { slugFormatError, suggestSlug } from "../new/wizard-state";

const DOMAIN_PATTERN = /^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$/;
const INTEGRATION_OWNERS: Record<string, string> = { WhatsApp__: "whatsapp", Email__: "email", Ai__: "ai" };

function Field({ label, error, hint, children }: { label: ReactNode; error?: string; hint?: ReactNode; children: ReactNode }) {
  return (
    <label className="form-label">
      {label}
      {children}
      {error ? <span className="form-error">{error}</span> : hint ? <span className="form-hint">{hint}</span> : null}
    </label>
  );
}

// Var olan bir okulu (bugünkü canlı okul) platforma taşıma (Adopt). Sihirbazdan farkı: okulun
// yöneticisi, kuralları ve verisi zaten var - kurulum ucu çağrılmaz, eski okulun dökümü yeni
// sunucuda geri yüklenir. Bu yüzden yalnızca kimlik, sürüm, modüller, entegrasyonlar ve döküm adı sorulur.
export default function AdoptSchoolPage() {
  const router = useRouter();
  const adopt = useAdoptSchool();
  const releases = useReleases();
  const [displayName, setDisplayName] = useState("");
  const [shortName, setShortName] = useState("");
  const [slug, setSlug] = useState("");
  const [slugTouched, setSlugTouched] = useState(false);
  const [customDomain, setCustomDomain] = useState("");
  const [primaryColor, setPrimaryColor] = useState("#a84e1f");
  const [version, setVersion] = useState("");
  const [isCanary, setIsCanary] = useState(false);
  const [modules, setModules] = useState<Set<string>>(new Set());
  const [integrations, setIntegrations] = useState<Record<string, string>>({});
  const [dumpName, setDumpName] = useState("");
  const [showErrors, setShowErrors] = useState(false);

  const catalog = useReleaseModules(version || null);
  const available = (releases.data ?? []).filter((release) => !release.isWithdrawn);

  const [debouncedSlug, setDebouncedSlug] = useState("");
  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedSlug(slug), 400);
    return () => window.clearTimeout(timer);
  }, [slug]);
  const slugQuery = useQuery({
    queryKey: ["check-slug", debouncedSlug],
    queryFn: () => checkSlug(debouncedSlug),
    enabled: slugFormatError(debouncedSlug) === null,
    staleTime: 5_000,
  });
  const slugCheck = debouncedSlug === slug ? slugQuery.data : undefined;
  const slugChecking = debouncedSlug !== slug || slugQuery.isFetching;

  const errors: Record<string, string> = {};
  if (!displayName.trim() || displayName.trim().length > 120) errors.displayName = "Okul adı 1-120 karakter olmalı.";
  if (!shortName.trim() || shortName.trim().length > 40) errors.shortName = "Kısa ad 1-40 karakter olmalı.";
  const slugError = slugFormatError(slug);
  if (slugError) errors.slug = slugError;
  else if (!slugChecking && slugCheck && !slugCheck.available) errors.slug = slugCheck.reason ?? "Bu kısaltma kullanılamaz.";
  else if (slugChecking || !slugCheck) errors.slug = "Kısaltma denetleniyor…";
  if (customDomain.trim() && !DOMAIN_PATTERN.test(customDomain.trim().toLowerCase())) errors.customDomain = "Geçerli bir alan adı gir.";
  if (!/^#[0-9a-f]{6}$/i.test(primaryColor)) errors.primaryColor = "Renk #RRGGBB biçiminde olmalı.";
  if (!version) errors.version = "Bir sürüm seç.";
  if (!DUMP_NAME_PATTERN.test(dumpName.trim())) errors.dumpName = "Döküm adı “<ad>.sql.gz” ya da “<ad>.sql.gz.enc” olmalı.";
  const shown = showErrors ? errors : {};

  const domain = customDomain.trim().toLowerCase() || slugCheck?.domain || "";

  function submit(event: FormEvent) {
    event.preventDefault();
    if (Object.keys(errors).length > 0 || !catalog.data) {
      setShowErrors(true);
      return;
    }
    const filled: Record<string, string> = {};
    for (const [key, raw] of Object.entries(integrations)) {
      const value = raw.trim();
      const owner = Object.entries(INTEGRATION_OWNERS).find(([prefix]) => key.startsWith(prefix))?.[1];
      if (value && (!owner || modules.has(owner))) filled[key] = value;
    }
    adopt.mutate(
      {
        slug,
        displayName: displayName.trim(),
        shortName: shortName.trim(),
        primaryColor: primaryColor.toLowerCase(),
        customDomain: customDomain.trim().toLowerCase() || null,
        version,
        modules: optionalOnly(modules, catalog.data),
        dumpName: dumpName.trim(),
        integrations: Object.keys(filled).length > 0 ? filled : null,
        isCanary,
      },
      { onSuccess: (result) => router.push(`/schools/${result.id}`) },
    );
  }

  return (
    <div className="grid gap-5">
      <PageHeader
        title="Var olan okulu taşı"
        description="Çalışan bir okulu dökümüyle yeni sunucuya taşır; okulun yöneticisi ve verisi korunur."
        actions={<Link href="/" className="btn btn-quiet"><Icon name="arrow-left" className="h-4 w-4" />Okullar</Link>}
      />

      <FormMessage tone="info">
        Önce eski okulun veritabanı dökümünü al ve hedef sunucuda <span className="font-mono">/srv/abdera/{slug || "<kısaltma>"}/dumps/</span> altına ELLE koy.
        Döküm orada değilse işlem “Dökümü geri yükle” adımında hata verir; dosyayı koyduktan sonra okul detayından “Yeniden dene”.
        Sunucu, kurulumun “Sunucu seç” adımında belirlenir - dökümü koyacağın sunucuyu işlem başladıktan sonra detay sayfasındaki adım logundan gör.
      </FormMessage>

      <form onSubmit={submit} className="grid gap-5" noValidate>
        <Panel title="Kimlik">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Okul adı" error={shown.displayName}>
              <input className="field" value={displayName} maxLength={120} onChange={(event) => { setDisplayName(event.target.value); if (!slugTouched) setSlug(suggestSlug(event.target.value)); }} />
            </Field>
            <Field label="Kısa ad" error={shown.shortName}>
              <input className="field" value={shortName} maxLength={40} onChange={(event) => setShortName(event.target.value)} />
            </Field>
            <Field
              label="Kısaltma"
              error={shown.slug && shown.slug !== "Kısaltma denetleniyor…" ? shown.slug : undefined}
              hint={slugChecking ? "Denetleniyor…" : slugCheck ? (slugCheck.available ? <span className="font-semibold text-[var(--success-strong)]">Uygun · {slugCheck.domain}</span> : <span className="font-semibold text-[var(--danger-strong)]">{slugCheck.reason}</span>) : "Eski okulun kısaltmasıyla aynı olması önerilir."}
            >
              <input className="field font-mono" value={slug} maxLength={31} spellCheck={false} autoCapitalize="off" onChange={(event) => { setSlug(event.target.value.toLowerCase().replace(/[^a-z0-9-]/g, "")); setSlugTouched(true); }} />
            </Field>
            <Field label={<>Özel alan adı <span className="font-medium">(isteğe bağlı)</span></>} error={shown.customDomain} hint="Okul bugün kendi alan adında çalışıyorsa onu yaz; DNS kaydı yeni sunucuya yönlendirilir.">
              <input className="field font-mono" value={customDomain} spellCheck={false} autoCapitalize="off" onChange={(event) => setCustomDomain(event.target.value)} />
            </Field>
            <Field label="Ana renk" error={shown.primaryColor}>
              <span className="mt-[.3rem] flex items-center gap-2">
                <input type="color" aria-label="Renk seç" value={/^#[0-9a-f]{6}$/i.test(primaryColor) ? primaryColor : "#a84e1f"} onChange={(event) => setPrimaryColor(event.target.value)} className="h-10 w-12 shrink-0 cursor-pointer rounded-lg border border-[var(--line)] bg-transparent p-1" />
                <input className="field font-mono" value={primaryColor} maxLength={7} onChange={(event) => setPrimaryColor(event.target.value.trim())} />
              </span>
            </Field>
            {domain && <p className="self-end rounded-xl bg-[var(--surface-muted)] px-3 py-2 text-xs">Adres: <span className="font-mono font-bold">https://{domain}</span></p>}
          </div>
        </Panel>

        <Panel title="Sürüm">
          {releases.isLoading ? <Skeleton className="h-20" /> : releases.error ? <LoadError message={errorMessage(releases.error)} onRetry={() => releases.refetch()} /> : (
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Sürüm" error={shown.version} hint="Dökümün alındığı sürümle aynı ya da daha yeni olmalı; migration'lar açılışta uygulanır.">
                <select className="field font-mono" value={version} onChange={(event) => { setVersion(event.target.value); }}>
                  <option value="">Seç…</option>
                  {available.map((release) => <option key={release.version} value={release.version}>v{release.version}</option>)}
                </select>
              </Field>
              <label className="flex items-start gap-3 self-end rounded-2xl border border-[var(--line)] p-3">
                <input type="checkbox" className="mt-1 accent-[var(--brand-strong)]" checked={isCanary} onChange={(event) => setIsCanary(event.target.checked)} />
                <span><span className="block text-sm font-bold">Kanarya okul</span><span className="text-meta block">Filo güncellemelerinde ilk dalgada.</span></span>
              </label>
            </div>
          )}
        </Panel>

        <Panel title="Modüller" meta={version ? `v${version} kataloğu` : undefined}>
          {!version ? <p className="text-meta">Önce sürüm seç.</p>
            : catalog.isLoading ? <Skeleton className="h-40" />
            : catalog.error ? <LoadError message={errorMessage(catalog.error)} onRetry={() => catalog.refetch()} />
            : catalog.data && <ModulePicker catalog={catalog.data} selected={modules} onChange={setModules} />}
        </Panel>

        <Panel title="Entegrasyonlar" meta="Şifreli saklanır, boş = kapalı">
          <IntegrationFields modules={modules} values={integrations} onChange={(key, value) => setIntegrations((current) => ({ ...current, [key]: value }))} />
        </Panel>

        <Panel title="Döküm">
          <Field label="Döküm dosyasının adı" error={shown.dumpName} hint={<>Yalnızca dosya adı, yol değil (örn. <span className="font-mono">abdera-2026-10-15.sql.gz</span>). Şifreli döküm <span className="font-mono">.sql.gz.enc</span>.</>}>
            <input className="field font-mono" value={dumpName} spellCheck={false} autoCapitalize="off" onChange={(event) => setDumpName(event.target.value)} />
          </Field>
        </Panel>

        {showErrors && Object.keys(errors).length > 0 && <FormMessage tone="error">Gönderilmeden önce işaretli alanları düzelt.</FormMessage>}
        {adopt.error && <FormMessage tone="error">{errorMessage(adopt.error)}</FormMessage>}
        <div className="flex justify-end">
          <button type="submit" className="btn btn-primary" disabled={adopt.isPending}>
            <Icon name="rocket" className="h-4 w-4" />{adopt.isPending ? "Başlatılıyor…" : "Taşımayı başlat"}
          </button>
        </div>
      </form>
    </div>
  );
}
