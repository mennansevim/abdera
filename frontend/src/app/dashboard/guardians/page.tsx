"use client";

import { useMemo, useState } from "react";
import { Icon } from "@/components/icons";
import { AdminGate, Badge, EmptyState, Modal, PageHeader, SearchInput, Segmented, type Tone } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  GUARDIAN_PASSWORD_STATE_LABEL,
  guardianCredentialMessage,
  localPhone,
  useGuardianLogins,
  useResetGuardianPassword,
  useRevealGuardianPassword,
  whatsAppChatUrl,
  type GuardianLogin,
  type GuardianPasswordState,
} from "@/lib/people";
import { useSessionState } from "@/lib/use-session-state";
import { useSchoolProfile } from "@/lib/instance";

// docs/10-decisions.md AA: veli giriş bilgilerini okul yönetir. Kullanıcı adı velinin telefonu,
// şifreyi okul verir (rastgele 6 hane) ve gerektiğinde tekrar görür. İşletme WhatsApp hesabı
// şifre içeren mesaj gönderemediği için (R2) hazır metni yönetici kopyalayıp kendisi iletir.
export default function GuardianLoginsPage() {
  return <AdminGate><GuardianLoginsContent /></AdminGate>;
}

const STATE_TONE: Record<GuardianPasswordState, Tone> = {
  Default: "warning",
  Issued: "success",
  GuardianSet: "brand",
  Legacy: "muted",
};

type Filter = "all" | "school" | "guardian";

type Credential = { guardianId: string; password: string | null; note?: string };

function GuardianLoginsContent() {
  const { data, isLoading, isError, isFetching, refetch } = useGuardianLogins();
  const reveal = useRevealGuardianPassword();
  const reset = useResetGuardianPassword();
  const [search, setSearch] = useSessionState("abdera:guardian-logins:search", "");
  const [filter, setFilter] = useSessionState<Filter>("abdera:guardian-logins:filter", "all");
  const [credential, setCredential] = useState<Credential | null>(null);
  const [confirmReset, setConfirmReset] = useState<GuardianLogin | null>(null);
  const [error, setError] = useState<string | null>(null);

  const rows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase("tr-TR");
    const digits = query.replace(/\D/g, "");
    return (data ?? []).filter((row) => {
      if (filter === "guardian" && row.passwordState !== "GuardianSet") return false;
      if (filter === "school" && row.passwordState === "GuardianSet") return false;
      if (!query) return true;
      const haystack = [`${row.firstName} ${row.lastName}`, ...row.studentNames].join(" ").toLocaleLowerCase("tr-TR");
      return haystack.includes(query) || (digits.length >= 3 && localPhone(row.phoneNumber).includes(digits));
    });
  }, [data, filter, search]);

  async function show(row: GuardianLogin) {
    setError(null);
    try {
      const result = await reveal.mutateAsync(row.id);
      setCredential({
        guardianId: row.id,
        password: result.password,
        note: result.password ? undefined : result.passwordState === "GuardianSet"
          ? "Veli kendi şifresini belirlemiş, okul bu şifreyi göremez. Unuttuysa yeni şifre ver."
          : "Bu şifre görüntülenemiyor. Veliye yeni şifre ver.",
      });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Şifre gösterilemedi.");
    }
  }

  async function issue(row: GuardianLogin) {
    setError(null);
    try {
      const result = await reset.mutateAsync(row.id);
      setCredential({ guardianId: row.id, password: result.password, note: "Yeni şifre verildi. Eski şifre artık geçmez." });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Yeni şifre verilemedi.");
    } finally {
      setConfirmReset(null);
    }
  }

  const total = data?.length ?? 0;

  return (
    <div className="space-y-3">
      <PageHeader
        title="Veli girişleri"
        description="Kullanıcı adı velinin telefon numarası, şifreyi okul verir. Şifreyi göster, kopyala ya da yenisini ver."
        actions={<SearchInput value={search} onChange={setSearch} label="Veli ara" placeholder="Veli, öğrenci ya da telefon…" />}
      />

      {error && <p role="alert" className="rounded-xl border border-[color:var(--danger)]/30 bg-[var(--danger-soft)] px-3 py-2 text-sm font-semibold text-[var(--danger-strong)]">{error}</p>}

      <div className="app-card relative overflow-hidden">
        <div className="flex flex-wrap items-center gap-2 border-b border-[var(--line)] px-3 py-2">
          <p className="text-meta mr-auto"><strong className="text-[var(--foreground)]">{rows.length}</strong> / {total} veli</p>
          <Segmented
            label="Şifre durumuna göre filtrele"
            options={[{ value: "all", label: "Tümü" }, { value: "school", label: "Okulun şifresi" }, { value: "guardian", label: "Veli değiştirdi" }]}
            value={filter}
            onChange={(value) => setFilter(value as Filter)}
          />
        </div>

        {isLoading && <div className="space-y-2 p-3">{Array.from({ length: 6 }, (_, index) => <div key={index} className="skeleton h-12 rounded-lg" />)}</div>}
        {!isLoading && isError && (
          <div className="grid min-h-48 place-items-center p-6 text-center"><div>
            <p className="text-sm font-bold">Veliler yüklenemedi</p>
            <button type="button" onClick={() => void refetch()} disabled={isFetching} className="btn btn-quiet mt-3 disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button>
          </div></div>
        )}
        {!isLoading && !isError && rows.length === 0 && <EmptyState icon="students" title={total ? "Aramayla eşleşen veli yok." : "Henüz veli yok."} />}

        {!isLoading && !isError && rows.length > 0 && (
          <ul className="divide-y divide-[var(--line)]">
            {rows.map((row) => (
              <li key={row.id} className="px-3 py-2.5">
                <div className="flex flex-wrap items-center gap-x-3 gap-y-2 md:grid md:grid-cols-[minmax(0,1fr)_7.5rem_10rem_16.5rem]">
                  <div className="min-w-0 flex-1 basis-56">
                    <p className="truncate text-sm font-bold">{row.firstName} {row.lastName}</p>
                    <p className="text-meta truncate">{row.studentNames.length ? row.studentNames.join(", ") : "Bağlı öğrenci yok"}</p>
                  </div>
                  <span className="font-mono text-sm tabular-nums">{localPhone(row.phoneNumber)}</span>
                  <Badge tone={STATE_TONE[row.passwordState]} className="md:justify-self-start">{GUARDIAN_PASSWORD_STATE_LABEL[row.passwordState]}</Badge>
                  <div className="flex gap-1.5 md:justify-end">
                    {row.passwordState !== "GuardianSet" && (
                      <button type="button" onClick={() => void show(row)} disabled={reveal.isPending} className="btn btn-quiet text-xs disabled:opacity-50">
                        <Icon name="search" className="h-3.5 w-3.5" /> Şifreyi göster
                      </button>
                    )}
                    <button type="button" onClick={() => setConfirmReset(row)} disabled={reset.isPending} className="btn btn-quiet text-xs disabled:opacity-50">
                      <Icon name="shield" className="h-3.5 w-3.5" /> Yeni şifre ver
                    </button>
                  </div>
                </div>
                {credential?.guardianId === row.id && (
                  <CredentialBox row={row} credential={credential} onClose={() => setCredential(null)} onIssue={() => setConfirmReset(row)} />
                )}
              </li>
            ))}
          </ul>
        )}
      </div>

      <Modal
        open={confirmReset !== null}
        title="Yeni şifre ver"
        description={confirmReset ? `${confirmReset.firstName} ${confirmReset.lastName} için 6 haneli yeni bir şifre üretilecek. Mevcut şifre ve açık oturumlar geçersiz olur.` : undefined}
        onClose={() => setConfirmReset(null)}
        size="sm"
      >
        <div className="flex justify-end gap-2">
          <button type="button" onClick={() => setConfirmReset(null)} className="btn btn-quiet">Vazgeç</button>
          <button type="button" onClick={() => confirmReset && void issue(confirmReset)} disabled={reset.isPending} className="btn btn-primary disabled:opacity-50">
            {reset.isPending ? "Üretiliyor…" : "Yeni şifre ver"}
          </button>
        </div>
      </Modal>
    </div>
  );
}

