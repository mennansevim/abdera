"use client";

import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { useMe } from "@/lib/use-auth";
import { Icon, type IconName } from "./icons";

// Yönetici-özel sayfaların (Aidatlar, Giderler, Banka, Mesaj Merkezi, Ders Talepleri,
// Yedekleme) ortak kapısı. Önceden bu sayfalar YALNIZCA kenar çubuğunda gizleniyordu ve
// altlarındaki API çağrıları Admin-only olduğu için veri sızmıyordu - ama bir öğretmen
// adresi doğrudan yazarsa (ör. /dashboard/costs) sayfanın kendisi (başlık, boş durumlar,
// bazı ekranlarda "şifreni doğrula" kutusu) yine de render ediliyordu. Kullanıcı isteği net:
// "masraflarla ilgili bir sayfayı ASLA görmemeli" - API 403'ü yeterli değil, sayfa hiç
// açılmamalı. `me` yüklenene kadar hiçbir şey göstermez (yanlış rolün anlık görünüp
// kaybolmasını engeller).
export function AdminGate({ children }: { children: ReactNode }) {
  const { data: me, isLoading } = useMe();
  const router = useRouter();
  const isAdmin = me?.role === "Admin";

  useEffect(() => {
    if (!isLoading && me && !isAdmin) {
      router.replace("/dashboard");
    }
  }, [isLoading, me, isAdmin, router]);

  if (isLoading || !me || !isAdmin) return null;
  return <>{children}</>;
}

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

// Liste ekranlarının arama kutusu. Uzun listelerde (öğrenci/öğretmen) kaydı gözle aramak
// yerine yazarak daraltmak için - ekranın kendi verisini filtreler, sunucuya istek atmaz.
export function SearchInput({ value, onChange, label, placeholder }: { value: string; onChange: (value: string) => void; label: string; placeholder?: string }) {
  return (
    <label className="relative block w-full sm:w-56">
      <span className="sr-only">{label}</span>
      <Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[var(--muted)]" />
      <input
        type="search"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder ?? label}
        className="field min-h-11 pl-9 text-sm"
      />
    </label>
  );
}

// Satır sonundaki "⋮" eylem menüsü. Bir satırda birden fazla eylem olduğunda hepsini yan
// yana buton olarak dizmek listeyi okunmaz yapıyor; ikincil eylemler buraya toplanır.
// Dışarı tıklama ve Esc ile kapanır, klavyeyle erişilebilir.
export function RowMenu({ label, children }: { label: string; children: (close: () => void) => ReactNode }) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: MouseEvent) {
      if (!containerRef.current?.contains(event.target as Node)) setOpen(false);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  return (
    <div ref={containerRef} className="relative shrink-0">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={label}
        title={label}
        className="icon-btn icon-btn-quiet h-9 w-9 border-transparent bg-transparent"
      >
        <Icon name="more" className="h-4 w-4" />
      </button>
      {open && (
        <div role="menu" className="app-card absolute right-0 top-[calc(100%+.25rem)] z-30 w-52 overflow-hidden p-1">
          {children(() => setOpen(false))}
        </div>
      )}
    </div>
  );
}

// RowMenu içindeki tek bir eylem satırı.
export function RowMenuItem({ onClick, icon, tone = "default", children }: { onClick: () => void; icon?: IconName; tone?: "default" | "danger"; children: ReactNode }) {
  return (
    <button
      type="button"
      role="menuitem"
      onClick={onClick}
      className={`pressable flex min-h-10 w-full items-center gap-2 rounded-lg px-2.5 text-left text-xs font-semibold ${
        tone === "danger"
          ? "text-[var(--danger-strong)] hover:bg-[var(--danger-soft)]"
          : "text-[var(--foreground)] hover:bg-[var(--surface-muted)]"
      }`}
    >
      {icon && <Icon name={icon} className="h-4 w-4 shrink-0 opacity-70" />}
      {children}
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

// Sayfa seviyesinde kısa süreli başarı bildirimi. Pencere kapandıktan sonra "oldu mu?"
// sorusunu bırakmamak için: form penceresi kapanırken sonucu buraya taşır.
export function Notice({ children, onDismiss }: { children: ReactNode; onDismiss?: () => void }) {
  return (
    <p role="status" className="flex items-center gap-2 rounded-xl border border-[color:var(--success-soft)] bg-[var(--success-soft)] px-3 py-2.5 text-xs font-semibold text-[var(--success-strong)]">
      <Icon name="check" className="h-4 w-4 shrink-0" />
      <span className="min-w-0 flex-1">{children}</span>
      {onDismiss && <button type="button" onClick={onDismiss} aria-label="Bildirimi kapat" className="pressable shrink-0 rounded-lg p-1 hover:bg-white/60"><Icon name="close" className="h-3.5 w-3.5" /></button>}
    </p>
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
