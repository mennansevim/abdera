"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import type { Me } from "@/lib/api";
import { useLogout } from "@/lib/use-auth";

const LINKS = [
  { href: "/dashboard", label: "Bugün" },
  { href: "/dashboard/students", label: "Öğrenciler" },
  { href: "/dashboard/teachers", label: "Öğretmenler" },
  { href: "/dashboard/calendar", label: "Takvim" },
];

// docs/04-permissions.md: ders değişikliği onay/red ve aidat/tahsilat tamamen Admin -
// linkler Teacher'a gösterilmez (backend zaten 403 verirdi, ama gereksiz tıklamayı da
// önlemeye değer).
const ADMIN_ONLY_LINKS = [
  { href: "/dashboard/change-requests", label: "Değişiklik Talepleri" },
  { href: "/dashboard/billing", label: "Aidatlar" },
  { href: "/dashboard/notifications", label: "Bildirimler" },
  { href: "/dashboard/banking", label: "Banka" },
];

// UX-1 (docs/13-audit-fix-prompt.md): 8 linkli nav dar ekranda flex-wrap ile 3-4 satıra
// yayılıyordu, hamburger/drawer yoktu. md (768px) altında yatay nav gizlenip hamburger +
// sağdan açılan slide-in drawer'a geçiliyor; md ve üstünde eski yatay nav aynen kalıyor.
export function AppHeader({ me }: { me: Me }) {
  const pathname = usePathname();
  const router = useRouter();
  const logout = useLogout();
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const links = me.role === "Admin" ? [...LINKS, ...ADMIN_ONLY_LINKS] : LINKS;

  const handleLogout = () => {
    logout.mutate(undefined, { onSuccess: () => router.replace("/login") });
  };

  return (
    <header className="border-b border-neutral-200 bg-white">
      <div className="mx-auto flex max-w-5xl items-center justify-between gap-3 px-4 py-3">
        <nav className="hidden items-center gap-4 text-sm md:flex">
          {links.map((link) => (
            <Link
              key={link.href}
              href={link.href}
              className={
                pathname === link.href
                  ? "font-semibold text-neutral-900"
                  : "text-neutral-500 hover:text-neutral-900"
              }
            >
              {link.label}
            </Link>
          ))}
        </nav>

        <button
          type="button"
          onClick={() => setIsMenuOpen(true)}
          aria-label="Menüyü aç"
          aria-expanded={isMenuOpen}
          className="flex min-h-11 min-w-11 items-center justify-center rounded-md border border-neutral-300 text-neutral-700 hover:bg-neutral-100 md:hidden"
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} className="h-5 w-5" aria-hidden="true">
            <path strokeLinecap="round" strokeLinejoin="round" d="M4 6h16M4 12h16M4 18h16" />
          </svg>
        </button>

        <div className="hidden items-center gap-3 text-sm text-neutral-500 md:flex">
          <span>
            {me.email} · {me.role === "Admin" ? "Yönetici" : "Öğretmen"}
          </span>
          <button
            onClick={handleLogout}
            className="min-h-11 rounded-md border border-neutral-300 px-3 text-neutral-700 hover:bg-neutral-100"
          >
            Çıkış yap
          </button>
        </div>
      </div>

      {isMenuOpen && (
        <div className="fixed inset-0 z-50 md:hidden">
          <button
            type="button"
            aria-label="Menüyü kapat"
            onClick={() => setIsMenuOpen(false)}
            className="absolute inset-0 bg-black/30"
          />
          <div className="absolute inset-y-0 right-0 flex w-72 max-w-[85vw] flex-col bg-white shadow-xl">
            <div className="flex items-center justify-between border-b border-neutral-200 px-4 py-3">
              <span className="truncate text-sm text-neutral-500">{me.email}</span>
              <button
                type="button"
                onClick={() => setIsMenuOpen(false)}
                aria-label="Menüyü kapat"
                className="flex min-h-11 min-w-11 items-center justify-center rounded-md text-neutral-500 hover:bg-neutral-100"
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} className="h-5 w-5" aria-hidden="true">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            </div>
            <nav className="flex flex-1 flex-col gap-1 overflow-y-auto p-2">
              {links.map((link) => (
                <Link
                  key={link.href}
                  href={link.href}
                  onClick={() => setIsMenuOpen(false)}
                  className={`flex min-h-11 items-center rounded-md px-3 text-base ${
                    pathname === link.href
                      ? "bg-neutral-100 font-semibold text-neutral-900"
                      : "text-neutral-700 hover:bg-neutral-100"
                  }`}
                >
                  {link.label}
                </Link>
              ))}
            </nav>
            <div className="border-t border-neutral-200 p-3">
              <span className="mb-2 block text-xs text-neutral-500">
                {me.role === "Admin" ? "Yönetici" : "Öğretmen"}
              </span>
              <button
                onClick={() => {
                  setIsMenuOpen(false);
                  handleLogout();
                }}
                className="flex min-h-11 w-full items-center justify-center rounded-md border border-neutral-300 text-neutral-700 hover:bg-neutral-100"
              >
                Çıkış yap
              </button>
            </div>
          </div>
        </div>
      )}
    </header>
  );
}
