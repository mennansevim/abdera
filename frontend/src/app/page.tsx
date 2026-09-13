"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { BrandMark } from "@/components/icons";
import { useSessionDestination } from "@/lib/session-destination";

export default function RootPage() {
  const router = useRouter();
  const { destination, isResolving } = useSessionDestination();

  useEffect(() => {
    if (destination) {
      router.replace(destination);
    } else if (!isResolving) {
      router.replace("/login");
    }
  }, [destination, isResolving, router]);

  return (
    <main className="grid min-h-dvh place-items-center bg-[var(--background)]">
      <div className="flex flex-col items-center gap-3 text-sm font-semibold text-[var(--muted)]">
        <BrandMark compact />
        Oturumun açılıyor…
      </div>
    </main>
  );
}
