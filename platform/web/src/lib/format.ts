// Tarih/sayı biçimleri - panel Türkçe, saat dilimi tarayıcınınki (operatör tek kişidir).
const dateTime = new Intl.DateTimeFormat("tr-TR", { dateStyle: "medium", timeStyle: "short" });
const dateOnly = new Intl.DateTimeFormat("tr-TR", { dateStyle: "medium" });
const number = new Intl.NumberFormat("tr-TR");

export function formatDateTime(value: string | null | undefined): string {
  return value ? dateTime.format(new Date(value)) : "—";
}

export function formatDate(value: string | null | undefined): string {
  return value ? dateOnly.format(new Date(value)) : "—";
}

// Dar alanlar (istatistik şeridi) için: "14.11.2026".
const dateShort = new Intl.DateTimeFormat("tr-TR", { dateStyle: "short" });
export function formatShortDate(value: string | null | undefined): string {
  return value ? dateShort.format(new Date(value)) : "—";
}

export function formatNumber(value: number | null | undefined): string {
  return value === null || value === undefined ? "—" : number.format(value);
}

// Para: "4.500,00 ₺". Tutar sunucudan gelir (decimal); burada yalnızca gösterim.
export function formatMoney(value: number | null | undefined, currency = "TRY"): string {
  if (value === null || value === undefined) return "—";
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, minimumFractionDigits: 2 }).format(value);
}

export function formatBytes(value: number | null | undefined): string {
  if (value === null || value === undefined) return "—";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let size = value;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit += 1;
  }
  return `${size.toLocaleString("tr-TR", { maximumFractionDigits: unit === 0 ? 0 : 1 })} ${units[unit]}`;
}

// Bir zamanın şimdiden ne kadar önce olduğu: "3 sa önce", "2 gün önce".
export function hoursSince(value: string | null | undefined, now = Date.now()): number | null {
  return value ? (now - new Date(value).getTime()) / 3_600_000 : null;
}

export function formatAge(value: string | null | undefined, now = Date.now()): string {
  const hours = hoursSince(value, now);
  if (hours === null) return "hiç";
  if (hours < 1 / 60) return "az önce";
  if (hours < 1) return `${Math.round(hours * 60)} dk önce`;
  if (hours < 48) return `${Math.round(hours)} sa önce`;
  return `${Math.round(hours / 24)} gün önce`;
}

export function formatDuration(start: string, end: string | null | undefined, now = Date.now()): string {
  const ms = (end ? new Date(end).getTime() : now) - new Date(start).getTime();
  if (ms < 0) return "—";
  const seconds = Math.round(ms / 1000);
  if (seconds < 60) return `${seconds} sn`;
  const minutes = Math.floor(seconds / 60);
  return `${minutes} dk ${seconds % 60} sn`;
}

// Okul adından baş harfler (kutucuk rozeti): "Do Re Mi Akademi" → "DR".
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  const letters = parts.length >= 2 ? parts[0][0] + parts[1][0] : name.trim().slice(0, 2);
  return letters.toLocaleUpperCase("tr-TR");
}

// Ana renk beyaz metinle okunur mu (WCAG göreli parlaklık). Okunmuyorsa koyu metin seçilir.
export function readableTextOn(hex: string | null | undefined): string {
  const match = /^#?([0-9a-f]{6})$/i.exec(hex ?? "");
  if (!match) return "#ffffff";
  const value = parseInt(match[1], 16);
  const channel = (shift: number) => {
    const c = ((value >> shift) & 0xff) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  const luminance = 0.2126 * channel(16) + 0.7152 * channel(8) + 0.0722 * channel(0);
  return 1.05 / (luminance + 0.05) >= 4.5 ? "#ffffff" : "#2a1c14";
}
