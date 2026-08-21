"use client";

import { useRouter } from "next/navigation";
import { useRef, useState, type FormEvent } from "react";
import { BrandMark, Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useRequestGuardianOtp, useVerifyGuardianOtp } from "@/lib/guardian-auth";

// docs/10-decisions.md Karar F reversal: veli e-posta/şifre yerine telefon + WhatsApp OTP ile
// giriş yapar (Guardian'ın hiç e-postası/şifresi yok, bkz. login/page.tsx'teki Admin/Öğretmen
// akışından farkı). DebugCode yalnızca backend Development ortamındayken dolu gelir - gerçek
// bir WABA hesabı olmadan uçtan uca test edilebilsin diye (GuardianAuth.cs'teki yoruma bak).
export default function GuardianLoginPage() {
  const router = useRouter();
  const requestOtp = useRequestGuardianOtp();
  const verifyOtp = useVerifyGuardianOtp();
  const codeRef = useRef<HTMLInputElement>(null);

  const [phoneNumber, setPhoneNumber] = useState("");
  const [code, setCode] = useState("");
  const [step, setStep] = useState<"phone" | "code">("phone");
  const [debugCode, setDebugCode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleRequestOtp(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const result = await requestOtp.mutateAsync({ phoneNumber });
      setDebugCode(result.debugCode);
      setStep("code");
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

  return (
    <main className="flex min-h-dvh items-center justify-center bg-[#efede6] sm:p-6">
      <section className="min-h-dvh w-full max-w-[420px] overflow-hidden border-[#ddd7ce] bg-[#fbf8f3] shadow-[0_8px_28px_rgba(46,37,30,.07)] sm:min-h-0 sm:border">
        <div className="px-4 pb-5 pt-8 sm:px-6 sm:pb-6 sm:pt-9">
          <div className="mb-8 flex justify-center text-[var(--brand-strong)]">
            <BrandMark />
          </div>

          <button
            type="button"
            onClick={() => router.push("/login")}
            className="pressable mb-5 flex items-center gap-1.5 text-[.68rem] font-semibold text-[var(--muted)]"
          >
            <Icon name="arrow-left" className="h-3.5 w-3.5" /> Ana sayfaya dön
          </button>

          <div className="mb-6">
            <h1 className="text-[1.05rem] font-bold tracking-[-0.015em]">
              {step === "phone" ? "Veli girişi" : "Telefonuna gelen kodu gir"}
            </h1>
            <p className="mt-1 text-xs leading-relaxed text-[var(--muted)]">
              {step === "phone"
                ? "Kayıtlı telefon numarana WhatsApp üzerinden bir doğrulama kodu gönderelim."
                : `${phoneNumber} numarasına gönderilen 6 haneli kodu gir.`}
            </p>
          </div>

          {step === "phone" ? (
            <form onSubmit={handleRequestOtp}>
              <label htmlFor="phoneNumber" className="mb-1.5 block text-[.7rem] font-semibold text-[#625c68]">Telefon numarası</label>
              <input
                id="phoneNumber"
                type="tel"
                required
                autoFocus
                autoComplete="tel"
                value={phoneNumber}
                onChange={(event) => setPhoneNumber(event.target.value)}
                placeholder="0555 123 45 67"
                className="field text-sm"
              />

              {error && <p role="alert" className="mt-3 rounded-xl bg-[#fff0ef] px-3 py-2.5 text-xs font-medium text-[#b84545]">{error}</p>}

              <button type="submit" disabled={requestOtp.isPending} className="pressable mt-5 min-h-12 w-full rounded-lg bg-[#5948aa] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(74,55,143,.16)] hover:bg-[#4d3c9b] disabled:cursor-wait disabled:opacity-60">
                {requestOtp.isPending ? "Kod gönderiliyor…" : "Kod gönder"}
              </button>
            </form>
          ) : (
            <form onSubmit={handleVerifyOtp}>
              <label htmlFor="code" className="mb-1.5 block text-[.7rem] font-semibold text-[#625c68]">Doğrulama kodu</label>
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
                className="field text-center text-lg tracking-[.5em]"
              />

              {debugCode && (
                <p className="mt-2 rounded-xl bg-[#fff9e8] px-3 py-2 text-center text-[.68rem] font-semibold text-[#7f5d0d]">
                  Geliştirme kodu: {debugCode}
                </p>
              )}

              {error && <p role="alert" className="mt-3 rounded-xl bg-[#fff0ef] px-3 py-2.5 text-xs font-medium text-[#b84545]">{error}</p>}

              <button type="submit" disabled={verifyOtp.isPending || code.length !== 6} className="pressable mt-5 min-h-12 w-full rounded-lg bg-[#5948aa] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(74,55,143,.16)] hover:bg-[#4d3c9b] disabled:cursor-wait disabled:opacity-60">
                {verifyOtp.isPending ? "Giriş yapılıyor…" : "Giriş yap"}
              </button>

              <button
                type="button"
                onClick={() => { setStep("phone"); setCode(""); setError(null); setDebugCode(null); }}
                className="pressable mt-3 flex min-h-11 w-full items-center justify-center gap-1.5 rounded-xl text-xs font-semibold text-[var(--muted)]"
              >
                <Icon name="arrow-left" className="h-3.5 w-3.5" /> Farklı bir numara kullan
              </button>
            </form>
          )}
        </div>
      </section>
    </main>
  );
}
