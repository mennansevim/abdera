"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { useMe } from "./use-auth";

// /dashboard altındaki tüm sayfalar bunu kullanır - oturum yoksa /login'e yönlendirir.
export function useRequireAuth() {
  const router = useRouter();
  const { data: me, isLoading, isError } = useMe();

  useEffect(() => {
    if (isError) {
      router.replace("/login");
    }
  }, [isError, router]);

  return { me, isLoading };
}
