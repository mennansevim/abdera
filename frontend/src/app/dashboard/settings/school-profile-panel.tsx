"use client";

import { useRef, useState, type FormEvent } from "react";
import { FormMessage, SectionHeader } from "@/components/ui";
import { SchoolBrandMark } from "@/components/school-brand";
import { ApiError } from "@/lib/api";
import {
  SCHOOL_LOGO_MAX_BYTES,
  useInstanceConfig,
  useRemoveSchoolLogo,
  useUpdateSchoolProfile,
  useUploadSchoolLogo,
  type SchoolProfile,
} from "@/lib/instance";

// Okulun adı ve logosu (docs/multi-tenant/ARCHITECTURE.md §5.1). Yalnızca yönetici görür;
// değişiklik menüde, giriş ekranında ve veli portalında görünür, sunucu audit_log'a yazar.
export function SchoolProfilePanel() {
  const { data: config } = useInstanceConfig();

  return (
    <section className="app-card overflow-hidden">
      <div className="border-b border-[var(--line)] px-4 py-2.5">
        <SectionHeader title="Okul bilgileri" description="Okulun adı ve logosu menüde, giriş ekranında ve veli portalında görünür." />
      </div>
      <div className="space-y-5 p-4">
        <div className="rounded-xl bg-[linear-gradient(160deg,var(--sidebar-from)_0%,#c15a4a_45%,var(--sidebar-to)_100%)] px-4 py-3 text-white" aria-label="Menüde görünüm önizlemesi">
          <SchoolBrandMark />
        </div>
        {/* Form yapılandırma geldikten sonra kurulur; kayıttan sonra yeniden kurulmaz ki başarı mesajı kalsın. */}
        {config && <ProfileForm profile={config.profile} />}
        {config && <LogoForm hasLogo={Boolean(config.profile.logoUrl)} />}
      </div>
    </section>
  );
}

function ProfileForm({ profile }: { profile: SchoolProfile }) {
  const update = useUpdateSchoolProfile();
  const [name, setName] = useState(profile.name);
  const [shortName, setShortName] = useState(profile.shortName);
  const [legalName, setLegalName] = useState(profile.legalName ?? "");
  const [contactEmail, setContactEmail] = useState(profile.contactEmail ?? "");
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const changed = name.trim() !== profile.name || shortName.trim() !== profile.shortName
    || legalName.trim() !== (profile.legalName ?? "") || contactEmail.trim() !== (profile.contactEmail ?? "");

  async function submit(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    try {
      // Boş metin sunucuda alanı temizler (unvan/e-posta isteğe bağlı).
      await update.mutateAsync({ name, shortName, primaryColor: profile.primaryColor, legalName, contactEmail });
      setMessage({ tone: "success", text: "Okul bilgileri kaydedildi." });
    } catch (err) {
      setMessage({ tone: "error", text: describe(err, "Okul bilgileri kaydedilemedi.") });
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Okulun tam adı
          <input value={name} onChange={(event) => setName(event.target.value)} required maxLength={120} className="field text-sm" placeholder="Örn. Deniz Müzik ve Dans Akademisi" />
        </label>
        <label className="form-label">Kısa ad
          <input value={shortName} onChange={(event) => setShortName(event.target.value)} required maxLength={40} className="field text-sm" placeholder="Örn. Deniz" />
          <span className="text-meta mt-1 block font-normal">Menüde büyük yazılır. Tam ad kısa adla başlıyorsa kalanı alt satırda görünür.</span>
        </label>
        <label className="form-label">Resmi unvan (isteğe bağlı)
          <input value={legalName} onChange={(event) => setLegalName(event.target.value)} maxLength={200} className="field text-sm" placeholder="Örn. Deniz Müzik Eğitim Ltd. Şti." />
          <span className="text-meta mt-1 block font-normal">Gizlilik metninde veri sorumlusu olarak yazılır; boşsa okulun tam adı kullanılır.</span>
        </label>
        <label className="form-label">KVKK iletişim e-postası (isteğe bağlı)
          <input type="email" value={contactEmail} onChange={(event) => setContactEmail(event.target.value)} maxLength={254} className="field text-sm" placeholder="kvkk@okulunuz.com" />
        </label>
      </div>
      {message && <FormMessage tone={message.tone}>{message.text}</FormMessage>}
      <button type="submit" className="btn btn-primary" disabled={!changed || update.isPending}>
        {update.isPending ? "Kaydediliyor…" : "Bilgileri kaydet"}
      </button>
    </form>
  );
}

function LogoForm({ hasLogo }: { hasLogo: boolean }) {
  const upload = useUploadSchoolLogo();
  const remove = useRemoveSchoolLogo();
  const input = useRef<HTMLInputElement>(null);
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);

  async function onFile(file: File | undefined) {
    if (!file) return;
    setMessage(null);
    if (file.size > SCHOOL_LOGO_MAX_BYTES) {
      setMessage({ tone: "error", text: "Logo en fazla 512 KB olabilir." });
      return;
    }
    try {
      await upload.mutateAsync(file);
      setMessage({ tone: "success", text: "Logo güncellendi." });
    } catch (err) {
      setMessage({ tone: "error", text: describe(err, "Logo yüklenemedi.") });
    } finally {
      if (input.current) input.current.value = "";
    }
  }

  async function onRemove() {
    setMessage(null);
    try {
      await remove.mutateAsync();
      setMessage({ tone: "success", text: "Logo kaldırıldı." });
    } catch (err) {
      setMessage({ tone: "error", text: describe(err, "Logo kaldırılamadı.") });
    }
  }

  return (
    <div className="space-y-2 border-t border-[var(--line)] pt-4">
      <p className="text-sm font-bold">Logo</p>
      <p className="text-meta">PNG, JPEG veya WebP; kare ve en fazla 512 KB. Yuvarlak çerçevede gösterilir.</p>
      <div className="flex flex-wrap gap-2">
        <label className={`btn btn-quiet cursor-pointer ${upload.isPending ? "pointer-events-none opacity-60" : ""}`}>
          {upload.isPending ? "Yükleniyor…" : hasLogo ? "Logoyu değiştir" : "Logo yükle"}
          <input ref={input} type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={(event) => void onFile(event.target.files?.[0])} />
        </label>
        {hasLogo && <button type="button" onClick={() => void onRemove()} disabled={remove.isPending} className="btn btn-quiet">Logoyu kaldır</button>}
      </div>
      {message && <FormMessage tone={message.tone}>{message.text}</FormMessage>}
    </div>
  );
}

function describe(error: unknown, fallback: string) {
  if (!(error instanceof ApiError)) return fallback;
  const fieldError = error.errors ? Object.values(error.errors)[0]?.[0] : undefined;
  return fieldError ?? error.detail ?? error.title ?? fallback;
}
