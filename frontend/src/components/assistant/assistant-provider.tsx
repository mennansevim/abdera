"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from "react";
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
  // Rol asistanı kullanamıyorsa (AE7) düğmeler ve kısayollar hiç çizilmez.
  enabled: boolean;
  isOpen: boolean;
  open: () => void;
  close: () => void;
  // Ekrana giden bağlantılar: panelin yanında sayfaya yer varsa panel açık kalır.
  closeForPage: () => void;
  ask: (text: string, studentId?: string) => void;
}

const AssistantContext = createContext<AssistantContextValue | null>(null);

export function useAssistant() {
  const value = useContext(AssistantContext);
  if (!value) throw new Error("useAssistant, AssistantProvider içinde kullanılmalı.");
  return value;
}

// Panel lg'den itibaren sayfanın yanında durur (pencere değildir); 90rem'den itibaren sayfa da
// ona yer açar (globals.css .assistant-open). Daha dar masaüstünde panel sayfanın üstüne biner.
const DOCKED_QUERY = "(min-width: 64rem)";
const ROOMY_QUERY = "(min-width: 90rem)";

function useMediaQuery(query: string) {
  return useSyncExternalStore(
    (onChange) => {
      const list = window.matchMedia(query);
      list.addEventListener("change", onChange);
      return () => list.removeEventListener("change", onChange);
    },
    () => window.matchMedia(query).matches,
    () => false,
  );
}

export function AssistantProvider({ enabled, children }: { enabled: boolean; children: ReactNode }) {
  const [isOpen, setIsOpen] = useState(false);
  const [request, setRequest] = useState<AssistantRequest | null>(null);
  const docked = useMediaQuery(DOCKED_QUERY);
  const roomy = useMediaQuery(ROOMY_QUERY);

  const open = useCallback(() => setIsOpen(true), []);
  const close = useCallback(() => setIsOpen(false), []);
  const closeForPage = useCallback(() => {
    if (!roomy) setIsOpen(false);
  }, [roomy]);
  const ask = useCallback((text: string, studentId?: string) => {
    if (!enabled) return;
    setRequest((current) => ({ id: (current?.id ?? 0) + 1, text, studentId }));
    setIsOpen(true);
  }, [enabled]);

  useEffect(() => {
    if (!enabled) return;
    function onKeyDown(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setIsOpen((current) => !current);
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [enabled]);

  const value = useMemo(() => ({ enabled, isOpen, open, close, closeForPage, ask }), [enabled, isOpen, open, close, closeForPage, ask]);

  return (
    <AssistantContext.Provider value={value}>
      {/* Panel pencere gibi açıldığında (telefon, tablet) arkadaki uygulama klavyeye de kapanır. */}
      <div className={isOpen ? "assistant-open" : undefined} inert={isOpen && !docked}>{children}</div>
      {enabled && <AssistantPanel open={isOpen} modal={!docked} onClose={close} request={request} />}
    </AssistantContext.Provider>
  );
}
