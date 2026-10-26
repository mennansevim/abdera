"use client";

import { Icon } from "@/components/icons";
import { useAssistant } from "./assistant-provider";

// Masaüstünde kenar menüsünün başında, ⌘K kısayolunu da gösteren düğme.
export function SidebarAssistantButton({ collapsed }: { collapsed: boolean }) {
  const { enabled, open } = useAssistant();
  if (!enabled) return null;
  return (
    <button
      type="button"
      onClick={open}
      aria-label="Abdera Asistanı (⌘K)"
      title={collapsed ? "Asistan (⌘K)" : undefined}
      className={`pressable mb-3 flex min-h-11 shrink-0 items-center gap-2.5 rounded-2xl bg-white/15 text-sm font-bold text-white ring-1 ring-white/25 hover:bg-white/25 ${collapsed ? "justify-center px-0" : "px-3"}`}
    >
      <Icon name="sparkles" className="h-[1.1rem] w-[1.1rem] shrink-0" />
      {!collapsed && <>
        <span className="truncate">Asistan</span>
        <kbd className="ml-auto rounded-md bg-white/20 px-1.5 py-0.5 font-sans text-[.7rem] font-semibold">⌘K</kbd>
      </>}
    </button>
  );
}

// Telefonda alt menünün üstünde duran yuvarlak düğme (alt menüde boş sekme yok).
export function MobileAssistantButton() {
  const { enabled, open } = useAssistant();
  if (!enabled) return null;
  return (
    <button
      type="button"
      onClick={open}
      aria-label="Abdera Asistanı"
      className="pressable fixed bottom-[calc(5rem+env(safe-area-inset-bottom))] right-3 z-30 grid h-12 w-12 place-items-center rounded-full bg-[var(--brand-strong)] text-white shadow-[0_12px_32px_rgba(84,43,28,.28)] ring-1 ring-white/50 lg:hidden"
    >
      <Icon name="sparkles" className="h-5 w-5" />
    </button>
  );
}
