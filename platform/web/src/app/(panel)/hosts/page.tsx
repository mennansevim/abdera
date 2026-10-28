"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { Badge, EmptyState, FormMessage, LoadError, Modal, Notice, PageHeader, Panel, ProgressBar, Skeleton } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { formatDateTime } from "@/lib/format";
import { HOST_STATUS_LABEL, useCheckHost, useCreateHost, useHosts, useSetHostStatus, type HostStatus, type ServerHost } from "@/lib/hosts";

const STATUS_TONE: Record<HostStatus, "success" | "warning" | "muted"> = { Active: "success", Draining: "warning", Offline: "muted" };

// Okul sunucuları (§4.4): kapasite, ajan yoklaması, durum değişikliği ve yeni sunucu.
export default function HostsPage() {
  const hosts = useHosts();
  const check = useCheckHost();
  const setStatus = useSetHostStatus();
  const [adding, setAdding] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  function runCheck(host: ServerHost) {
    setNotice(null);
    check.mutate(host.id, {
      onSuccess: (info) => {
        const agent = typeof info?.agent === "string" ? info.agent : "?";
        setNotice(`${host.name} yanıt verdi · ajan ${agent}`);
      },
    });
  }

  return (
    <div className="grid gap-5">
      <PageHeader
        title="Sunucular"
        description="Okulların çalıştığı sunucular ve ayrılmış kapasite (toplam belleğin %80'i kullanılabilir)."
        actions={<button type="button" className="btn btn-primary" onClick={() => setAdding(true)}><Icon name="plus" className="h-4 w-4" />Sunucu ekle</button>}
      />
      {notice && <Notice onDismiss={() => setNotice(null)}>{notice}</Notice>}
      {check.error && <FormMessage tone="error">Yoklama başarısız: {errorMessage(check.error)}</FormMessage>}
      {setStatus.error && <FormMessage tone="error">{errorMessage(setStatus.error)}</FormMessage>}

      {hosts.isLoading ? <Skeleton className="h-48" /> : hosts.error ? <LoadError message={errorMessage(hosts.error)} onRetry={() => hosts.refetch()} /> : (hosts.data ?? []).length === 0 ? (
        <Panel><EmptyState icon="server" title="Sunucu yok" description="Okul kurmak için en az bir sunucu ekle." /></Panel>
      ) : (
        <div className="grid gap-3 md:grid-cols-2">
          {(hosts.data ?? []).map((host) => {
            const ratio = host.capacityMb > 0 ? host.reservedMemoryMb / host.capacityMb : 1;
            return (
              <section key={host.id} className="app-card grid gap-3 p-4">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <Link href={`/hosts/${host.id}`} className="flex items-center gap-2 text-sm font-bold hover:underline"><Icon name="server" className="h-4 w-4" />{host.name}<Icon name="arrow-right" className="h-3.5 w-3.5" /></Link>
                    <p className="text-meta break-all font-mono">{host.sshUser}@{host.address}:{host.sshPort} · {host.publicIp}</p>
                  </div>
                  <Badge tone={STATUS_TONE[host.status]}>{HOST_STATUS_LABEL[host.status]}</Badge>
                </div>
                <div>
                  <ProgressBar value={host.reservedMemoryMb} max={host.capacityMb} tone={ratio > 0.9 ? "danger" : ratio > 0.75 ? "warning" : "brand"} label={`${host.name} kapasite`} />
                  <p className="text-meta mt-1">{host.reservedMemoryMb} / {host.capacityMb} MB ayrılmış · toplam {host.totalMemoryMb} MB · {host.schoolCount} okul</p>
                </div>
                <p className="text-[.72rem] text-[var(--muted)] break-all">
                  Parmak izi <span className="font-mono">{host.hostKeyFingerprint}</span><br />
                  Ajan {host.agentVersion ?? "hiç yoklanmadı"}{host.lastCheckedAt && ` · ${formatDateTime(host.lastCheckedAt)}`}
                </p>
                <div className="flex flex-wrap items-center gap-2 border-t border-[var(--line)] pt-3">
                  <button type="button" className="btn btn-quiet" disabled={check.isPending && check.variables === host.id} onClick={() => runCheck(host)}>
                    <Icon name="refresh" className="h-4 w-4" />{check.isPending && check.variables === host.id ? "Yoklanıyor…" : "Yokla"}
                  </button>
                  <label className="ml-auto flex items-center gap-2 text-xs font-bold text-[var(--muted)]">
                    Durum
                    <select
                      className="field w-auto min-w-36"
                      value={host.status}
                      disabled={setStatus.isPending}
                      onChange={(event) => setStatus.mutate({ id: host.id, status: event.target.value as HostStatus })}
                    >
                      {(Object.keys(HOST_STATUS_LABEL) as HostStatus[]).map((status) => <option key={status} value={status}>{HOST_STATUS_LABEL[status]}</option>)}
                    </select>
                  </label>
                </div>
              </section>
            );
          })}
        </div>
      )}
      <p className="text-meta">“Boşaltılıyor” ve “Kapalı” sunuculara yeni okul yerleşmez; üzerindeki okullar çalışmaya devam eder. Sunucu çökerse platform otomatik taşıma yapmaz (§16.6).</p>

      <AddHostDialog open={adding} onClose={() => setAdding(false)} onCreated={(name) => { setAdding(false); setNotice(`${name} eklendi. Ajanı doğrulamak için “Yokla”.`); }} />
    </div>
  );
}

