"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { ApiError } from "./api";
import { useMe } from "./use-auth";

// /dashboard altındaki tüm sayfalar bunu kullanır - oturum yoksa /login'e yönlendirir.
export function useRequireAuth() {
  const router = useRouter();
  const { data: me, isLoading, isError, error, isFetching } = useMe();
  const isUnauthorized = error instanceof ApiError && (error.status === 401 || error.status === 403);

  useEffect(() => {
    if (isError && isUnauthorized) {
      router.replace("/login");
    }
  }, [isError, isUnauthorized, router]);

  return { me, isLoading: isLoading || (!me && isFetching), authError: isError && !isUnauthorized };
}
