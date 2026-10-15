"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Icon, type IconName } from "./icons";

// Okul uygulamasının components/ui.tsx yapı taşlarından platformun kullandığı alt küme
// (aynı sınıflar, aynı yerleşim kuralları) + platforma özgü onay penceresi ve kopyalama.

export type Tone = "brand" | "success" | "danger" | "warning" | "info" | "muted";

export const TONE_TEXT: Record<Tone, string> = {
  brand: "text-[var(--brand-strong)]",
  success: "text-[var(--success-strong)]",
  danger: "text-[var(--danger-strong)]",
  warning: "text-[var(--warning-strong)]",
  info: "text-[var(--info-strong)]",
  muted: "text-[var(--muted)]",
};

export const TONE_BADGE: Record<Tone, string> = {
  brand: "bg-[var(--brand-soft)] text-[var(--brand-strong)]",
  success: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  danger: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  warning: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  info: "bg-[var(--info-soft)] text-[var(--info-strong)]",
  muted: "bg-[var(--surface-muted)] text-[var(--muted)]",
};

export function PageHeader({ title, description, actions }: { title: string; description?: ReactNode; actions?: ReactNode }) {
  return (
    <header className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
      <div className="min-w-0">
        <h1 className="text-display font-serif leading-tight">{title}</h1>
        {description && <p className="text-meta mt-0.5">{description}</p>}
      </div>
      {actions && <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto sm:shrink-0">{actions}</div>}
    </header>
  );
}

// Başlık şeritli kart. `flush` gövdeyi dolgusuz bırakır (tablo kenara dayansın diye).
export function Panel({ title, meta, actions, footer, flush = false, className = "", children }: {
  title?: ReactNode; meta?: ReactNode; actions?: ReactNode; footer?: ReactNode; flush?: boolean; className?: string; children?: ReactNode;
}) {
  return (
    <section className={`app-card min-w-0 overflow-hidden ${className}`}>
      {(title || actions) && (
        <div className="flex min-h-11 flex-wrap items-center justify-between gap-x-3 gap-y-1 border-b border-[var(--line)] px-4 py-2">
          <div className="flex min-w-0 items-baseline gap-2">
            {title && <h2 className="truncate text-sm font-bold">{title}</h2>}
            {meta && <span className="text-meta truncate">{meta}</span>}
          </div>
          {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
        </div>
      )}
      <div className={flush ? "" : "p-4"}>{children}</div>
      {footer && <div className="border-t border-[var(--line)] px-4 py-2">{footer}</div>}
    </section>
  );
}

export function Badge({ tone = "muted", children, className = "" }: { tone?: Tone; children: ReactNode; className?: string }) {
  return <span className={`inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-[.72rem] font-bold whitespace-nowrap ${TONE_BADGE[tone]} ${className}`}>{children}</span>;
}

export function EmptyState({ icon = "check", title, description, action }: { icon?: IconName; title: string; description?: ReactNode; action?: ReactNode }) {
  return (
    <div className="grid min-h-32 place-items-center px-6 py-8 text-center">
      <div>
        <span className="mx-auto grid h-10 w-10 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name={icon} className="h-5 w-5" /></span>
        <p className="mt-3 text-sm font-bold">{title}</p>
        {description && <p className="text-meta mt-1">{description}</p>}
        {action && <div className="mt-3">{action}</div>}
      </div>
    </div>
  );
}

// Özet sayılar tek şeritte (telemetri, kapasite).
export interface StatItem {
  key: string;
  label: string;
  value: ReactNode;
  tone?: Tone;
  hint?: ReactNode;
}

export function StatStrip({ label, items }: { label: string; items: StatItem[] }) {
  return (
    <section aria-label={label} className="app-card overflow-hidden">
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6">
        {items.map((item) => (
          <div key={item.key} className="min-w-0 border-b border-r border-[var(--line)] px-4 py-2.5">
            <span className="block text-xs font-bold leading-snug text-[var(--muted)]">{item.label}</span>
            <span className={`block truncate text-lg font-bold tabular-nums tracking-[-.01em] ${item.tone ? TONE_TEXT[item.tone] : ""}`}>{item.value}</span>
            {item.hint && <span className="text-meta block text-[.72rem] leading-snug">{item.hint}</span>}
          </div>
        ))}
      </div>
    </section>
  );
}

// Birkaç seçenekten biri: "Açık / Tümü" gibi.
export function Segmented<T extends string>({ label, options, value, onChange }: {
  label: string; options: { value: T; label: ReactNode }[]; value: T; onChange: (value: T) => void;
}) {
  return (
    <div role="group" aria-label={label} className="inline-flex max-w-full overflow-x-auto rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] p-0.5">
      {options.map((option) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            aria-pressed={active}
            onClick={() => onChange(option.value)}
            className={`pressable inline-flex min-h-9 pointer-coarse:min-h-11 shrink-0 items-center gap-1.5 rounded-[.6rem] px-2.5 text-xs font-bold whitespace-nowrap ${active ? "bg-[var(--surface)] text-[var(--brand-strong)] shadow-sm" : "text-[var(--muted)] hover:text-[var(--foreground)]"}`}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}

export function SearchInput({ value, onChange, label, placeholder }: { value: string; onChange: (value: string) => void; label: string; placeholder?: string }) {
  return (
    <label className="relative block min-w-0 flex-1 sm:w-64 sm:flex-none">
      <span className="sr-only">{label}</span>
      <Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--muted)]" />
      <input
        type="search"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder ?? label}
        className="field pl-9 text-sm"
      />
    </label>
  );
}

// Ortak pencere kabuğu: Esc ile kapanma, açıkken sayfa kaymaz, kapanınca odak geri döner.
export function Modal({ open, title, description, onClose, children, size = "md" }: {
  open: boolean; title: string; description?: ReactNode; onClose: () => void; children: ReactNode; size?: "sm" | "md" | "lg";
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);
  // onClose satır içi verilir; efektin bağımlılığı olsaydı her çizimde odak kayardı.
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  });

  useEffect(() => {
    if (!open) return;
    previouslyFocused.current = document.activeElement as HTMLElement | null;
    if (!panelRef.current?.contains(document.activeElement)) panelRef.current?.focus();
    const { overflow } = document.body.style;
    document.body.style.overflow = "hidden";
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") onCloseRef.current();
    }
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.body.style.overflow = overflow;
      previouslyFocused.current?.focus();
    };
  }, [open]);

  if (!open || typeof document === "undefined") return null;

  const width = { sm: "max-w-md", md: "max-w-2xl", lg: "max-w-4xl" }[size];

  return createPortal(
    <div className="fixed inset-0 z-50 grid place-items-center p-3 sm:p-4">
      <button type="button" onClick={onClose} aria-label={`${title} penceresini kapat`} className="absolute inset-0 bg-[var(--overlay)] backdrop-blur-[2px]" />
      <div
        ref={panelRef}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={`app-card relative z-10 flex max-h-[calc(100dvh-1.5rem)] w-full ${width} flex-col overflow-hidden focus:outline-none`}
      >
        <div className="flex items-start justify-between gap-3 border-b border-[var(--line)] px-4 py-4 sm:px-5">
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
    </div>,
    document.body,
  );
}

