"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { useAlerts } from "@/lib/alerts";
import { useLogout, type Me } from "@/lib/auth";
import { Icon, type IconName } from "./icons";

interface NavLink {
  href: string;
  label: string;
  icon: IconName;
}

const LINKS: NavLink[] = [
  { href: "/overview", label: "Genel bakış", icon: "activity" },
  { href: "/", label: "Okullar", icon: "home" },
  { href: "/hosts", label: "Sunucular", icon: "server" },
  { href: "/releases", label: "Sürümler", icon: "package" },
  { href: "/rollouts", label: "Dağıtımlar", icon: "rocket" },
  { href: "/alerts", label: "Alarmlar", icon: "bell" },
  { href: "/audit", label: "Denetim", icon: "history" },
];

function isActive(pathname: string, href: string) {
  if (href === "/") return pathname === "/" || pathname.startsWith("/schools");
  return pathname === href || pathname.startsWith(`${href}/`);
}

function BrandMark() {
  return (
    <span className="inline-flex items-center gap-2.5">
      <span className="brand-mark text-sm font-bold" aria-hidden="true">M</span>
      <span className="min-w-0 leading-none">
        <span className="block truncate text-[1.05rem] font-bold tracking-[-0.01em]">Miredo</span>
        <span className="mt-1 block truncate text-[.7rem] font-semibold tracking-[0.12em] opacity-70">PLATFORM</span>
      </span>
    </span>
  );
}

// Operatör paneli kabuğu: masaüstünde okul uygulamasının gradyan kenar çubuğu, mobilde üst
// şerit + çekmece menü. İçerik 16px kenar boşluğuyla akar, yatay kaydırma yok.
export function AppShell({ me, children }: { me: Me; children: ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const logout = useLogout();
  const [menuOpen, setMenuOpen] = useState(false);
  const { data: openAlerts } = useAlerts(true);
  const alertCount = openAlerts?.length ?? 0;

  function handleLogout() {
    logout.mutate(undefined, { onSettled: () => router.replace("/login") });
  }

  const nav = (onNavigate?: () => void) => (
    <nav className="flex flex-col gap-1" aria-label="Ana menü">
      {LINKS.map((link) => {
        const active = isActive(pathname, link.href);
        return (
          <Link
            key={link.href}
            href={link.href}
            onClick={onNavigate}
            aria-current={active ? "page" : undefined}
            className={`pressable flex min-h-10 items-center gap-3 rounded-md px-3 text-sm font-semibold ${active ? "bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "text-[var(--foreground)]/80 hover:bg-[var(--surface-muted)]"}`}
          >
            <Icon name={link.icon} className="h-[1.1rem] w-[1.1rem] shrink-0" />
            <span className="truncate">{link.label}</span>
            {link.href === "/alerts" && alertCount > 0 && (
              <span className="ml-auto rounded-full bg-[var(--danger-soft)] px-1.5 text-[.7rem] font-bold text-[var(--danger-strong)]" aria-label={`${alertCount} açık alarm`}>{alertCount}</span>
            )}
          </Link>
        );
      })}
    </nav>
  );

  const footer = (
    <div className="border-t border-[var(--line)] pt-4">
      <div className="flex items-center gap-2.5 px-2">
        <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-[var(--surface-muted)] text-xs font-bold" aria-hidden="true">{me.email.slice(0, 1).toUpperCase()}</span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-xs font-bold">{me.email}</span>
          <span className="flex items-center gap-1 text-[.72rem] text-[var(--muted)]"><Icon name="shield" className="h-3 w-3" /> İki adımlı doğrulandı</span>
        </span>
        <button type="button" onClick={handleLogout} disabled={logout.isPending} className="pressable grid h-10 w-10 place-items-center rounded-lg text-[var(--muted)] hover:bg-[var(--surface-muted)] hover:text-[var(--foreground)]" aria-label="Çıkış yap" title="Çıkış yap">
          <Icon name="logout" className="h-4 w-4" />
        </button>
      </div>
    </div>
  );

  const topbar = "bg-[var(--sidebar-from)] text-white";

  return (
    <div className="min-h-dvh bg-[var(--background)]">
      <header className={`sticky top-0 z-30 hidden h-[52px] items-center gap-4 px-5 lg:flex ${topbar}`}>
        <Link href="/" className="text-white" aria-label="Okullar"><BrandMark /></Link>
        <div className="flex-1" />
        <span className="flex items-center gap-2 text-[.8125rem] text-white/80"><Icon name="shield" className="h-4 w-4" />{me.email} · İki adımlı doğrulandı</span>
        <button type="button" onClick={handleLogout} disabled={logout.isPending} className="pressable grid h-9 w-9 place-items-center rounded-md text-white/80 hover:bg-white/10 hover:text-white" aria-label="Çıkış yap" title="Çıkış yap">
          <Icon name="logout" className="h-4 w-4" />
        </button>
      </header>

      <div className="lg:grid lg:grid-cols-[14.5rem_minmax(0,1fr)]">
        <aside className="sticky top-[52px] hidden h-[calc(100dvh-52px)] flex-col gap-6 overflow-y-auto border-r border-[var(--line)] bg-[var(--surface)] px-3 py-4 lg:flex">
          <div className="flex-1">{nav()}</div>
        </aside>

        <div className="min-w-0">
          <header className={`sticky top-0 z-30 flex items-center justify-between gap-3 px-4 py-2.5 lg:hidden ${topbar}`}>
            <Link href="/" className="text-white" aria-label="Okullar"><BrandMark /></Link>
            <button type="button" onClick={() => setMenuOpen(true)} className="icon-btn relative text-white hover:bg-white/10" aria-label="Menüyü aç" aria-expanded={menuOpen}>
              <Icon name="menu" className="h-5 w-5" />
              {alertCount > 0 && <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-[var(--danger)]" aria-hidden="true" />}
            </button>
          </header>
          <main className="mx-auto w-full max-w-7xl px-4 py-5 sm:px-6 lg:px-8 lg:py-6">{children}</main>
        </div>
      </div>

      {menuOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <button type="button" aria-label="Menüyü kapat" onClick={() => setMenuOpen(false)} className="absolute inset-0 bg-[var(--overlay)] backdrop-blur-[2px]" />
          <section className={`absolute inset-y-0 right-0 flex w-[min(20rem,86vw)] flex-col gap-6 overflow-y-auto bg-[var(--surface)] px-3 py-5 text-[var(--foreground)]`} role="dialog" aria-modal="true" aria-label="Menü">
            <div className="flex items-center justify-between pl-2">
              <BrandMark />
              <button type="button" onClick={() => setMenuOpen(false)} className="pressable grid h-10 w-10 place-items-center rounded-md text-[var(--muted)] hover:bg-[var(--surface-muted)]" aria-label="Kapat">
                <Icon name="close" className="h-4 w-4" />
              </button>
            </div>
            <div className="flex-1">{nav(() => setMenuOpen(false))}</div>
            {footer}
          </section>
        </div>
      )}
    </div>
  );
}
