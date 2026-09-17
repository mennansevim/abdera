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
    // isFetching koşulu şart: React Query tazeleme sürerken bir önceki hatayı da taşır.
    // Giriş sonrası ilk /dashboard render'ında bu hata giriş ekranındaki 401 olabilir ve
    // kullanıcı daha oturumu okunmadan /login'e geri atılırdı.
    if (isError && isUnauthorized && !isFetching) {
      router.replace("/login");
    }
  }, [isError, isUnauthorized, isFetching, router]);

  return { me, isLoading: isLoading || (!me && isFetching), authError: isError && !isUnauthorized };
}