// Onay penceresi. `confirmText` verilirse kullanıcı o metni birebir yazmadan eylem açılmaz
// (okul silme: kısaltmayı yazarak onay, §4.3 "Tehlikeli bölge").
export function ConfirmDialog({ open, title, description, confirmLabel, tone = "primary", confirmText, pending, error, onConfirm, onClose, children }: {
  open: boolean;
  title: string;
  description?: ReactNode;
  confirmLabel: string;
  tone?: "primary" | "danger";
  confirmText?: string;
  pending?: boolean;
  error?: string | null;
  onConfirm: () => void;
  onClose: () => void;
  children?: ReactNode;
}) {
  const [typed, setTyped] = useState("");
  const blocked = confirmText !== undefined && typed.trim() !== confirmText;

  function close() {
    setTyped("");
    onClose();
  }

  return (
    <Modal open={open} title={title} description={description} onClose={close} size="sm">
      <form
        className="grid gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          if (!blocked) onConfirm();
        }}
      >
        {children}
        {confirmText !== undefined && (
          <label className="form-label">
            Onaylamak için <span className="font-mono text-[var(--foreground)]">{confirmText}</span> yaz
            <input className="field font-mono" value={typed} onChange={(event) => setTyped(event.target.value)} autoComplete="off" autoFocus />
          </label>
        )}
        {error && <FormMessage tone="error">{error}</FormMessage>}
        <div className="flex justify-end gap-2 pt-1">
          <button type="button" onClick={close} className="btn btn-quiet">Vazgeç</button>
          <button type="submit" disabled={blocked || pending} className={`btn ${tone === "danger" ? "btn-danger" : "btn-primary"}`}>
            {pending ? "Gönderiliyor…" : confirmLabel}
          </button>
        </div>
      </form>
    </Modal>
  );
}

