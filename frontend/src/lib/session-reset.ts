"use client";

import type { QueryClient } from "@tanstack/react-query";

// Oturum değiştiğinde (giriş, çıkış) önceki kullanıcının verisini ekrandan tamamen siler.
//
// React Query önbelleği bellekte, sekme boyunca yaşar. Önceden çıkışta yalnızca oturum
// bilgisi (`me`) temizleniyordu; aynı sekmede başka bir hesapla girilince öğrenci listesi gibi
// sorgular önceki oturumun verisiyle anında çiziliyor, sunucudan yeni kullanıcının kapsamlı
// listesi gelince yerine geçiyordu - öğretmen, yöneticinin gördüğü öğrencileri bir an için
// görüyordu (kullanıcı tarafından bildirilen gerçek bir veri sızıntısı). Aynı sebeple
// sessionStorage'daki `abdera:` önekli filtre/taslak anahtarları da (önceki kullanıcının
// seçtiği öğretmen/öğrenci kimlikleri, yazılmamış veli yorumu taslağı) silinir.
const SESSION_STORAGE_PREFIX = "abdera:";

export function clearSessionData(queryClient: QueryClient) {
  void queryClient.cancelQueries();
  queryClient.clear();

  try {
    const keys: string[] = [];
    for (let index = 0; index < window.sessionStorage.length; index++) {
      const key = window.sessionStorage.key(index);
      if (key?.startsWith(SESSION_STORAGE_PREFIX)) keys.push(key);
    }
    keys.forEach((key) => window.sessionStorage.removeItem(key));
  } catch {
    // Gizli pencere / engellenmiş depolama: temizlenecek bir şey de yoktur.
  }
}
