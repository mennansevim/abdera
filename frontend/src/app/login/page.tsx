"use client";

import { useRouter } from "next/navigation";
import { useRef, useState, type FormEvent } from "react";
import { BrandMark, Icon, type IconName } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useLogin } from "@/lib/use-auth";

type LoginRole = "Admin" | "Teacher" | "Guardian";

const ROLE_OPTIONS: { role: LoginRole; title: string; description: string; icon: IconName; color: string }[] = [
  { role: "Admin", title: "Yöneticiyim", description: "Okulu, aidatı ve programı düzenlerim", icon: "bank", color: "#5b47ae" },
  { role: "Teacher", title: "Öğretmenim", description: "Derslerimi görür, yoklama alırım", icon: "teachers", color: "#d76e4d" },
  { role: "Guardian", title: "Veliyim", description: "Ders ve ödeme bildirimlerini takip ederim", icon: "students", color: "#2b918d" },
];

// Şimdilik geliştirme kolaylığı: AdminBootstrapper.cs'nin oluşturduğu ilk yönetici hesabıyla
// eşleşir (.env / .env.example - Bootstrap__AdminEmail=admin@example.com,
// Bootstrap__AdminPassword=DevAdmin123!). Yalnızca production build'e sızmasın diye env
// kontrolü var - gerçek bir dağıtımda bu alanlar boş kalır.
const DEV_ADMIN_EMAIL = process.env.NODE_ENV !== "production" ? "admin@example.com" : "";
const DEV_ADMIN_PASSWORD = process.env.NODE_ENV !== "production" ? "DevAdmin123!" : "";

export default function LoginPage() {
  const router = useRouter();
  const login = useLogin();
  const emailRef = useRef<HTMLInputElement>(null);
  const [selectedRole, setSelectedRole] = useState<LoginRole>("Admin");
  const [email, setEmail] = useState(DEV_ADMIN_EMAIL);
  const [password, setPassword] = useState(DEV_ADMIN_PASSWORD);
  const [error, setError] = useState<string | null>(null);

  function chooseRole(role: LoginRole) {
    setSelectedRole(role);
    setError(null);
    if (role === "Guardian") {
      router.push("/parent/login");
      return;
    }
    if (role === "Admin") {
      setEmail(DEV_ADMIN_EMAIL);
      setPassword(DEV_ADMIN_PASSWORD);
    } else {
      setEmail("");
      setPassword("");
    }
    requestAnimationFrame(() => emailRef.current?.focus());
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const result = await login.mutateAsync({ email, password });
      if (result.role !== selectedRole) {
        setSelectedRole(result.role);
      }
      router.push(result.mustChangePassword ? "/dashboard?changePassword=1" : "/dashboard");
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

          <div className="mb-4">
            <h1 className="text-[1.05rem] font-bold tracking-[-0.015em]">Nasıl giriş yapmak istersin?</h1>
            <p className="mt-1 text-xs leading-relaxed text-[var(--muted)]">Rolüne göre sana özel çalışma alanına yönlendirilirsin.</p>
          </div>

          <div className="space-y-2.5" role="radiogroup" aria-label="Giriş rolü">
            {ROLE_OPTIONS.map((option) => {
              const active = selectedRole === option.role;
              return (
                <button
                  key={option.role}
                  type="button"
                  role="radio"
                  aria-checked={active}
                  onClick={() => chooseRole(option.role)}
                  className={`pressable flex min-h-[4rem] w-full items-center gap-3 rounded-xl border bg-white px-3 text-left shadow-[0_2px_8px_rgba(45,37,31,.025)] ${active ? "border-[#9c8dd1] ring-2 ring-[#6a54b3]/8" : "border-[var(--line)] hover:border-[#d4ccc3]"}`}
                >
                  <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl" style={{ color: option.color, backgroundColor: `${option.color}18` }}>
                    <Icon name={option.icon} className="h-5 w-5" />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block text-sm font-bold">{option.title}</span>
                    <span className="mt-0.5 block text-[.68rem] leading-snug text-[var(--muted)]">{option.description}</span>
                  </span>
                  <Icon name="chevron" className="h-4 w-4 text-[#aaa3ae]" />
                </button>
              );
            })}
          </div>

          {selectedRole !== "Guardian" && (
            <form onSubmit={handleSubmit} className="mt-7">
              <div className="mb-5 flex items-center gap-3 text-[.65rem] text-[#aaa3ae] before:h-px before:flex-1 before:bg-[var(--line)] after:h-px after:flex-1 after:bg-[var(--line)]">
                e-posta ile giriş yap
              </div>

              <label htmlFor="email" className="mb-1.5 block text-[.7rem] font-semibold text-[#625c68]">E-posta</label>
              <input ref={emailRef} id="email" type="email" required autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="ornek@abdera.com" className="field text-sm" />

              <label htmlFor="password" className="mb-1.5 mt-4 block text-[.7rem] font-semibold text-[#625c68]">Şifre</label>
              <input id="password" type="password" required autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="••••••••" className="field text-sm tracking-[.18em]" />

              {error && <p role="alert" className="mt-3 rounded-xl bg-[#fff0ef] px-3 py-2.5 text-xs font-medium text-[#b84545]">{error}</p>}

              <button type="submit" disabled={login.isPending} className="pressable mt-5 min-h-12 w-full rounded-lg bg-[#5948aa] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(74,55,143,.16)] hover:bg-[#4d3c9b] disabled:cursor-wait disabled:opacity-60">
                {login.isPending ? "Giriş yapılıyor…" : "Giriş yap"}
              </button>
            </form>
          )}
        </div>
      </section>
    </main>
  );
}
