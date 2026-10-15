"use client";

import Link from "next/link";
import { AppLoader } from "@/components/app-loader";
import { Icon } from "@/components/icons";
import { isModuleEnabled, useInstanceConfig } from "@/lib/instance";

// Veli portalı opsiyonel modül (docs/multi-tenant/ARCHITECTURE.md §5.2, "parent-portal").
// Kapalı okulda /parent ve /parent/login yerine açıklama gösterilir; uçlar zaten 404 döner.
export default function ParentLayout({ children }: { children: React.ReactNode }) {
  const { data, isLoading } = useInstanceConfig();
  // Yapılandırma gelmeden portal açılmaz: açılırsa veli oturumu/örnek giriş denemesi kapalı uçlara
  // istek atıp giriş ekranına yönlendirir, ardından bu açıklamaya geçer (titreşim).
  if (isLoading) return <AppLoader message="Veli portalı açılıyor…" />;
  if (isModuleEnabled(data, "parent-portal")) return children;

  return (
    <main className="grid min-h-dvh place-items-center bg-[var(--background)] p-5">
      <div className="app-card w-full max-w-md p-6 text-center">
        <span className="mx-auto grid h-11 w-11 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]" aria-hidden="true">
          <Icon name="shield" className="h-5 w-5" />
        </span>
        <h1 className="mt-4 text-lg font-bold">Veli portalı bu okulda açık değil</h1>
        <p className="mt-2 text-sm leading-relaxed text-[var(--muted)]">Ders ve ödeme bilgileri için okul yönetimiyle iletişime geçebilirsiniz.</p>
        <Link href="/login" className="btn btn-quiet mt-4">Personel girişine dön</Link>
      </div>
    </main>
  );
}
