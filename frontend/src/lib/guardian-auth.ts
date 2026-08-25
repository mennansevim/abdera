// Veli OTP girişi - docs/10-decisions.md Karar F reversal. Auth/use-auth.ts'teki
// e-posta/şifre modeliyle ilgisi yok; oturum Guardian.Id + Role=Guardian claim'iyle kurulur.
"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "./api";

const GUARDIAN_ME_QUERY_KEY = ["guardian", "me"] as const;

export interface GuardianMe {
  id: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
}

export function useGuardianMe() {
  return useQuery<GuardianMe>({
    queryKey: GUARDIAN_ME_QUERY_KEY,
    queryFn: () => api.get<GuardianMe>("/api/guardian/me"),
    retry: false,
  });
}

// /parent altındaki tüm sayfalar bunu kullanır - oturum yoksa /parent/login'e yönlendirir
// (dashboard/lib/use-require-auth.ts'teki desenin veli karşılığı).
export function useRequireGuardianAuth() {
  const router = useRouter();
  const { data: guardian, isLoading, isError } = useGuardianMe();

  useEffect(() => {
    if (isError) {
      router.replace("/parent/login");
    }
  }, [isError, router]);

  return { guardian, isLoading };
}

export interface RequestOtpResult {
  message: string;
  debugCode: string | null;
}

export function useRequestGuardianOtp() {
  return useMutation<RequestOtpResult, ApiError, { phoneNumber: string }>({
    mutationFn: (body) => api.post<RequestOtpResult>("/api/guardian/otp/request", body),
  });
}

export interface VerifyOtpResult {
  id: string;
  firstName: string;
  lastName: string;
}

export function useVerifyGuardianOtp() {
  const queryClient = useQueryClient();
  return useMutation<VerifyOtpResult, ApiError, { phoneNumber: string; code: string }>({
    mutationFn: (body) => api.post<VerifyOtpResult>("/api/guardian/otp/verify", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: GUARDIAN_ME_QUERY_KEY }),
  });
}

// Yalnızca Development backend'inde route edilir; gerçek OTP/WhatsApp gerektirmeden
// veli portalını hızlıca önizlemek için kullanılır.
export function useDebugGuardianLogin() {
  const queryClient = useQueryClient();
  return useMutation<VerifyOtpResult, ApiError, void>({
    mutationFn: () => api.post<VerifyOtpResult>("/api/guardian/debug-login", {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: GUARDIAN_ME_QUERY_KEY }),
  });
}
