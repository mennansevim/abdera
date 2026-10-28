"use client";

import { useRouter } from "next/navigation";
import { useRef, useState, useSyncExternalStore, type FormEvent } from "react";
import { BrandMark, Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import {
  useDebugGuardianLogin,
  useGuardianLogin,
  useRequestGuardianOtp,
  useVerifyGuardianOtp,
} from "@/lib/guardian-auth";

const EXAMPLE_GUARDIAN_PHONE = "0555 000 00 01";
// login/page.tsx'teki DEMO_ENABLED ile aynı bayrak - üretimde (Demo:Enabled=false) backend
// /api/guardian/debug-login'i hiç kaydetmiyor, bu yüzden örnek numara ve "demo veli" butonu da
// gerçek bir veliyi yanıltmasın diye burada gizlenir.
const DEMO_ENABLED = process.env.NEXT_PUBLIC_DEMO_ENABLED === "true";

// docs/10-decisions.md Karar F (ikinci) reversal: veli artık telefon + KALICI ŞİFRE ile giriş
// yapar (şifreyi okul verir, docs/10-decisions.md AA; veli portaldan değiştirebilir). WhatsApp OTP ikincil
// seçenek olarak korunur ("Şifreni bilmiyor musun?"). Demo yayınında örnek veli düğmesi vardır.
type Mode = "password" | "otp-phone" | "otp-code";

// Portal açıkken oturum düşen veli buraya `?expired=1` ile atılır (bkz. guardian-auth.ts).
// useSearchParams statik sayfada Suspense sınırı ister ve hidrasyona kadar formun yerine boş
// bir yedek çizdirirdi; adres çubuğu doğrudan okunur, sunucu çiziminde not yoktur.
const noSubscription = () => () => {};
function useSessionExpired() {
  return useSyncExternalStore(
    noSubscription,
    () => new URLSearchParams(window.location.search).get("expired") === "1",
    () => false,
  );
}

export default function GuardianLoginPage() {
  const router = useRouter();
  const login = useGuardianLogin();
  const requestOtp = useRequestGuardianOtp();
  const verifyOtp = useVerifyGuardianOtp();
  const debugLogin = useDebugGuardianLogin();
  const codeRef = useRef<HTMLInputElement>(null);

  const [phoneNumber, setPhoneNumber] = useState(DEMO_ENABLED ? EXAMPLE_GUARDIAN_PHONE : "");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [code, setCode] = useState("");
  const [mode, setMode] = useState<Mode>("password");
  const [debugCode, setDebugCode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const sessionExpired = useSessionExpired();

  async function handlePasswordLogin(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await login.mutateAsync({ phoneNumber, password });
      router.push("/parent");
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Giriş yapılamadı. Lütfen tekrar dene.");
    }
  }

  async function handleRequestOtp(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const result = await requestOtp.mutateAsync({ phoneNumber });
      setDebugCode(result.debugCode);
      setMode("otp-code");
      requestAnimationFrame(() => codeRef.current?.focus());
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Kod gönderilemedi. Lütfen tekrar dene.");
    }
  }

  async function handleVerifyOtp(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await verifyOtp.mutateAsync({ phoneNumber, code });
      router.push("/parent");
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Giriş yapılamadı. Lütfen tekrar dene.");
    }
  }

  async function handleDebugLogin() {
    setError(null);
    try {
      await debugLogin.mutateAsync();
      router.push("/parent");
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Demo veli girişi yapılamadı. Lütfen tekrar dene.");
    }
  }

  const title =
    mode === "otp-code" ? "Kodu gir" : "Veli girişi";
  const subtitle =
    mode === "password"
      ? "Okula kayıtlı telefon numaran ve şifrenle gir."
      : mode === "otp-phone"
        ? "Kayıtlı telefon numarana WhatsApp üzerinden tek kullanımlık bir kod gönderelim."
        : `${phoneNumber} numarasına gönderilen 6 haneli kodu gir.`;

  const errorBox = error && <p role="alert" className="mt-4 rounded-xl bg-[var(--danger-soft)] px-3.5 py-3 text-sm font-semibold text-[var(--danger-strong)]">{error}</p>;
  const labelClass = "mb-1.5 block text-sm font-semibold";
  // 16px: iOS Safari daha küçük yazılı alana odaklanınca sayfayı yakınlaştırıyor.
  const inputClass = "field min-h-13 text-base";
  const primaryClass = "pressable mt-5 flex min-h-13 w-full items-center justify-center rounded-2xl bg-[var(--brand-strong)] text-base font-bold text-white shadow-[0_8px_20px_rgba(168,78,31,.25)] disabled:cursor-wait disabled:opacity-70";
  const secondaryClass = "pressable mt-2 flex min-h-12 w-full items-center justify-center gap-1.5 rounded-xl text-sm font-semibold text-[var(--muted)]";

  return (
    <main className="flex min-h-dvh flex-col bg-[var(--background)] sm:items-center sm:justify-center sm:p-6">
      <section className="flex w-full flex-1 flex-col sm:max-w-[420px] sm:flex-none sm:overflow-hidden sm:rounded-[1.6rem] sm:bg-white sm:shadow-[var(--shadow-card)] sm:ring-1 sm:ring-[var(--line)]">
        <div className="bg-[linear-gradient(150deg,#fbe2cb,#fdf3e7)] px-5 pb-8 pt-[max(1rem,env(safe-area-inset-top))]">
          <button
            type="button"
            onClick={() => router.push("/login")}
            className="pressable -ml-2 flex min-h-11 items-center gap-1 px-2 text-sm font-semibold text-[var(--muted)]"
          >
            <Icon name="arrow-left" className="h-4 w-4" /> Geri
          </button>
          <div className="mt-4 text-[var(--brand-strong)]">
            <BrandMark />
          </div>
          <h1 className="mt-5 text-[1.75rem] font-bold leading-tight tracking-[-0.015em]">{title}</h1>
          <p className="mt-1.5 text-[.9375rem] leading-relaxed text-[var(--muted)]">{subtitle}</p>
        </div>

        <div className="flex-1 px-5 pb-[max(1.5rem,env(safe-area-inset-bottom))] pt-6">
          {sessionExpired && (
            <p role="status" className="mb-5 rounded-xl bg-[var(--warning-soft)] px-3.5 py-3 text-sm font-semibold text-[var(--warning-strong)]">
              Oturumun sona erdi. Devam etmek için tekrar giriş yap.
            </p>
          )}

          {/* --- Birincil: telefon + şifre --- */}
          {mode === "password" && (
            <form onSubmit={handlePasswordLogin}>
              <label htmlFor="phoneNumber" className={labelClass}>Telefon numarası</label>
              <input
                id="phoneNumber"
                type="tel"
                inputMode="tel"
                required
                autoComplete="username"
                value={phoneNumber}
                onChange={(event) => setPhoneNumber(event.target.value)}
                placeholder="0555 123 45 67"
                className={inputClass}
              />

              <label htmlFor="password" className={`${labelClass} mt-4`}>Şifre</label>
              <div className="relative">
                <input
                  id="password"
                  type={showPassword ? "text" : "password"}
                  required
                  autoComplete="current-password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                  placeholder="Şifren"
                  className={`${inputClass} pr-20`}
                />
                {/* Etiket bilerek "Şifre" kelimesini içermiyor: getByLabel("Şifre") alanın kendisini bulmalı. */}
                <button
                  type="button"
                  onClick={() => setShowPassword((value) => !value)}
                  aria-pressed={showPassword}
                  className="pressable absolute inset-y-0 right-1 my-auto h-10 rounded-lg px-3 text-sm font-bold text-[var(--brand-strong)]"
                >
                  {showPassword ? "Gizle" : "Göster"}
                </button>
              </div>

              {errorBox}

              <button type="submit" disabled={login.isPending} className={primaryClass}>
                {login.isPending ? "Giriş yapılıyor…" : "Giriş yap"}
              </button>

              {/* docs/10-decisions.md AA: şifreyi okul yönetir. "WhatsApp ile kod al" kaldırıldı -
                  kod şablonu Meta'da açılamadığı için (R2) kod veliye hiç ulaşmıyordu. OTP akışı
                  kodda duruyor; şablon açılırsa bu giriş noktası geri eklenebilir. */}
              <p className="mt-5 flex gap-2 rounded-xl bg-[var(--surface-muted)] px-3.5 py-3 text-sm text-[var(--muted)]">
                <Icon name="shield" className="mt-0.5 h-4 w-4 shrink-0" />
                <span>Şifreni bilmiyor ya da unuttuysan okul yönetiminden yeni şifre iste.</span>
              </p>
            </form>
          )}

          {/* --- İkincil: OTP telefon adımı --- */}
          {mode === "otp-phone" && (
            <form onSubmit={handleRequestOtp}>
              <label htmlFor="otpPhone" className={labelClass}>Telefon numarası</label>
              <input
                id="otpPhone"
                type="tel"
                inputMode="tel"
                required
                autoFocus
                autoComplete="tel"
                value={phoneNumber}
                onChange={(event) => setPhoneNumber(event.target.value)}
                placeholder="0555 123 45 67"
                className={inputClass}
              />

              {errorBox}

              <button type="submit" disabled={requestOtp.isPending} className={primaryClass}>
                {requestOtp.isPending ? "Kod gönderiliyor…" : "Kod gönder"}
              </button>

              <button type="button" onClick={() => { setMode("password"); setError(null); }} className={secondaryClass}>
                <Icon name="arrow-left" className="h-4 w-4" /> Şifreyle girişe dön
              </button>
            </form>
          )}

          {/* --- İkincil: OTP kod adımı --- */}
          {mode === "otp-code" && (
            <form onSubmit={handleVerifyOtp}>
              <label htmlFor="code" className={labelClass}>Doğrulama kodu</label>
              <input
                ref={codeRef}
                id="code"
                type="text"
                inputMode="numeric"
                pattern="[0-9]*"
                maxLength={6}
                required
                autoComplete="one-time-code"
                value={code}
                onChange={(event) => setCode(event.target.value.replace(/\D/g, ""))}
                placeholder="••••••"
                className="field min-h-14 text-center text-xl tracking-[.5em]"
              />

              {debugCode && (
                <p className="mt-2 rounded-xl bg-[var(--warning-soft)] px-3 py-2 text-center text-sm font-semibold text-[var(--warning-strong)]">
                  Geliştirme kodu: {debugCode}
                </p>
              )}

              {errorBox}

              <button type="submit" disabled={verifyOtp.isPending || code.length !== 6} className={primaryClass}>
                {verifyOtp.isPending ? "Giriş yapılıyor…" : "Giriş yap"}
              </button>

              <button type="button" onClick={() => { setMode("otp-phone"); setCode(""); setError(null); setDebugCode(null); }} className={secondaryClass}>
                <Icon name="arrow-left" className="h-4 w-4" /> Farklı bir numara kullan
              </button>
            </form>
          )}

          {/* Demo veli - yalnızca dev/demo backend'inde çalışır, üretimde DEMO_ENABLED false
              olduğundan hem bu buton hem backend ucu (guardian/debug-login) birlikte kapanır. */}
          {DEMO_ENABLED && (
            <button
              type="button"
              onClick={handleDebugLogin}
              disabled={debugLogin.isPending}
              className="pressable mt-6 min-h-12 w-full rounded-xl border border-[var(--line)] bg-white px-4 text-sm font-bold text-[var(--muted)] disabled:cursor-wait disabled:opacity-60"
            >
              {debugLogin.isPending ? "Demo veli açılıyor…" : "Demo veli olarak devam et"}
            </button>
          )}
        </div>
      </section>
    </main>
  );
}
