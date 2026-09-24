"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { AppLoader } from "@/components/app-loader";
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

  return <AppLoader message="Oturumun açılıyor…" />;
}
