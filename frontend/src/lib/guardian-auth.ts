// Veli OTP girişi - docs/10-decisions.md Karar F reversal. Auth/use-auth.ts'teki
// e-posta/şifre modeliyle ilgisi yok; oturum Guardian.Id + Role=Guardian claim'iyle kurulur.
"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError, onSessionLost } from "./api";
import { clearSessionData } from "./session-reset";

const GUARDIAN_ME_QUERY_KEY = ["guardian", "me"] as const;

export interface GuardianMe {
  id: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  // Veli hâlâ ad soyaddan türeyen ilk şifreyle mi giriyor (docs/10-decisions.md Q1).
  usesDefaultPassword: boolean;
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
  const queryClient = useQueryClient();
  const { data: guardian, isLoading, isError } = useGuardianMe();
  const { mutate: openDemoGuardian, isPending: isDemoLoginPending } = useDebugGuardianLogin();
  const attemptedDemoLogin = useRef(false);

  // Portal açıkken oturum düşerse ilk 401'de giriş ekranına dön (personel panelindeki
  // use-require-auth.ts ile aynı kural). Yalnızca bu sekmede açık bir veli oturumu varken:
  // açılıştaki 401 aşağıdaki örnek veli girişi denemesine bırakılır.
  useEffect(() => {
    let handled = false;
    return onSessionLost(() => {
      if (handled || queryClient.getQueryData(GUARDIAN_ME_QUERY_KEY) === undefined) return;
      handled = true;
      clearSessionData(queryClient);
      router.replace("/parent/login");
    });
  }, [queryClient, router]);

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
    // Önceki oturumun verisi silinir (bkz. session-reset.ts), sonra yeni veli bilgisi çekilir.
    onSuccess: () => {
      clearSessionData(queryClient);
      return queryClient.prefetchQuery({ queryKey: GUARDIAN_ME_QUERY_KEY, queryFn: () => api.get<GuardianMe>("/api/guardian/me") });
    },
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
    // Önceki oturumun verisi silinir (bkz. session-reset.ts), sonra yeni veli bilgisi çekilir.
    onSuccess: () => {
      clearSessionData(queryClient);
      return queryClient.prefetchQuery({ queryKey: GUARDIAN_ME_QUERY_KEY, queryFn: () => api.get<GuardianMe>("/api/guardian/me") });
    },
  });
}

// Development veya açıkça demo olarak yapılandırılmış staging backend'inde route edilir.
export function useDebugGuardianLogin() {
  const queryClient = useQueryClient();
  return useMutation<VerifyOtpResult, ApiError, void>({
    mutationFn: () => api.post<VerifyOtpResult>("/api/guardian/debug-login", {}),
    // Önceki oturumun verisi silinir (bkz. session-reset.ts), sonra yeni veli bilgisi çekilir.
    onSuccess: () => {
      clearSessionData(queryClient);
      return queryClient.prefetchQuery({ queryKey: GUARDIAN_ME_QUERY_KEY, queryFn: () => api.get<GuardianMe>("/api/guardian/me") });
    },
  });
}

export function useGuardianLogout() {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, void>({
    // Ortak çıkış ucu güvenlik damgasını da yeniler; 30 günlük hatırlanan veli cookie'sinin
    // kopyası dahi çıkıştan sonra yeniden kullanılamaz.
    mutationFn: () => api.post<void>("/api/auth/logout"),
    onSuccess: () => clearSessionData(queryClient),
  });
}

// docs/10-decisions.md Q1: veli kendi şifresini değiştirir. Sunucu oturumu yeni güvenlik
// damgasıyla tazeler; "varsayılan şifre" hatırlatması için /me yeniden çekilir.
export function useChangeGuardianPassword() {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, { currentPassword: string; newPassword: string }>({
    mutationFn: (body) => api.post<void>("/api/guardian/change-password", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: GUARDIAN_ME_QUERY_KEY }),
  });
}
