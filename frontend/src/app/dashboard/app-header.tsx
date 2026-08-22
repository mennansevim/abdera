"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { BrandMark, Icon, type IconName } from "@/components/icons";
import type { Me } from "@/lib/api";
import { useLogout } from "@/lib/use-auth";

type NavItem = { href: string; label: string; icon: IconName; alert?: boolean };

const CORE_LINKS: NavItem[] = [
  { href: "/dashboard", label: "Bugün", icon: "home" },
  { href: "/dashboard/students", label: "Öğrenciler", icon: "students" },
  { href: "/dashboard/teachers", label: "Öğretmenler", icon: "teachers" },
  { href: "/dashboard/calendar", label: "Takvim", icon: "calendar" },
];

const ADMIN_LINKS: NavItem[] = [
  { href: "/dashboard/billing", label: "Aidatlar", icon: "wallet" },
  { href: "/dashboard/notifications", label: "Mesaj Merkezi", icon: "bell", alert: true },
  { href: "/dashboard/costs", label: "Maliyet Takibi", icon: "bank" },
  { href: "/dashboard/banking", label: "Banka", icon: "bank", alert: true },
  { href: "/dashboard/change-requests", label: "Değişiklik Talepleri", icon: "swap" },
  { href: "/dashboard/backups", label: "Yedekleme", icon: "shield" },
];

const SETTINGS_LINK: NavItem = { href: "/dashboard/settings", label: "Ayarlar", icon: "settings" };

function isActive(pathname: string, href: string) {
  return href === "/dashboard" ? pathname === href : pathname.startsWith(href);
}

function displayName(email: string) {
  return email
    .split("@")[0]
    .split(/[._-]/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toLocaleUpperCase("tr-TR") + part.slice(1))
    .join(" ");
}

