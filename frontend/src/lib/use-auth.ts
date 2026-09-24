"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "./api";
import type { LoginResponse, Me, UserRole } from "./api";
import { clearSessionData } from "./session-reset";

const ME_QUERY_KEY = ["auth", "me"] as const;

export function useMe() {
  return useQuery<Me, ApiError>({
    queryKey: ME_QUERY_KEY,
    queryFn: () => api.get<Me>("/api/auth/me"),
    // Kısa süreli API/container kesintisi oturumu düşürmüş gibi görünmemeli.
    // Gerçek yetkisiz yanıtlarda ise yeniden denemek yerine giriş ekranına dönülür.
    retry: (failureCount, error) => {
      if (error instanceof ApiError && (error.status === 401 || error.status === 403)) return false;
      return failureCount < 4;
    },
    retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 5000),
  });
}

export function useLogin() {
  const queryClient = useQueryClient();
  // expectedRole: giriş ekranında seçilen rol. Sunucu, şifre doğru olsa bile hesabın
  // rolü seçimle uyuşmuyorsa 403 döner ve oturum açılmaz - seçim kozmetik değil.
  //
  // passwordless: yalnızca localhost'taki hesap seçicisinden gelir. Sunucudaki
  // /api/dev/auth/login Development + Auth__DevLogin__Enabled=true dışında hiç yoktur (404).
  return useMutation<LoginResponse, ApiError, { email: string; password: string; expectedRole: UserRole; passwordless?: boolean }>({
    mutationFn: ({ passwordless, ...credentials }) => passwordless
      ? api.post<LoginResponse>("/api/dev/auth/login", { role: credentials.expectedRole, email: credentials.email || null })
      : api.post<LoginResponse>("/api/auth/login", credentials),
    // invalidate yerine BEKLENEN bir refetch: giriş ekranı açılırken /api/auth/me bir kez 401
    // alır ve React Query bu hatayı önbellekte tutar. Sadece invalidate edip hemen
    // /dashboard'a geçersek, dashboard layout'u henüz tazelenmemiş sorguyu okur, eski 401'i
    // görür ve kullanıcıyı giriş ekranına geri atar - "ilk girişte hata verdi, tekrar
    // denedim girdi" şikâyetinin sebebi buydu. onSuccess bir promise döndürdüğü için
    // mutateAsync oturum bilgisi tazelenmeden çözülmez.
    //
    // Önce önceki oturumun bütün verisi silinir (bkz. session-reset.ts): arada çıkış yapılmadan
    // (ör. süresi dolan oturumdan sonra) başka bir hesapla girilse bile eski liste çizilmez.
    // Silme, önbellekteki eski 401'i de götürdüğü için yeni `me` burada bekleyerek çekilir.
    onSuccess: () => {
      clearSessionData(queryClient);
      return queryClient.prefetchQuery({ queryKey: ME_QUERY_KEY, queryFn: () => api.get<Me>("/api/auth/me") });
    },
  });
}

export type DevAccount = { email: string; role: UserRole | "Guardian"; name: string | null };

// Yerel hesap seçicisi. Uç yalnızca Development + Auth__DevLogin__Enabled=true iken vardır;
// başka her yerde 404 döner ve giriş ekranı normal e-posta/şifre formunda kalır.
export function useDevAccounts(enabled: boolean) {
  return useQuery<DevAccount[], ApiError>({
    queryKey: ["auth", "dev-accounts"],
    queryFn: () => api.get<DevAccount[]>("/api/dev/auth/accounts"),
    enabled,
    retry: false,
    staleTime: Infinity,
  });
}

export function useLogout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post("/api/auth/logout"),
    // Yalnızca `me` değil, önceki kullanıcının bütün verisi silinir (bkz. session-reset.ts).
    onSuccess: () => clearSessionData(queryClient),
  });
}

export function useChangePassword() {
  return useMutation<void, ApiError, { currentPassword: string; newPassword: string }>({
    mutationFn: (payload) => api.post("/api/auth/change-password", payload),
  });
}
