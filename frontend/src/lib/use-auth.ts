"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "./api";
import type { LoginResponse, Me } from "./api";

const ME_QUERY_KEY = ["auth", "me"] as const;

export function useMe() {
  return useQuery<Me>({
    queryKey: ME_QUERY_KEY,
    queryFn: () => api.get<Me>("/api/auth/me"),
    retry: false,
  });
}

export function useLogin() {
  const queryClient = useQueryClient();
  return useMutation<LoginResponse, ApiError, { email: string; password: string }>({
    mutationFn: (credentials) => api.post<LoginResponse>("/api/auth/login", credentials),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ME_QUERY_KEY });
    },
  });
}

export function useLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post("/api/auth/logout"),
    onSuccess: () => {
      queryClient.setQueryData(ME_QUERY_KEY, null);
      queryClient.invalidateQueries({ queryKey: ME_QUERY_KEY });
    },
  });
}

export function useChangePassword() {
  return useMutation<void, ApiError, { currentPassword: string; newPassword: string }>({
    mutationFn: (payload) => api.post("/api/auth/change-password", payload),
  });
}

export function useVerifyPassword() {
  return useMutation<void, ApiError, string>({
    mutationFn: (currentPassword) => api.post("/api/auth/verify-password", { currentPassword, newPassword: "not-used" }),
  });
}
