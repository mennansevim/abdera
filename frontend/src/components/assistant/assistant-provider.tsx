"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { AssistantPanel } from "./assistant-panel";

// Asistanı her ekrandan açmak için: üst menüdeki düğme, ⌘K / Ctrl+K ve öğrenci satırındaki
// kısayollar ("Gelişimi özetle") aynı paneli açar. Kısayol cümleyi öğrenci bağlamıyla birlikte
// gönderir; sunucu metinde ad yoksa bağlamdaki öğrenciyi kullanır (AE1).
export interface AssistantRequest {
  id: number;
  text: string;
  studentId?: string;
}

interface AssistantContextValue {
  isOpen: boolean;
  open: () => void;
  close: () => void;
  ask: (text: string, studentId?: string) => void;
}

const AssistantContext = createContext<AssistantContextValue | null>(null);

export function useAssistant() {
  const value = useContext(AssistantContext);
  if (!value) throw new Error("useAssistant, AssistantProvider içinde kullanılmalı.");
  return value;
}

export function AssistantProvider({ children }: { children: ReactNode }) {
  const [isOpen, setIsOpen] = useState(false);
  const [request, setRequest] = useState<AssistantRequest | null>(null);

  const open = useCallback(() => setIsOpen(true), []);
  const close = useCallback(() => setIsOpen(false), []);
  const ask = useCallback((text: string, studentId?: string) => {
    setRequest((current) => ({ id: (current?.id ?? 0) + 1, text, studentId }));
    setIsOpen(true);
  }, []);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setIsOpen((current) => !current);
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  const value = useMemo(() => ({ isOpen, open, close, ask }), [isOpen, open, close, ask]);

  return (
    <AssistantContext.Provider value={value}>
      {children}
      <AssistantPanel open={isOpen} onClose={close} request={request} />
    </AssistantContext.Provider>
  );
}