export function AppShell({ me, children }: { me: Me; children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const logout = useLogout();
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const links = me.role === "Admin" ? [...CORE_LINKS, ...ADMIN_LINKS, SETTINGS_LINK] : [...CORE_LINKS, SETTINGS_LINK];
  const mobilePrimary: NavItem[] = me.role === "Admin"
    ? [CORE_LINKS[0]!, CORE_LINKS[3]!, ADMIN_LINKS[0]!, ADMIN_LINKS[1]!]
    : [
        { ...CORE_LINKS[0], label: "Bugün" },
        { ...CORE_LINKS[3], label: "Takvimim" },
        { ...CORE_LINKS[1], label: "Öğrencilerim" },
      ];

  useEffect(() => {
    if (!isMenuOpen) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setIsMenuOpen(false);
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isMenuOpen]);

  function handleLogout() {
    logout.mutate(undefined, { onSuccess: () => router.replace("/login") });
  }

  return (
    <div className="min-h-dvh bg-[var(--background)] lg:grid lg:grid-cols-[15rem_minmax(0,1fr)]">
      <aside className="sticky top-0 hidden h-dvh flex-col overflow-hidden bg-[linear-gradient(160deg,var(--sidebar-from)_0%,#c15a4a_45%,var(--sidebar-to)_100%)] px-3 py-5 text-white lg:flex">
        <Link href="/dashboard" className="mb-6 px-2 text-white"><BrandMark /></Link>
        <nav className="flex flex-1 flex-col gap-1" aria-label="Ana menü">
          {links.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              aria-current={isActive(pathname, link.href) ? "page" : undefined}
              className={`pressable group flex min-h-11 items-center gap-3 rounded-2xl px-3 text-sm font-bold ${
                isActive(pathname, link.href)
                  ? "bg-white text-[var(--brand-strong)] shadow-[0_4px_14px_rgba(0,0,0,.12)]"
                  : "text-white/85 hover:bg-white/10 hover:text-white"
              }`}
            >
              <Icon name={link.icon} className="h-[1.1rem] w-[1.1rem] shrink-0" />
              <span className="truncate">{link.label}</span>
              {link.alert && <span className="ml-auto h-2 w-2 rounded-full bg-[#ffe27a] ring-1 ring-black/10" aria-label="Dikkat gereken kayıtlar olabilir" />}
            </Link>
          ))}
        </nav>
        <div className="mt-4 border-t border-white/25 pt-4">
          <div className="flex items-center gap-2.5 rounded-xl px-2 py-2">
            <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-white/25 text-xs font-bold">
              {displayName(me.email).slice(0, 2).toLocaleUpperCase("tr-TR")}
            </span>
            <span className="min-w-0 flex-1">
              <span className="block truncate text-xs font-bold">{displayName(me.email) || me.email}</span>
              <span className="block text-[.65rem] text-white/70">{me.role === "Admin" ? "Yönetici" : "Öğretmen"}</span>
            </span>
            <button onClick={handleLogout} disabled={logout.isPending} className="pressable grid h-10 w-10 place-items-center rounded-lg text-white/75 hover:bg-white/15 hover:text-white" aria-label="Çıkış yap">
              <Icon name="logout" className="h-4 w-4" />
            </button>
          </div>
        </div>
      </aside>

      <div className="min-w-0">
        <header className={`${me.role === "Teacher" ? "hidden" : "flex"} sticky top-0 z-30 h-16 items-center justify-between border-b border-black/5 bg-[rgba(248,246,241,.82)] px-4 backdrop-blur-xl lg:hidden`}>
          <Link href="/dashboard" className="text-[var(--brand-strong)]"><BrandMark /></Link>
          <button onClick={() => setIsMenuOpen(true)} className="pressable grid h-11 w-11 place-items-center rounded-xl border border-[var(--line)] bg-white text-[var(--brand-strong)]" aria-label="Tüm menüyü aç" aria-expanded={isMenuOpen}>
            <Icon name="menu" className="h-5 w-5" />
          </button>
        </header>

        <main className={`mx-auto w-full max-w-[94rem] px-4 pb-24 sm:px-6 lg:min-h-dvh lg:px-8 lg:pb-10 lg:pt-7 xl:px-10 ${me.role === "Teacher" ? "min-h-dvh pt-4" : "min-h-[calc(100dvh-4rem)] pt-5"}`}>
          {children}
        </main>

        <nav className={`fixed inset-x-0 bottom-0 z-30 grid ${me.role === "Admin" ? "grid-cols-5" : "grid-cols-4"} border-t border-black/5 bg-[rgba(255,253,249,.94)] px-2 pb-[max(.35rem,env(safe-area-inset-bottom))] pt-1 backdrop-blur-2xl lg:hidden`} aria-label="Mobil ana menü">
          {mobilePrimary.map((link) => <MobileNavLink key={link.href} link={link} active={isActive(pathname, link.href)} />)}
          <button onClick={() => setIsMenuOpen(true)} className="pressable flex min-h-14 flex-col items-center justify-center gap-1 rounded-xl text-[.61rem] font-medium text-[var(--muted)]" aria-label={me.role === "Admin" ? "Daha fazla menü" : "Profili aç"}>
            <Icon name={me.role === "Admin" ? "more" : "teachers"} className="h-[1.05rem] w-[1.05rem]" /><span>{me.role === "Admin" ? "Daha Fazla" : "Profil"}</span>
          </button>
        </nav>
      </div>

      {isMenuOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <button aria-label="Menüyü kapat" onClick={() => setIsMenuOpen(false)} className="absolute inset-0 bg-[#171320]/35 backdrop-blur-[2px]" />
          <section className="absolute inset-y-0 right-0 flex w-[min(22rem,88vw)] flex-col bg-[var(--surface)] shadow-[-24px_0_60px_rgba(30,22,53,.18)]" role="dialog" aria-modal="true" aria-label="Uygulama menüsü">
            <div className="flex items-center justify-between border-b border-[var(--line)] px-5 py-4">
              <BrandMark />
              <button onClick={() => setIsMenuOpen(false)} className="pressable grid h-11 w-11 place-items-center rounded-xl hover:bg-black/5" aria-label="Menüyü kapat"><Icon name="close" className="h-5 w-5" /></button>
            </div>
            <nav className="flex-1 space-y-1 overflow-y-auto p-3">
              {links.map((link) => (
                <Link key={link.href} href={link.href} onClick={() => setIsMenuOpen(false)} className={`pressable flex min-h-12 items-center gap-3 rounded-xl px-3 text-sm font-medium ${isActive(pathname, link.href) ? "bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "text-[#5c4d3f] hover:bg-black/[.035]"}`}>
                  <Icon name={link.icon} className="h-5 w-5" /><span>{link.label}</span>{link.alert && <span className="ml-auto h-1.5 w-1.5 rounded-full bg-[var(--danger)]" />}
                </Link>
              ))}
            </nav>
            <div className="border-t border-[var(--line)] p-4 pb-[max(1rem,env(safe-area-inset-bottom))]">
              <p className="truncate text-sm font-semibold">{displayName(me.email) || me.email}</p>
              <p className="mt-0.5 text-xs text-[var(--muted)]">{me.email} · {me.role === "Admin" ? "Yönetici" : "Öğretmen"}</p>
              <button onClick={handleLogout} className="pressable mt-4 flex min-h-11 w-full items-center justify-center gap-2 rounded-xl border border-[var(--line)] bg-white text-sm font-semibold text-[#5c4d3f] hover:border-[#e0c39d]">
                <Icon name="logout" className="h-4 w-4" /> Çıkış yap
              </button>
            </div>
          </section>
        </div>
      )}
    </div>
  );
}

function MobileNavLink({ link, active }: { link: NavItem; active: boolean }) {
  return (
    <Link href={link.href} aria-current={active ? "page" : undefined} className={`pressable flex min-h-14 flex-col items-center justify-center gap-1 rounded-xl text-[.61rem] font-medium ${active ? "text-[var(--brand)]" : "text-[var(--muted)]"}`}>
      <Icon name={link.icon} className="h-[1.05rem] w-[1.05rem]" /><span className="max-w-full truncate">{link.label}</span>
    </Link>
  );
}
