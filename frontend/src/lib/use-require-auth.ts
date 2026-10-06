"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useEffect, useRef } from "react";
import { ApiError, onSessionLost } from "./api";
import { clearSessionData } from "./session-reset";
import { ME_QUERY_KEY, useMe } from "./use-auth";

// /dashboard altındaki tüm sayfalar bunu kullanır - oturum yoksa /login'e yönlendirir.
export function useRequireAuth() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { data: me, isLoading, isError, error, isFetching } = useMe();
  const isUnauthorized = error instanceof ApiError && (error.status === 401 || error.status === 403);
  const sessionLost = useRef(false);

  useEffect(() => {
    // isFetching koşulu şart: React Query tazeleme sürerken bir önceki hatayı da taşır.
    // Giriş sonrası ilk /dashboard render'ında bu hata giriş ekranındaki 401 olabilir ve
    // kullanıcı daha oturumu okunmadan /login'e geri atılırdı.
    // Oturum kaybı aşağıda zaten yönlendirildiyse, önbellek silinince yeniden çekilen `me`'nin
    // 401'i "oturumun sona erdi" notunu taşıyan adresi ezmesin.
    if (isError && isUnauthorized && !isFetching && !sessionLost.current) {
      router.replace("/login");
    }
  }, [isError, isUnauthorized, isFetching, router]);

  // Panel açıkken oturum düşerse (bkz. api.ts onSessionLost) ilk 401'de - en geç zil
  // yoklamasının 30 sn'lik turunda - kullanıcı giriş ekranına atılır. Önbellek de silinir:
  // kalırsa eski veriyle çizilen kartlar ve düğmeler çalışıyormuş gibi görünmeye devam eder.
  useEffect(() => {
    return onSessionLost(() => {
      if (sessionLost.current) return;
      sessionLost.current = true;
      // "Oturumun sona erdi" notu yalnızca bu sekmede gerçekten açık bir oturum varken
      // gösterilir; hiç girilmemiş bir tarayıcıda /dashboard açılınca düz giriş ekranı gelir.
      const hadSession = queryClient.getQueryData(ME_QUERY_KEY) !== undefined;
      clearSessionData(queryClient);
      router.replace(hadSession ? "/login?expired=1" : "/login");
    });
  }, [queryClient, router]);

  return { me, isLoading: isLoading || (!me && isFetching), authError: isError && !isUnauthorized };
}
