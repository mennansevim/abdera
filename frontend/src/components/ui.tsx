"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
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
    <header className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
      <div className="min-w-0">
        <h1 className="text-display font-serif leading-tight">{title}</h1>
        {description && <p className="text-meta mt-0.5">{description}</p>}
      </div>
      {actions && <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto sm:shrink-0">{actions}</div>}
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

// Kompakt ekran yapı taşları (2026-09 sadeleştirmesi, ilk örnek: Yoklama ekranı). Ortak kalıp:
// filtreler tek kartta tek satır, özet sayılar tek şeritte (tıklanabilirse filtre olur),
// liste başlık şeritli tek kartta yoğun satırlar. Yeni ekran kendi kart/sayaç iskeletini kurmaz.
export type Tone = "brand" | "success" | "danger" | "warning" | "muted";

export const TONE_TEXT: Record<Tone, string> = {
  brand: "text-[var(--brand-strong)]",
  success: "text-[var(--success-strong)]",
  danger: "text-[var(--danger-strong)]",
  warning: "text-[var(--warning-strong)]",
  muted: "text-[var(--muted)]",
};

export const TONE_BADGE: Record<Tone, string> = {
  brand: "bg-[var(--brand-soft)] text-[var(--brand-strong)]",
  success: "bg-[var(--success-soft)] text-[var(--success-strong)]",
  danger: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  warning: "bg-[var(--warning-soft)] text-[var(--warning-strong)]",
  muted: "bg-[var(--surface-muted)] text-[var(--muted)]",
};

// Başlık şeritli kart: solda başlık, yanında sakin bir sayaç/açıklama, sağda eylemler.
// `flush` gövdeyi dolgusuz bırakır (tablo/liste kenara dayansın diye).
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

// Filtre çubuğundaki tek alan: küçük büyük harfli etiket + altında denetim.
export function FilterField({ label, className = "", children }: { label: string; className?: string; children: ReactNode }) {
  return (
    <label className={`block min-w-0 ${className}`}>
      <span className="text-micro text-[var(--muted)]">{label}</span>
      <div className="mt-1">{children}</div>
    </label>
  );
}

// Birkaç seçenekten biri: "7 gün / 30 gün", "Liste / Izgara" gibi. Seçili olan beyaz zeminle öne çıkar.
export function Segmented<T extends string>({ label, options, value, onChange, className = "" }: {
  label: string; options: { value: T; label: ReactNode; icon?: IconName }[]; value: T | undefined; onChange: (value: T) => void; className?: string;
}) {
  return (
    <div role="group" aria-label={label} className={`inline-flex max-w-full overflow-x-auto rounded-xl border border-[var(--line)] bg-[var(--surface-muted)] p-0.5 ${className}`}>
      {options.map((option) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            aria-pressed={active}
            onClick={() => onChange(option.value)}
            className={`pressable inline-flex min-h-9 pointer-coarse:min-h-11 shrink-0 items-center gap-1.5 rounded-[.6rem] px-2.5 text-xs font-bold whitespace-nowrap ${active ? "bg-white text-[var(--brand-strong)] shadow-sm" : "text-[var(--muted)] hover:text-[var(--foreground)]"}`}
          >
            {option.icon && <Icon name={option.icon} className="h-3.5 w-3.5 shrink-0" />}
            {option.label}
          </button>
        );
      })}
    </div>
  );
}

// Özet sayılar tek şeritte. `onClick` verilen öğe buton olur (sayaç = filtre).
export interface StatItem {
  key: string;
  label: string;
  value: ReactNode;
  tone?: Tone;
  hint?: ReactNode;
  active?: boolean;
  loading?: boolean;
  onClick?: () => void;
  href?: string;
}

export function StatStrip({ label, items, footer, className = "" }: { label: string; items: StatItem[]; footer?: ReactNode; className?: string }) {
  const cols = { 2: "sm:grid-cols-2", 3: "sm:grid-cols-3", 4: "sm:grid-cols-4", 5: "sm:grid-cols-5", 6: "sm:grid-cols-3 lg:grid-cols-6" }[items.length] ?? "sm:grid-cols-4";
  return (
    <section aria-label={label} className={`app-card overflow-hidden ${className}`}>
      <div className={`grid grid-cols-2 divide-[var(--line)] sm:divide-x ${cols}`}>
        {items.map((item) => {
          const body = (
            <>
              {item.active && <span aria-hidden className="absolute inset-x-0 top-0 h-0.5 bg-[var(--brand)]" />}
              <span className="block min-w-0">
                <span className={`block truncate text-xs font-bold ${item.active ? "text-[var(--brand-strong)]" : "text-[var(--muted)]"}`}>{item.label}</span>
                {item.loading
                  ? <span className="skeleton mt-1 block h-6 w-16 rounded-md" />
                  : <span className={`block truncate text-lg font-bold tabular-nums tracking-[-.01em] ${item.tone ? TONE_TEXT[item.tone] : ""}`}>{item.value}</span>}
                {item.hint && <span className="text-meta block truncate text-[.72rem]">{item.hint}</span>}
              </span>
            </>
          );
          const base = `relative min-w-0 px-4 py-2.5 text-left ${item.active ? "bg-[var(--brand-soft)]" : ""}`;
          if (item.href) return <Link key={item.key} href={item.href} className={`pressable ${base} hover:bg-[var(--surface-muted)]`}>{body}</Link>;
          return item.onClick
            ? <button key={item.key} type="button" aria-pressed={Boolean(item.active)} onClick={item.onClick} className={`pressable ${base} hover:bg-[var(--surface-muted)]`}>{body}</button>
            : <div key={item.key} className={base}>{body}</div>;
        })}
      </div>
      {footer}
    </section>
  );
}