function CredentialBox({ row, credential, onClose, onIssue }: {
  row: GuardianLogin;
  credential: Credential;
  onClose: () => void;
  onIssue: () => void;
}) {
  const [copied, setCopied] = useState<string | null>(null);
  const schoolName = useSchoolProfile().shortName;
  const message = credential.password ? guardianCredentialMessage(row.phoneNumber, credential.password, window.location.origin, schoolName) : null;

  async function copy(text: string, label: string) {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(label);
    } catch {
      setCopied("Kopyalanamadı, metni elle seç.");
    }
  }

  return (
    <div className="mt-2 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/60 p-3">
      {credential.password ? (
        <>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
            <p className="text-meta">Kullanıcı adı <span className="font-mono text-sm font-bold text-[var(--foreground)]">{localPhone(row.phoneNumber)}</span></p>
            <p className="text-meta">Şifre <span className="font-mono text-lg font-bold tracking-wider text-[var(--foreground)]">{credential.password}</span></p>
          </div>
          {credential.note && <p className="text-meta mt-1">{credential.note}</p>}
          <div className="mt-2 flex flex-wrap gap-1.5">
            <button type="button" onClick={() => void copy(message!, "Giriş bilgileri kopyalandı.")} className="btn btn-primary text-xs">Giriş bilgilerini kopyala</button>
            <button type="button" onClick={() => void copy(credential.password!, "Şifre kopyalandı.")} className="btn btn-quiet text-xs">Yalnızca şifre</button>
            <a href={`${whatsAppChatUrl(row.phoneNumber)}?text=${encodeURIComponent(message!)}`} target="_blank" rel="noreferrer" className="btn btn-quiet text-xs">
              <Icon name="whatsapp" className="h-3.5 w-3.5" /> Kendi WhatsApp&apos;ımdan gönder
            </a>
            <button type="button" onClick={onClose} className="btn btn-quiet ml-auto text-xs">Gizle</button>
          </div>
          {copied && <p role="status" className="text-meta mt-1.5 font-semibold text-[var(--success-strong)]">{copied}</p>}
        </>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          <p className="text-meta mr-auto">{credential.note}</p>
          <button type="button" onClick={onIssue} className="btn btn-primary text-xs">Yeni şifre ver</button>
          <button type="button" onClick={onClose} className="btn btn-quiet text-xs">Kapat</button>
        </div>
      )}
    </div>
  );
}
