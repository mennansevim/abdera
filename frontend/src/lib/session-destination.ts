"use client";

import { useGuardianMe } from "./guardian-auth";
import { useMe } from "./use-auth";

export type SessionDestination = "/dashboard" | "/parent";

// Tek httpOnly cookie hem personel hem veli oturumunu taşır. Giriş bağlantısı açıldığında
// iki mevcut profil ucunu kontrol ederek cookie içindeki gerçek rolün hedefini bulur;
// localStorage gibi güvenilmez bir rol kopyası tutulmaz.
export function useSessionDestination() {
  const staff = useMe();
  const guardian = useGuardianMe();
  const destination: SessionDestination | null = staff.data
    ? "/dashboard"
    : guardian.data
      ? "/parent"
      : null;
  const isResolving = destination === null && (
    staff.isLoading || staff.isFetching || guardian.isLoading || guardian.isFetching
  );

  return { destination, isResolving };
}
