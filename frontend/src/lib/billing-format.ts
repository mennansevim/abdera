// Aidat ekranlarının ortak biçimlendirme yardımcıları. Daha önce aynı üç fonksiyonun
// (dönem doğrulama, dönem adı, para) birbirinden farklı kopyaları dues-list-section ve
// student-billing-section içinde yan yana duruyordu - biri yıl aralığını kontrol ediyor,
// diğeri etmiyordu. Tek kopya tutuluyor ki "Eylül 2026" her ekranda aynı yazılsın.

// "2026-09" biçimi. Yıl aralığı da kontrol edilir: <input type="month"> boş bırakıldığında
// veya elle yazıldığında "0000-09" gibi bir değer gelebiliyor.
export function isValidPeriod(period: string | null | undefined): period is string {
  if (!period || !/^\d{4}-(0[1-9]|1[0-2])$/.test(period)) return false;
  const [year] = period.split("-").map(Number);
  return year >= 1900 && year <= 9999;
}

export function isValidDateInput(value: string | null | undefined): value is string {
  return !!value && /^\d{4}-(0[1-9]|1[0-2])-([0-2]\d|3[01])$/.test(value) && Number.isFinite(new Date(`${value}T00:00:00`).getTime());
}

export function formatPeriod(period: string | null | undefined) {
  if (!isValidPeriod(period)) return "Dönem seçilmedi";
  return new Date(`${period}-01T00:00:00`).toLocaleDateString("tr-TR", { month: "long", year: "numeric" });
}

export function formatMoney(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
}

export function formatDay(isoDate: string) {
  return new Date(`${isoDate}T00:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long" });
}

// "2026-09" + 3 -> ["2026-09", "2026-10", "2026-11"]. Yalnızca hangi ayların kapsandığını
// EKRANDA göstermek için; tutar hesabı her zaman sunucudadır (tek kaynak TuitionCalculator).
export function periodSequence(startPeriod: string, months: number) {
  if (!isValidPeriod(startPeriod)) return [];
  const [year, month] = startPeriod.split("-").map(Number);
  return Array.from({ length: Math.max(0, months) }, (_, index) => {
    const date = new Date(Date.UTC(year, month - 1 + index, 1));
    return `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, "0")}`;
  });
}

export function todayInput() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
}

export function currentPeriod() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`;
}

// Kullanıcı kuralı: kayıt olan her öğrenci kayıt ayından itibaren her ayın aidatından
// sorumludur - "aidat açılmadı" diye bir durum yoktur, ay ya ödenmiştir ya ödenmemiştir.
// Kayıttan önceki aylar (ve biten kaydın bitişinden sonrası) sorumluluk dışıdır.
export function isObligatedPeriod(period: string, enrollment: { startedAt: string | null; endedAt?: string | null }) {
  if (!isValidPeriod(period) || !enrollment.startedAt) return false;
  if (period < enrollment.startedAt.slice(0, 7)) return false;
  return !enrollment.endedAt || period <= enrollment.endedAt.slice(0, 7);
}

// Kayıt ayından itibaren taahhüt edilen 12 ayın sonuncusu ("önündeki 12 ay").
export function commitmentEndPeriod(startedAt: string) {
  const [year, month] = startedAt.slice(0, 7).split("-").map(Number);
  const end = new Date(year, month - 1 + 11, 1);
  return `${end.getFullYear()}-${String(end.getMonth() + 1).padStart(2, "0")}`;
}
