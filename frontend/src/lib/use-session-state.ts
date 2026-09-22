"use client";

import { useEffect, useState } from "react";

// Liste/takvim ekranlarının filtrelerini route değişiminde kaybetmemesi için küçük,
// bağımlılıksız bir sessionStorage köprüsü. İlk değer effect içinde okunduğundan SSR ile
// istemcinin ilk HTML'i aynı kalır; sonraki render kayıtlı çalışma bağlamını geri getirir.
export function useSessionState<T>(
  key: string,
  initialValue: T | (() => T),
  options?: { encode?: (value: T) => string; decode?: (value: string) => T },
) {
  const [value, setValue] = useState<T>(initialValue);
  const [hydrated, setHydrated] = useState(false);

  useEffect(() => {
    try {
      const stored = window.sessionStorage.getItem(key);
      // Harici bir tarayıcı deposunu ilk kez React'e eşitleyen bu tek çağrı bilinçli;
      // ilk SSR/istemci HTML'ini aynı tutup hydration sonrasında çalışma bağlamını yükler.
      // eslint-disable-next-line react-hooks/set-state-in-effect
      if (stored !== null) setValue(options?.decode ? options.decode(stored) : JSON.parse(stored) as T);
    } catch {
      // Bozuk/eski kayıt ekranı kullanılamaz hâle getirmesin; varsayılanla devam edilir.
    } finally {
      setHydrated(true);
    }
  }, [key, options]);

  useEffect(() => {
    if (!hydrated) return;
    try {
      window.sessionStorage.setItem(key, options?.encode ? options.encode(value) : JSON.stringify(value));
    } catch {
      // Gizli mod/depolama kotası gibi durumlarda state yine bellekte çalışmaya devam eder.
    }
  }, [hydrated, key, options, value]);

  return [value, setValue] as const;
}
