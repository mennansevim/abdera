"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { CopyField, FormMessage } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { useMe, useTotpConfirm, useTotpSetup } from "@/lib/auth";

// İki adımlı doğrulama kurulumu. Platform QR kütüphanesi taşımaz: sır ve otpauth:// adresi
// kopyalanabilir metin olarak gösterilir; doğrulama uygulamasına elle eklenir.
export default function SetupTotpPage() {
  const router = useRouter();
  const { data: me, isLoading } = useMe();
  const setup = useTotpSetup();
  const confirm = useTotpConfirm();
  const [code, setCode] = useState("");

  useEffect(() => {
    if (isLoading) return;
    if (me === null) router.replace("/login");
    else if (me?.totpEnabled) router.replace(me.totpVerified ? "/" : "/login");
  }, [isLoading, me, router]);

  function submit(event: FormEvent) {
    event.preventDefault();
    confirm.mutate(code, { onSuccess: () => router.replace("/") });
  }

  if (isLoading || !me || me.totpEnabled) return null;

  return (
    <main className="grid min-h-dvh place-items-center bg-[var(--background)] px-4 py-8">
      <div className="app-card grid w-full max-w-lg gap-4 p-5 sm:p-6">
        <div>
          <h1 className="text-title">İki adımlı doğrulamayı kur</h1>
          <p className="text-meta mt-1">
            Okulları yönetmeden önce hesabın ({me.email}) bir doğrulama uygulamasına bağlanmalı. Bu adım atlanamaz.
          </p>
        </div>

        {!setup.data ? (
          <>
            <ol className="text-body grid list-decimal gap-1 pl-5 text-[var(--muted)]">
              <li>Telefonuna bir doğrulama uygulaması kur (Google Authenticator, 1Password, Aegis…).</li>
              <li>Aşağıdaki düğmeyle bu hesaba özel bir gizli anahtar üret.</li>
              <li>Anahtarı uygulamaya ekle ve uygulamanın gösterdiği 6 haneli kodu gir.</li>
            </ol>
            {setup.error && <FormMessage tone="error">{errorMessage(setup.error)}</FormMessage>}
            <button type="button" className="btn btn-primary" onClick={() => setup.mutate()} disabled={setup.isPending}>
              {setup.isPending ? "Üretiliyor…" : "Gizli anahtarı üret"}
            </button>
          </>
        ) : (
          <form onSubmit={submit} className="grid gap-4">
            <CopyField label="Gizli anahtar (uygulamaya “anahtar gir” ile ekle)" value={setup.data.secret} />
            <CopyField label="otpauth:// adresi (uygulaman bağlantıyla eklemeyi destekliyorsa)" value={setup.data.provisioningUri} />
            <FormMessage tone="info">
              Bu anahtarı kimseyle paylaşma ve bir yere kaydetme. Sayfayı yenilersen yeni bir anahtar üretmen gerekir; eskisi geçersiz olur.
            </FormMessage>
            <label className="form-label">
              Uygulamadaki 6 haneli kod
              <input
                className="field font-mono tracking-[.3em]"
                inputMode="numeric"
                autoComplete="one-time-code"
                pattern="[0-9]{6}"
                maxLength={6}
                required
                autoFocus
                value={code}
                onChange={(event) => setCode(event.target.value.replace(/\D/g, ""))}
              />
            </label>
            {confirm.error && <FormMessage tone="error">{errorMessage(confirm.error)}</FormMessage>}
            <button type="submit" className="btn btn-primary" disabled={confirm.isPending || code.length !== 6}>
              {confirm.isPending ? "Doğrulanıyor…" : "Doğrula ve etkinleştir"}
            </button>
          </form>
        )}
      </div>
    </main>
  );
}
