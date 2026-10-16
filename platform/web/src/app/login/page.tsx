"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { FormMessage } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { useLogin, useMe } from "@/lib/auth";

// Operatör girişi: e-posta + parola + 6 haneli kod. Kod, iki adımlı doğrulama kurulana kadar
// boş bırakılabilir; kurulduktan sonra zorunludur (backend aynı hatayla reddeder - hangi
// alanın yanlış olduğu bilinçli olarak söylenmez).
export default function LoginPage() {
  const router = useRouter();
  const { data: me } = useMe();
  const login = useLogin();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");

  // Oturum zaten tam doğrulanmışsa giriş ekranı atlanır.
  useEffect(() => {
    if (me?.totpEnabled && me.totpVerified) router.replace("/");
  }, [me, router]);

  // Oturum açık ama bu oturumda kod girilmemiş: e-posta bilinir, kod zorunlu.
  const needsCode = Boolean(me?.totpEnabled && !me.totpVerified);
  const emailValue = email || (needsCode ? me?.email ?? "" : "");

  function submit(event: FormEvent) {
    event.preventDefault();
    login.mutate(
      { email: emailValue, password, totpCode: code },
      {
        onSuccess: (result) => {
          if (!result.totpEnabled) router.replace("/setup-totp");
          else if (result.totpVerified) router.replace("/");
        },
      },
    );
  }

  return (
    <main className="grid min-h-dvh place-items-center bg-[var(--background)] px-4 py-8">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex items-center justify-center gap-2.5 text-[var(--brand-strong)]">
          <span className="brand-mark font-serif text-sm font-bold italic" aria-hidden="true">M</span>
          <span className="leading-none">
            <span className="block font-serif text-[1.35rem] font-bold italic tracking-[-0.03em]">Miralay</span>
            <span className="mt-1 block text-[.72rem] font-semibold tracking-[0.12em] text-[var(--muted)]">PLATFORM</span>
          </span>
        </div>
        <form onSubmit={submit} className="app-card grid gap-4 p-5 sm:p-6">
          <div>
            <h1 className="text-title">Operatör girişi</h1>
            <p className="text-meta mt-1">
              {needsCode ? "Bu oturum için doğrulama uygulamandaki kodu gir." : "Okul kurulumlarını yönetmek için giriş yap."}
            </p>
          </div>
          <label className="form-label">
            E-posta
            <input className="field" type="email" autoComplete="username" required value={emailValue} onChange={(event) => setEmail(event.target.value)} />
          </label>
          <label className="form-label">
            Parola
            <input className="field" type="password" autoComplete="current-password" required value={password} onChange={(event) => setPassword(event.target.value)} />
          </label>
          <label className="form-label">
            Doğrulama kodu {!needsCode && <span className="font-medium">(iki adımlı doğrulama açıksa)</span>}
            <input
              className="field font-mono tracking-[.3em]"
              inputMode="numeric"
              autoComplete="one-time-code"
              pattern="[0-9]{6}"
              maxLength={6}
              placeholder="••••••"
              required={needsCode}
              value={code}
              onChange={(event) => setCode(event.target.value.replace(/\D/g, ""))}
            />
            <span className="form-hint">Kimlik doğrulama uygulamasındaki 6 haneli kod.</span>
          </label>
          {login.error && <FormMessage tone="error">{errorMessage(login.error)}</FormMessage>}
          <button type="submit" className="btn btn-primary w-full" disabled={login.isPending}>
            {login.isPending ? "Giriş yapılıyor…" : "Giriş yap"}
          </button>
        </form>
      </div>
    </main>
  );
}
