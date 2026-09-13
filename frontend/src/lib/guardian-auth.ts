// Veli OTP girişi - docs/10-decisions.md Karar F reversal. Auth/use-auth.ts'teki
// e-posta/şifre modeliyle ilgisi yok; oturum Guardian.Id + Role=Guardian claim'iyle kurulur.
"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef } from "react";
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

// /parent altındaki tüm sayfalar bunu kullanır. Development veya Demo:Enabled ortamında
// oturum yokken önce örnek veli girişini dener. Endpoint kapalıysa normal OTP ekranına geçilir.
export function useRequireGuardianAuth() {
  const router = useRouter();
  const { data: guardian, isLoading, isError } = useGuardianMe();
  const { mutate: openDemoGuardian, isPending: isDemoLoginPending } = useDebugGuardianLogin();
  const attemptedDemoLogin = useRef(false);

  useEffect(() => {
    if (!isError || attemptedDemoLogin.current) return;
    attemptedDemoLogin.current = true;
    openDemoGuardian(undefined, { onError: () => router.replace("/parent/login") });
  }, [isError, openDemoGuardian, router]);

  return { guardian, isLoading: isLoading || isDemoLoginPending };
}

export interface GuardianLoginResult {
  id: string;
  firstName: string;
  lastName: string;
}

// Karar F (ikinci) reversal: telefon + kalıcı şifre ile giriş (docs/13-...). Birincil giriş
// yolu budur; WhatsApp OTP ikincil seçenek olarak korunur.
export function useGuardianLogin() {
  const queryClient = useQueryClient();
  return useMutation<GuardianLoginResult, ApiError, { phoneNumber: string; password: string }>({
    mutationFn: (body) => api.post<GuardianLoginResult>("/api/guardian/login", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: GUARDIAN_ME_QUERY_KEY }),
  });
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

// Development veya açıkça demo olarak yapılandırılmış staging backend'inde route edilir.
export function useDebugGuardianLogin() {
  const queryClient = useQueryClient();
  return useMutation<VerifyOtpResult, ApiError, void>({
    mutationFn: () => api.post<VerifyOtpResult>("/api/guardian/debug-login", {}),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: GUARDIAN_ME_QUERY_KEY }),
  });
}

export function useGuardianLogout() {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, void>({
    // Ortak çıkış ucu güvenlik damgasını da yeniler; 30 günlük hatırlanan veli cookie'sinin
    // kopyası dahi çıkıştan sonra yeniden kullanılamaz.
    mutationFn: () => api.post<void>("/api/auth/logout"),
    onSuccess: () => queryClient.removeQueries({ queryKey: GUARDIAN_ME_QUERY_KEY }),
  });
}