function AddHostDialog({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (name: string) => void }) {
  const create = useCreateHost();
  const [form, setForm] = useState({ name: "", address: "", sshPort: "22", sshUser: "abdera", hostKeyFingerprint: "", publicIp: "", totalMemoryMb: "" });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const set = (key: keyof typeof form) => (value: string) => setForm((current) => ({ ...current, [key]: value }));

  function submit(event: FormEvent) {
    event.preventDefault();
    const next: Record<string, string> = {};
    if (!form.name.trim() || form.name.trim().length > 60) next.name = "Ad 1-60 karakter.";
    if (!form.address.trim()) next.address = "Adres gerekli.";
    const port = Number(form.sshPort);
    if (!Number.isInteger(port) || port < 1 || port > 65535) next.sshPort = "1-65535.";
    if (!form.sshUser.trim()) next.sshUser = "Kullanıcı gerekli.";
    if (!/^SHA256:[A-Za-z0-9+/=]+$/.test(form.hostKeyFingerprint.trim())) next.hostKeyFingerprint = "“SHA256:…” biçiminde olmalı.";
    if (!/^(\d{1,3}\.){3}\d{1,3}$|^[0-9a-fA-F:]+$/.test(form.publicIp.trim())) next.publicIp = "Geçerli bir IP.";
    const memory = Number(form.totalMemoryMb);
    if (!Number.isInteger(memory) || memory < 1024) next.totalMemoryMb = "En az 1024 MB.";
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    create.mutate(
      { name: form.name.trim(), address: form.address.trim(), sshPort: port, sshUser: form.sshUser.trim(), hostKeyFingerprint: form.hostKeyFingerprint.trim(), publicIp: form.publicIp.trim(), totalMemoryMb: memory },
      { onSuccess: (host) => { setForm({ name: "", address: "", sshPort: "22", sshUser: "abdera", hostKeyFingerprint: "", publicIp: "", totalMemoryMb: "" }); onCreated(host.name); } },
    );
  }

  const field = (key: keyof typeof form, label: string, props: { hint?: string; mono?: boolean; inputMode?: "numeric" } = {}) => (
    <label className="form-label">
      {label}
      <input className={`field ${props.mono ? "font-mono" : ""}`} value={form[key]} inputMode={props.inputMode} spellCheck={false} autoCapitalize="off" onChange={(event) => set(key)(event.target.value)} aria-invalid={Boolean(errors[key])} />
      {errors[key] ? <span className="form-error">{errors[key]}</span> : props.hint ? <span className="form-hint">{props.hint}</span> : null}
    </label>
  );

  return (
    <Modal open={open} title="Sunucu ekle" description="Sunucuyu önce elle aç ve abdera-host ajanını kur; burada yalnızca bağlantı bilgisi girilir." onClose={onClose}>
      <form onSubmit={submit} className="grid gap-3 sm:grid-cols-2" noValidate>
        {field("name", "Ad", { hint: "Örn. hetzner-1" })}
        {field("address", "SSH adresi", { hint: "Alan adı ya da IP", mono: true })}
        {field("sshUser", "SSH kullanıcısı", { mono: true })}
        {field("sshPort", "SSH portu", { inputMode: "numeric" })}
        <div className="sm:col-span-2">{field("hostKeyFingerprint", "Sunucu anahtarı parmak izi (SHA256)", { hint: "Sunucuda: ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub → “SHA256:…” kısmı. Platform yalnızca bu anahtarı kabul eder.", mono: true })}</div>
        {field("publicIp", "Genel IP", { hint: "DNS kayıtları bu adrese yönlenir.", mono: true })}
        {field("totalMemoryMb", "Toplam bellek (MB)", { inputMode: "numeric", hint: "Kapasite bunun %80'i sayılır." })}
        {create.error && <div className="sm:col-span-2"><FormMessage tone="error">{errorMessage(create.error)}</FormMessage></div>}
        <div className="flex justify-end gap-2 sm:col-span-2">
          <button type="button" className="btn btn-quiet" onClick={onClose}>Vazgeç</button>
          <button type="submit" className="btn btn-primary" disabled={create.isPending}>{create.isPending ? "Ekleniyor…" : "Ekle"}</button>
        </div>
      </form>
    </Modal>
  );
}
