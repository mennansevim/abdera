"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useRef, useState, type FormEvent } from "react";
import { AppLoader } from "@/components/app-loader";
import { BrandMark, Icon, type IconName } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useSessionDestination } from "@/lib/session-destination";
import { useLogin } from "@/lib/use-auth";

type LoginRole = "Admin" | "Teacher" | "Guardian";
type StaffRole = Exclude<LoginRole, "Guardian">;

const ROLE_OPTIONS: { role: LoginRole; title: string; description: string; icon: IconName; color: string }[] = [
  { role: "Admin", title: "Yöneticiyim", description: "Okulu, aidatı ve programı düzenlerim", icon: "bank", color: "#a84e1f" },
  { role: "Teacher", title: "Öğretmenim", description: "Derslerimi görür, yoklama alırım", icon: "teachers", color: "#d76e4d" },
  { role: "Guardian", title: "Veliyim", description: "Ders ve ödeme bildirimlerini takip ederim", icon: "students", color: "#2b918d" },
];

const STAFF_OPTIONS = ROLE_OPTIONS.filter((option) => option.role !== "Guardian");
const GUARDIAN_OPTION = ROLE_OPTIONS.find((option) => option.role === "Guardian");

function roleCardClass(active: boolean) {
  return `pressable flex min-h-[4rem] w-full items-center gap-3 rounded-xl border px-3 text-left shadow-[0_2px_8px_rgba(45,37,31,.025)] ${active ? "border-[1.5px] border-[var(--brand)] bg-[var(--brand-soft)]" : "border-[var(--line)] bg-white hover:border-[#e0c39d]"}`;
}

function RoleCardContent({ option }: { option: (typeof ROLE_OPTIONS)[number] }) {
  return (
    <>
      <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl" style={{ color: option.color, backgroundColor: `${option.color}18` }}>
        <Icon name={option.icon} className="h-5 w-5" />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block text-sm font-bold">{option.title}</span>
        <span className="text-meta mt-0.5 block leading-snug">{option.description}</span>
      </span>
      <Icon name="chevron" className="h-4 w-4 text-[var(--muted)]" />
    </>
  );
}

const DEMO_PASSWORD = "AbderaDemo2026!";
const DEMO_ENABLED = process.env.NEXT_PUBLIC_DEMO_ENABLED === "true";
const DEMO_EMAILS: Record<StaffRole, string> = {
  Admin: "demo.yonetici@abdera.com",
  Teacher: "demo.ogretmen@abdera.com",
};

export default function LoginPage() {
  return (
    <Suspense fallback={<SessionCheckLoading />}>
      <LoginPageContent />
    </Suspense>
  );
}

function LoginPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const login = useLogin();
  const { destination, isResolving } = useSessionDestination();
  const shouldChooseRole = searchParams.get("chooseRole") === "1";
  const emailRef = useRef<HTMLInputElement>(null);
  const roleRefs = useRef<Partial<Record<StaffRole, HTMLButtonElement | null>>>({});
  const [selectedRole, setSelectedRole] = useState<LoginRole>("Admin");
  const [email, setEmail] = useState(DEMO_ENABLED ? DEMO_EMAILS.Admin : "");
  const [password, setPassword] = useState(DEMO_ENABLED ? DEMO_PASSWORD : "");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Veli portalındaki "Ana giriş ekranı" bilinçli bir rol değiştirme isteğidir; bu
    // parametre varken mevcut oturuma otomatik dönmek yerine rol seçeneklerini göster.
    if (!shouldChooseRole && destination) {
      router.replace(destination);
    }
  }, [shouldChooseRole, destination, router]);

  // Seçilen rol sunucuya gönderilir: hesabın rolü seçimle uyuşmuyorsa sunucu 403 döner ve
  // oturum hiç açılmaz. Eskiden yanıt sessizce kabul edilip seçim hesabın gerçek rolüne
  // çekiliyordu - "Yöneticiyim" seçip öğretmen bilgileriyle öğretmen ekranına düşmenin sebebi buydu.
  async function submitLogin(loginEmail: string, loginPassword: string, role: StaffRole) {
    setError(null);
    try {
      const result = await login.mutateAsync({ email: loginEmail, password: loginPassword, expectedRole: role });
      router.push(result.mustChangePassword ? "/dashboard/settings?changePassword=1" : "/dashboard");
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Giriş yapılamadı. Lütfen tekrar dene.");
    }
  }

  function chooseRole(role: LoginRole) {
    setSelectedRole(role);
    setError(null);
    if (role === "Guardian") {
      // Demo yayınında /parent örnek veli oturumunu otomatik açar. Demo kapalıysa
      // koruma normal telefon + OTP ekranına yönlendirir.
      router.push("/parent");
      return;
    }
    setEmail(DEMO_ENABLED ? DEMO_EMAILS[role] : "");
    setPassword(DEMO_ENABLED ? DEMO_PASSWORD : "");
    requestAnimationFrame(() => emailRef.current?.focus());
  }

  // WAI-ARIA radyo grubu: oklar seçimi değiştirir ve odağı yeni seçili seçeneğe taşır.
  function handleRoleKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    const step = event.key === "ArrowDown" || event.key === "ArrowRight" ? 1
      : event.key === "ArrowUp" || event.key === "ArrowLeft" ? -1
        : 0;
    if (!step) return;
    event.preventDefault();
    const roles = STAFF_OPTIONS.map((option) => option.role as StaffRole);
    const currentIndex = Math.max(0, roles.indexOf(selectedRole as StaffRole));
    const next = roles[(currentIndex + step + roles.length) % roles.length]!;
    setSelectedRole(next);
    setError(null);
    setEmail(DEMO_ENABLED ? DEMO_EMAILS[next] : "");
    setPassword(DEMO_ENABLED ? DEMO_PASSWORD : "");
    roleRefs.current[next]?.focus();
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (selectedRole === "Guardian") return; // form zaten gizli; tip daraltması için.
    await submitLogin(email, password, selectedRole);
  }

  if (!shouldChooseRole && (destination || isResolving)) {
    return <SessionCheckLoading />;
  }

  return (
    <main className="flex min-h-dvh items-center justify-center bg-[var(--background)] sm:p-6">
      <section className="min-h-dvh w-full max-w-[420px] overflow-hidden border-[var(--line)] bg-[var(--surface)] shadow-[0_8px_28px_rgba(90,55,20,.08)] sm:min-h-0 sm:border">
        <div className="px-4 pb-5 pt-8 sm:px-6 sm:pb-6 sm:pt-9">
          <div className="mb-8 flex justify-center text-[var(--brand-strong)]">
            <BrandMark />
          </div>

          <div className="mb-4">
            <h1 className="text-[1.05rem] font-bold tracking-[-0.015em]">Nasıl giriş yapmak istersin?</h1>
            <p className="mt-1 text-xs leading-relaxed text-[var(--muted)]">Rolüne göre sana özel çalışma alanına yönlendirilirsin.</p>
          </div>

          {/* Personel rolleri gerçek bir radyo grubu (ok tuşlarıyla gezilir, yalnızca seçili
              olan Tab durağıdır). "Veliyim" seçim değil, ayrı bir giriş ekranına geçiş olduğu
              için grubun dışında sıradan bir düğme. */}
          <div className="space-y-2.5" role="radiogroup" aria-label="Giriş rolü" onKeyDown={handleRoleKeyDown}>
            {STAFF_OPTIONS.map((option) => {
              const active = selectedRole === option.role;
              return (
                <button
                  key={option.role}
                  ref={(element) => { roleRefs.current[option.role as StaffRole] = element; }}
                  type="button"
                  role="radio"
                  aria-checked={active}
                  tabIndex={active ? 0 : -1}
                  onClick={() => chooseRole(option.role)}
                  className={roleCardClass(active)}
                >
                  <RoleCardContent option={option} />
                </button>
              );
            })}
          </div>
          {GUARDIAN_OPTION && (
            <button type="button" onClick={() => chooseRole("Guardian")} className={`mt-2.5 ${roleCardClass(false)}`}>
              <RoleCardContent option={GUARDIAN_OPTION} />
            </button>
          )}

          {selectedRole !== "Guardian" && (
            <form onSubmit={handleSubmit} className="mt-7">
              {DEMO_ENABLED && (
                <div className="mb-5 flex items-center gap-3 text-[.75rem] text-[var(--muted)] before:h-px before:flex-1 before:bg-[var(--line)] after:h-px after:flex-1 after:bg-[var(--line)]">
                  demo bilgileri hazır
                </div>
              )}

              <label htmlFor="email" className={`${DEMO_ENABLED ? "" : "mt-5"} mb-1.5 block text-[.75rem] font-semibold text-[var(--muted)]`}>E-posta</label>
              <input ref={emailRef} id="email" type="email" required autoComplete="username" value={email} onChange={(event) => setEmail(event.target.value)} placeholder="ornek@abdera.com" className="field text-sm" />

              <label htmlFor="password" className="mb-1.5 mt-4 block text-[.75rem] font-semibold text-[var(--muted)]">Şifre</label>
              <input id="password" type="password" required autoComplete="current-password" value={password} onChange={(event) => setPassword(event.target.value)} placeholder="••••••••" className="field text-sm tracking-[.18em]" />

              {error && <p role="alert" className="mt-3 rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}

              <button type="submit" disabled={login.isPending} className="pressable mt-5 min-h-12 w-full rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)] disabled:cursor-wait disabled:opacity-60">
                {login.isPending ? "Giriş yapılıyor…" : "Giriş yap"}
              </button>
            </form>
          )}
        </div>
      </section>
    </main>
  );
}

function SessionCheckLoading() {
  return <AppLoader message="Oturum kontrol ediliyor…" />;
}