export function FormMessage({ tone, children }: { tone: "error" | "success" | "info"; children: ReactNode }) {
  const style = {
    error: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
    success: "bg-[var(--success-soft)] text-[var(--success-strong)]",
    info: "bg-[var(--info-soft)] text-[var(--info-strong)]",
  }[tone];
  return (
    <p role={tone === "error" ? "alert" : "status"} className={`rounded-xl px-3 py-2.5 text-xs font-semibold ${style}`}>
      {children}
    </p>
  );
}

// Sayfa seviyesinde kısa süreli başarı bildirimi.
export function Notice({ children, onDismiss }: { children: ReactNode; onDismiss?: () => void }) {
  return (
    <p role="status" className="flex items-center gap-2 rounded-xl bg-[var(--success-soft)] px-3 py-2.5 text-xs font-semibold text-[var(--success-strong)]">
      <Icon name="check" className="h-4 w-4 shrink-0" />
      <span className="min-w-0 flex-1">{children}</span>
      {onDismiss && <button type="button" onClick={onDismiss} aria-label="Bildirimi kapat" className="pressable -my-2 -mr-2 grid h-9 w-9 shrink-0 place-items-center rounded-lg"><Icon name="close" className="h-3.5 w-3.5" /></button>}
    </p>
  );
}

// Uzun metni (sır, URI, parmak izi) kopyalama düğmesiyle gösterir.
export function CopyField({ label, value, mono = true }: { label: string; value: string; mono?: boolean }) {
  const [copied, setCopied] = useState(false);
  async function copy() {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      setCopied(false);
    }
  }
  return (
    <div className="min-w-0">
      <span className="form-label">{label}</span>
      <div className="mt-1 flex min-w-0 items-stretch gap-2">
        <code className={`min-w-0 flex-1 break-all rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] px-3 py-2 text-xs ${mono ? "font-mono" : ""}`}>{value}</code>
        <button type="button" onClick={copy} className="btn btn-quiet shrink-0" aria-label={`${label} kopyala`}>
          <Icon name={copied ? "check" : "copy"} className="h-4 w-4" />
          {copied ? "Kopyalandı" : "Kopyala"}
        </button>
      </div>
    </div>
  );
}

export function ProgressBar({ value, max, tone = "brand", label }: { value: number; max: number; tone?: "brand" | "danger" | "warning" | "success"; label: string }) {
  const percent = max > 0 ? Math.min(100, Math.max(0, (value / max) * 100)) : 0;
  const color = { brand: "var(--brand)", danger: "var(--danger)", warning: "var(--warning)", success: "var(--success)" }[tone];
  return (
    <div className="progress-track" role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={max} aria-valuenow={value}>
      <div className="progress-fill" style={{ width: `${percent}%`, background: color }} />
    </div>
  );
}

export function Skeleton({ className = "" }: { className?: string }) {
  return <div className={`skeleton rounded-2xl ${className}`} aria-hidden="true" />;
}

// Yükleme hatası kartı: sorgu düşerse ekran boş kalmasın, ne olduğu ve "yeniden dene" görünsün.
export function LoadError({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="app-card flex flex-wrap items-center justify-between gap-3 p-4">
      <p className="flex min-w-0 items-center gap-2 text-sm font-semibold text-[var(--danger-strong)]">
        <Icon name="alert-triangle" className="h-4 w-4 shrink-0" />
        <span className="min-w-0">{message}</span>
      </p>
      {onRetry && <button type="button" onClick={onRetry} className="btn btn-quiet">Yeniden dene</button>}
    </div>
  );
}

// Açma/kapama anahtarı (modül kartları, kanarya/sabitleme).
export function Toggle({ checked, onChange, disabled, label }: { checked: boolean; onChange: (value: boolean) => void; disabled?: boolean; label: string }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={`relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${checked ? "bg-[var(--brand-strong)]" : "bg-[var(--line-strong)]"}`}
    >
      <span className={`inline-block h-5 w-5 rounded-full bg-white shadow transition-transform ${checked ? "translate-x-5" : "translate-x-0.5"}`} />
    </button>
  );
}
