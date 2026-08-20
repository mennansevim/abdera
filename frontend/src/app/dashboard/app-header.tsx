"use client";

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
];

export function AppHeader({ me }: { me: Me }) {
  const pathname = usePathname();
  const router = useRouter();
  const logout = useLogout();
  const links = me.role === "Admin" ? [...LINKS, ...ADMIN_ONLY_LINKS] : LINKS;

  return (
    <header className="border-b border-neutral-200 bg-white">
      <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3 px-4 py-3">
        <nav className="flex items-center gap-4 text-sm">
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
        <div className="flex items-center gap-3 text-sm text-neutral-500">
          <span>
            {me.email} · {me.role === "Admin" ? "Yönetici" : "Öğretmen"}
          </span>
          <button
            onClick={() => logout.mutate(undefined, { onSuccess: () => router.replace("/login") })}
            className="rounded-md border border-neutral-300 px-3 py-1.5 text-neutral-700 hover:bg-neutral-100"
          >
            Çıkış yap
          </button>
        </div>
      </div>
    </header>
  );
}
