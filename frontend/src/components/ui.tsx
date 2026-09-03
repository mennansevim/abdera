"use client";

import { useEffect, useRef, type ReactNode } from "react";
import { Icon } from "./icons";

// Ekranların ortak iskeleti. Önceki sürümde her sayfa kendi başlık bloğunu (üstte küçük
// büyük harfli bir "göz kırpma" satırı + serif başlık + açıklama) ve altına HER ZAMAN AÇIK
// duran bir oluşturma formunu kuruyordu. Form ekranın en görünür parçası olduğu için asıl
// içerik (liste) aşağı itiliyordu. Yeni kural: başlık + tek satır açıklama + sağda küçük
// bir "+" eylemi; oluşturma formu istendiğinde Modal içinde açılır.
export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) {
  return (
    <header className="flex flex-wrap items-center justify-between gap-3">
      <div className="min-w-0">
        <h1 className="text-display font-serif italic">{title}</h1>
        {description && <p className="text-meta mt-1">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
    </header>
  );
}

// Kart/bölüm başlığı - sayfa başlığının küçük kardeşi, aynı yerleşim kuralıyla.
export function SectionHeader({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <div className="min-w-0">
        <h2 className="text-title">{title}</h2>
        {description && <p className="text-meta mt-1">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </div>
  );
}

// Küçük "+" eylemi. Metin taşımaz: erişilebilir adı ve fare ipucu `label`'dan gelir, bu
// yüzden label her zaman ne eklendiğini söylemeli ("Öğretmen ekle" gibi).
export function AddButton({ label, onClick, disabled = false, tone = "brand" }: { label: string; onClick: () => void; disabled?: boolean; tone?: "brand" | "quiet" }) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-label={label}
      title={label}
      className={`icon-btn ${tone === "brand" ? "icon-btn-brand" : "icon-btn-quiet"}`}
    >
      <Icon name="plus" className="h-4 w-4" />
    </button>
  );
}

// Tüm oluşturma/düzenleme formlarının ortak kabuğu: koyulaştırılmış arka plan, Esc ile
// kapanma, açıkken sayfanın kaydırılmaması ve kapanınca odağın butona geri dönmesi.
export function Modal({
  open,
  title,
  description,
  onClose,
  children,
  size = "md",
}: {
  open: boolean;
  title: string;
  description?: string;
  onClose: () => void;
  children: ReactNode;
  size?: "sm" | "md" | "lg";
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!open) return;
    previouslyFocused.current = document.activeElement as HTMLElement | null;
    panelRef.current?.focus();
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") onClose();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.body.style.overflow = overflow;
      previouslyFocused.current?.focus();
    };
  }, [open, onClose]);

  if (!open) return null;

  const width = { sm: "max-w-md", md: "max-w-2xl", lg: "max-w-4xl" }[size];

  return (
    <div className="fixed inset-0 z-50 grid place-items-center p-3 sm:p-4">
      <button type="button" onClick={onClose} aria-label={`${title} penceresini kapat`} className="absolute inset-0 bg-[#2a1c14]/35 backdrop-blur-[2px]" />
      <div
        ref={panelRef}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`app-card relative z-10 flex max-h-[calc(100vh-1.5rem)] w-full ${width} flex-col overflow-hidden focus:outline-none`}
      >
        <div className="flex items-start justify-between gap-3 border-b border-[var(--line)] px-4 py-3.5 sm:px-5">
          <div className="min-w-0">
            <h2 className="text-title">{title}</h2>
            {description && <p className="text-meta mt-1">{description}</p>}
          </div>
          <button type="button" onClick={onClose} aria-label="Kapat" title="Kapat" className="icon-btn icon-btn-quiet shrink-0">
            <Icon name="close" className="h-4 w-4" />
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4 sm:px-5">{children}</div>
      </div>
    </div>
  );
}

// Modal içindeki formların alt şeridi - solda vazgeç, sağda asıl eylem.
export function FormActions({ onCancel, submitLabel, pending, pendingLabel, disabled = false }: { onCancel: () => void; submitLabel: string; pending?: boolean; pendingLabel?: string; disabled?: boolean }) {
  return (
    <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-3.5">
      <button type="button" onClick={onCancel} className="btn btn-quiet">Vazgeç</button>
      <button type="submit" disabled={pending || disabled} className="btn btn-primary">
        {pending ? (pendingLabel ?? "Kaydediliyor…") : submitLabel}
      </button>
    </div>
  );
}

// Form içindeki hata/başarı bildirimi - her ekranda aynı görünüm.
export function FormMessage({ tone, children }: { tone: "error" | "success"; children: ReactNode }) {
  const style = tone === "error"
    ? "bg-[var(--danger-soft)] text-[var(--danger-strong)]"
    : "bg-[var(--success-soft)] text-[var(--success-strong)]";
  return (
    <p role={tone === "error" ? "alert" : "status"} className={`rounded-xl px-3 py-2.5 text-xs font-semibold ${style}`}>
      {children}
    </p>
  );
}
