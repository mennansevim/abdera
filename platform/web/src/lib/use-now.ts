"use client";

import { useEffect, useState } from "react";

// "3 sa önce" gibi göreli zamanlar için yavaş tıklayan saat. Render sırasında Date.now()
// çağırmak saf olmayan render'dır; değer durumda tutulur ve aralıkla tazelenir.
export function useNow(intervalMs = 30_000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(timer);
  }, [intervalMs]);
  return now;
}