// Küçük durum rozeti - listelerde tek tip.
export function Badge({ tone = "muted", children, className = "" }: { tone?: Tone; children: ReactNode; className?: string }) {
  return <span className={`inline-flex shrink-0 items-center rounded-full px-2 py-0.5 text-[.72rem] font-bold whitespace-nowrap ${TONE_BADGE[tone]} ${className}`}>{children}</span>;
}

// Liste/tablo boş durumu - kartın içinde sakin tek blok.
export function EmptyState({ icon = "check", title, description, action }: { icon?: IconName; title: string; description?: string; action?: ReactNode }) {
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

// Sayfalama şeridi - Panel footer'ında kullanılır.
export function Pager({ page, totalPages, onChange, summary }: { page: number; totalPages: number; onChange: (page: number) => void; summary?: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-2 text-sm">
      <span className="text-meta">{summary ?? <>Sayfa {page} / {totalPages}</>}</span>
      <div className="flex gap-2">
        <button type="button" onClick={() => onChange(Math.max(1, page - 1))} disabled={page <= 1} className="btn btn-quiet min-h-9 pointer-coarse:min-h-11 px-3 text-xs">Önceki</button>
        <button type="button" onClick={() => onChange(Math.min(totalPages, page + 1))} disabled={page >= totalPages} className="btn btn-quiet min-h-9 pointer-coarse:min-h-11 px-3 text-xs">Sonraki</button>
      </div>
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
    <label className="relative block min-w-0 flex-1 sm:w-56 sm:flex-none">
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

// Satır sonundaki "⋮" eylem menüsü. Bir satırda birden fazla eylem olduğunda hepsini yan
// yana buton olarak dizmek listeyi okunmaz yapıyor; ikincil eylemler buraya toplanır.
// Dışarı tıklama ve Esc ile kapanır, klavyeyle erişilebilir.
//
// Menü body'ye portal'lanır ve butona göre `fixed` konumlanır. Eskiden butonun yanında
// `absolute` duruyordu; `overflow-hidden` taşıyan bir kartın (Öğrenciler listesi, Panel)
// son satırında açılınca kartın alt kenarında kesiliyor, eylemlerin çoğu görünmüyordu.
// Altta yer yoksa yukarı doğru açılır.
export function RowMenu({ label, children }: { label: string; children: (close: () => void) => ReactNode }) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  useLayoutEffect(() => {
    if (!open) return;
    function place() {
      const button = containerRef.current?.getBoundingClientRect();
      const menu = menuRef.current;
      if (!button || !menu) return;
      const gap = 4;
      const margin = 8;
      const below = button.bottom + gap;
      const above = button.top - gap - menu.offsetHeight;
      const fitsBelow = below + menu.offsetHeight <= window.innerHeight - margin;
      menu.style.top = `${fitsBelow || above < margin ? below : above}px`;
      menu.style.right = `${Math.max(margin, document.documentElement.clientWidth - button.right)}px`;
      menu.style.visibility = "visible";
    }
    place();
    window.addEventListener("resize", place);
    // capture: sayfa değil iç içe bir kaydırma alanı (modal gövdesi) kaydığında da izle.
    window.addEventListener("scroll", place, true);
    return () => {
      window.removeEventListener("resize", place);
      window.removeEventListener("scroll", place, true);
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: MouseEvent) {
      const target = event.target as Node;
      if (!containerRef.current?.contains(target) && !menuRef.current?.contains(target)) setOpen(false);
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
        // Eskiden kenarlıksız ve şeffaftı; "…" satırın içinde kayboluyor, buton olduğu
        // anlaşılmıyordu (kullanıcı geri bildirimi). Yanındaki btn-quiet düğmelerle aynı çerçeve.
        className={`icon-btn icon-btn-quiet rounded-[1rem] text-[#5c4d3f] ${open ? "border-[var(--brand)] text-[var(--brand)]" : ""}`}
      >
        <Icon name="more" className="h-5 w-5" />
      </button>
      {open && createPortal(
        // z-[60]: Modal (z-50) içindeki bir satır menüsü de pencerenin üstünde kalsın.
        <div ref={menuRef} role="menu" style={{ visibility: "hidden" }} className="app-card fixed z-[60] w-52 overflow-hidden p-1">
          {children(() => setOpen(false))}
        </div>,
        document.body,
      )}
    </div>
  );
}

// RowMenu içinde yıkıcı eylemi (kalıcı silme) diğerlerinden ayıran çizgi.
export function RowMenuSeparator() {
  return <div role="separator" className="my-1 border-t border-[var(--line)]" />;
}

// RowMenu içindeki tek bir eylem satırı.
export function RowMenuItem({ onClick, icon, tone = "default", children }: { onClick: () => void; icon?: IconName; tone?: "default" | "danger"; children: ReactNode }) {
  return (
    <button
      type="button"
      role="menuitem"
      onClick={onClick}
      className={`pressable flex min-h-11 w-full items-center gap-2 rounded-lg px-2.5 text-left text-sm font-semibold ${
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
  // Çağıranlar onClose'u satır içi verir (`onClose={() => setOpen(false)}`). Efektin bağımlılığı
  // olsaydı üst bileşenin her yeniden çizimi - ör. takvimin 15 sn'lik yenilemesi - efekti
  // yeniden çalıştırıp paneli odaklıyordu: yazılan alandan odak kayıyor, mobilde klavye
  // kapanıyordu (gerçek bir hata olarak ölçüldü). En güncel onClose ref'ten okunur.
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  });

  useEffect(() => {
    if (!open) return;
    previouslyFocused.current = document.activeElement as HTMLElement | null;
    // İçerideki bir alan `autoFocus` ile odağı zaten almışsa (React bunu çocuklar mount olurken,
    // bu efektten önce yapar) paneli odaklayıp onu geri almayız.
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
      <button type="button" onClick={onClose} aria-label={`${title} penceresini kapat`} className="absolute inset-0 bg-[#2a1c14]/35 backdrop-blur-[2px]" />
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

// Modal içindeki formların alt şeridi - solda vazgeç, sağda asıl eylem.
export function FormActions({ onCancel, submitLabel, pending, pendingLabel, disabled = false }: { onCancel: () => void; submitLabel: string; pending?: boolean; pendingLabel?: string; disabled?: boolean }) {
  return (
    <div className="sticky bottom-[-1rem] z-10 -mx-4 flex justify-end gap-2 border-t border-[var(--line)] bg-[var(--surface)] px-4 pb-[max(1rem,env(safe-area-inset-bottom))] pt-3 sm:-mx-5 sm:px-5">
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
      {onDismiss && <button type="button" onClick={onDismiss} aria-label="Bildirimi kapat" className="pressable -my-2 -mr-2 grid h-11 w-11 shrink-0 place-items-center rounded-lg hover:bg-white/60"><Icon name="close" className="h-3.5 w-3.5" /></button>}
    </p>
  );
}

// Tarayıcının kendi doğrulama balonları (boş zorunlu alan, min/max) sistem diline göre
// çıkıyor - Türkçe bir arayüzde aniden "Please fill out this field" görünmesin diye
// `onInvalid`/`onChange` çiftiyle Türkçeleştirilir. `setCustomValidity` bir kez çağrılınca
// alan temizlenene kadar geçersiz kalmaya devam ettiği için `onChange`'in bunu da sıfırlaması
// gerekiyor - `resetValidity` var olan bir onChange handler'ını bu sıfırlamayla sarmalar.
type ValidatableElement = HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;

export function turkishValidityMessage(target: ValidatableElement): string {
  const { validity } = target;
  if (validity.valueMissing) return "Bu alan zorunlu.";
  if (validity.rangeOverflow) return `Değer en fazla ${(target as HTMLInputElement).max} olabilir.`;
  if (validity.rangeUnderflow) return `Değer en az ${(target as HTMLInputElement).min} olabilir.`;
  if (validity.typeMismatch) return "Geçerli bir değer gir.";
  return "Bu alan geçersiz.";
}

export function onInvalidTurkish(event: React.InvalidEvent<ValidatableElement>) {
  // React'te SyntheticEvent.target hep EventTarget'tır - doğru tipli olan currentTarget.
  event.currentTarget.setCustomValidity(turkishValidityMessage(event.currentTarget));
}

export function resetValidity<E extends { target: ValidatableElement }>(handler: (event: E) => void) {
  return (event: E) => {
    event.target.setCustomValidity("");
    handler(event);
  };
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
