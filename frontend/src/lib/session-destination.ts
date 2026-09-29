"use client";

import { ApiError } from "./api";
import { useGuardianMe } from "./guardian-auth";
import { useMe } from "./use-auth";

export type SessionDestination = "/dashboard" | "/parent";

// Tek httpOnly cookie hem personel hem veli oturumunu taşır. Giriş bağlantısı açıldığında
// iki mevcut profil ucunu kontrol ederek cookie içindeki gerçek rolün hedefini bulur;
// localStorage gibi güvenilmez bir rol kopyası tutulmaz.
export function useSessionDestination() {
  const staff = useMe();
  const guardian = useGuardianMe();
  // React Query başarısız bir tazelemeden sonra da önceki `data`'yı tutar. Sunucu oturumu
  // reddettiyse (401/403) o eski veri "oturum açık" sayılmamalı: sayılırsa giriş ekranı
  // /dashboard'a, dashboard da 401 yüzünden /login'e yönlendirip sonsuz döngüye giriyordu.
  const staffSignedIn = !!staff.data && !isUnauthorized(staff.error);
  const guardianSignedIn = !!guardian.data && !isUnauthorized(guardian.error);
  const destination: SessionDestination | null = staffSignedIn
    ? "/dashboard"
    : guardianSignedIn
      ? "/parent"
      : null;
  const isResolving = destination === null && (
    staff.isLoading || staff.isFetching || guardian.isLoading || guardian.isFetching
  );

  return { destination, isResolving };
}

function isUnauthorized(error: unknown) {
  return error instanceof ApiError && (error.status === 401 || error.status === 403);
}
