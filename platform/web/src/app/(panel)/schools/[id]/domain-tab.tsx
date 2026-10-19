"use client";

import { useState, type FormEvent } from "react";
import { Badge, CopyField, FormMessage, Panel } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { formatDateTime } from "@/lib/format";
import { useCheckSchoolDomain, useRemoveSchoolDomain, useSetSchoolDomain, type SchoolDetail } from "@/lib/schools";

// Okulun kendi alan adı (ör. panel.okulum.com). Okulun web sitesi kendi yerinde kalır; panel okulun
// alan adında açılır. Akış: alan adını kaydet → müşteriye DNS talimatını ver → "DNS'i kontrol et" →
// platform bağlar (Caddy, otomatik sertifika). İstenirse platform alt alanı yeni adrese yönlenir.
export function DomainTab({ detail, onQueued }: { detail: SchoolDetail; onQueued: (message: string) => void }) {
  const domain = detail.domain;
  const id = detail.school.id;
  const set = useSetSchoolDomain(id);
  const check = useCheckSchoolDomain(id);
  const remove = useRemoveSchoolDomain(id);
  const [value, setValue] = useState(domain?.customDomain ?? "");
  const [redirect, setRedirect] = useState(domain?.customDomain ? domain.redirect : true);
  const [checkMessage, setCheckMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const editable = detail.school.status === "Active";

  if (!domain) return <FormMessage tone="error">Alan adı bilgisi okunamadı.</FormMessage>;
  const custom = domain.customDomain;
  // Kök alan adı (okulum.com) CNAME alamaz; A kaydı gerekir. Alt alan (panel.okulum.com) CNAME alır.
  const twoLevelSuffix = custom ? /\.(com|net|org|gen|edu|k12|web|biz|info)\.tr$/.test(custom) : false;
  const isApex = custom ? custom.split(".").length <= (twoLevelSuffix ? 3 : 2) : false;
  const recordName = custom ? (isApex ? "@" : custom.split(".")[0]) : "";

  async function save(event: FormEvent) {
    event.preventDefault();
    setCheckMessage(null);
    try {
      await set.mutateAsync({ domain: value, redirect });
      if (domain?.active && custom === value.trim().toLowerCase()) onQueued("Yönlendirme tercihi uygulanıyor.");
    } catch {
      // Hata aşağıda gösterilir.
    }
  }

  async function runCheck() {
    setCheckMessage(null);
    try {
      const result = await check.mutateAsync(undefined);
      setCheckMessage({ tone: result.ready ? "success" : "error", text: result.message });
      if (result.ready) onQueued("Alan adı bağlanıyor; sertifika birkaç saniye içinde alınır.");
    } catch (error) {
      setCheckMessage({ tone: "error", text: errorMessage(error) });
    }
  }

  async function removeDomain() {
    try {
      await remove.mutateAsync(undefined);
      setValue("");
      if (domain?.active) onQueued("Alan adı kaldırılıyor.");
    } catch {
      // Hata aşağıda gösterilir.
    }
  }

  return (
    <div className="grid gap-4">
      <Panel title="Okulun alan adı" meta={custom ? (domain.active ? "Bağlı" : "DNS bekleniyor") : "Yok"}>
        <div className="grid gap-4 p-4">
          <p className="text-meta">
            Okulun paneli şu an <span className="font-mono">https://{domain.target}</span> adresinde. Okul kendi alan adını
            istiyorsa (ör. <span className="font-mono">panel.okulum.com</span>) buraya yaz; okulun web sitesi kendi yerinde kalır,
            sitesine “Giriş” bağlantısı olarak bu adresi koyar.
          </p>

          {custom && domain.active && (
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone="success">Bağlı</Badge>
              <a href={`https://${custom}`} target="_blank" rel="noreferrer" className="font-mono text-sm font-bold text-[var(--brand-strong)] underline">https://{custom}</a>
              {domain.redirect && <span className="text-meta">· eski adres buraya yönleniyor</span>}
            </div>
          )}

          <form onSubmit={save} className="grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
            <label className="form-label">Alan adı
              <input className="field font-mono" value={value} onChange={(event) => setValue(event.target.value)} placeholder="panel.okulum.com"
                disabled={!editable} maxLength={253} autoComplete="off" spellCheck={false} />
            </label>
            <button type="submit" className="btn btn-primary" disabled={!editable || set.isPending || !value.trim()}>Kaydet</button>
            <label className="flex items-center gap-2 text-sm font-semibold sm:col-span-2">
              <input type="checkbox" checked={redirect} onChange={(event) => setRedirect(event.target.checked)} disabled={!editable} />
              Eski adresi (<span className="font-mono">{domain.target}</span>) bu adrese yönlendir
            </label>
          </form>
          {!editable && <FormMessage tone="info">Alan adı yalnızca etkin okulda bağlanabilir.</FormMessage>}
          {set.error && <FormMessage tone="error">{errorMessage(set.error)}</FormMessage>}
        </div>
      </Panel>

      {custom && !domain.active && (
        <Panel title="Müşteriye verilecek DNS talimatı" meta="Okulun alan adını yönettiği yerde (Cloudflare, GoDaddy, Natro…)">
          <div className="grid gap-3 p-4">
            {isApex ? (
              <>
                <p className="text-sm">Kök alan adı CNAME alamaz; şu <b>A kaydını</b> ekleyin:</p>
                <div className="grid gap-2 sm:grid-cols-3">
                  <CopyField label="Tür" value="A" />
                  <CopyField label="Ad" value="@" />
                  <CopyField label="Değer" value={domain.hostIp ?? "—"} />
                </div>
              </>
            ) : (
              <>
                <p className="text-sm">Şu <b>CNAME kaydını</b> ekleyin:</p>
                <div className="grid gap-2 sm:grid-cols-3">
                  <CopyField label="Tür" value="CNAME" />
                  <CopyField label="Ad" value={recordName} />
                  <CopyField label="Hedef" value={domain.target} />
                </div>
              </>
            )}
            <p className="text-meta">Kayıt eklendikten sonra yayılması birkaç dakika sürebilir. Cloudflare kullanıyorsa kaydın “Proxy” (turuncu bulut) kapalı olmalı.</p>
            <div className="flex flex-wrap items-center gap-2">
              <button type="button" className="btn btn-primary" onClick={runCheck} disabled={check.isPending}>{check.isPending ? "Kontrol ediliyor…" : "DNS'i kontrol et"}</button>
              {domain.lastCheck && !checkMessage && <span className="text-meta">Son kontrol {domain.checkedAt ? formatDateTime(domain.checkedAt) : ""}: {domain.lastCheck}</span>}
            </div>
            {checkMessage && <FormMessage tone={checkMessage.tone}>{checkMessage.text}</FormMessage>}
          </div>
        </Panel>
      )}

      {custom && (
        <div className="flex flex-wrap items-center justify-end gap-2">
          <span className="text-meta">{domain.active ? "Kaldırınca okul yalnızca platform adresinde açılır." : "Bekleyen alan adı silinir."}</span>
          <button type="button" className="btn btn-quiet text-[var(--danger-strong)]" onClick={removeDomain} disabled={remove.isPending}>Alan adını kaldır</button>
        </div>
      )}
      {remove.error && <FormMessage tone="error">{errorMessage(remove.error)}</FormMessage>}
    </div>
  );
}
