"use client";

// Operatör girişi: parola + TOTP (platform/api Features/Auth.cs). TOTP etkinleşmeden yalnızca
// kurulum uçları açıktır; okul/sunucu yönetimi TOTP doğrulanmış oturum ister (403).
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "./api";

export interface Me {
  id: string;
  email: string;
  totpEnabled: boolean;
  totpVerified: boolean;
}

export interface LoginInput {
  email: string;
  password: string;
  totpCode?: string;
}

export interface TotpSetup {
  secret: string;
  provisioningUri: string;
}

export const meKey = ["auth", "me"] as const;

// Oturum yoksa (401) null döner - "giriş yapılmamış" bir hata değil, bir durumdur.
export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: async () => {
      try {
        return await api.get<Me>("/api/auth/me");
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null;
        throw error;
      }
    },
    staleTime: 60_000,
    retry: false,
  });
}

export function useLogin() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: LoginInput) => api.post<Me>("/api/auth/login", {
      email: input.email.trim(),
      password: input.password,
      totpCode: input.totpCode?.trim() || null,
    }),
    onSuccess: (me) => {
      client.clear();
      client.setQueryData(meKey, me);
    },
  });
}

export function useLogout() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<void>("/api/auth/logout"),
    onSettled: () => {
      client.clear();
      client.setQueryData(meKey, null);
    },
  });
}

export function useTotpSetup() {
  return useMutation({ mutationFn: () => api.post<TotpSetup>("/api/auth/totp/setup") });
}

export function useTotpConfirm() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (code: string) => api.post<Me>("/api/auth/totp/confirm", { code: code.trim() }),
    onSuccess: (me) => client.setQueryData(meKey, me),
  });
}
