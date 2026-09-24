"use client";

// `<input type="month">` Safari masaüstünde DESTEKLENMİYOR: düz bir metin kutusuna düşüyor,
// yönetici "2026-09" biçimini elle yazmak zorunda kalıyordu (gerçek bir Safari hatası olarak
// bulundu). Bu bileşen aynı "YYYY-MM" değerini iki açılır menüyle (ay + yıl) üretir ve her
// tarayıcıda aynı görünür.
const MONTHS = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

export function MonthInput({
  value,
  onChange,
  label,
  className = "",
}: {
  value: string;
  onChange: (value: string) => void;
  // Ekran okuyucu için alan adı ("İlk dönem" gibi) - iki menüye ayrı ayrı eklenir.
  label: string;
  className?: string;
}) {
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  const now = new Date();
  const year = match ? Number(match[1]) : now.getFullYear();
  const month = match ? Number(match[2]) : now.getMonth() + 1;
  const years = Array.from(new Set([...Array.from({ length: 6 }, (_, index) => now.getFullYear() - 3 + index), year])).sort((a, b) => a - b);

  function emit(nextYear: number, nextMonth: number) {
    onChange(`${nextYear}-${String(nextMonth).padStart(2, "0")}`);
  }

  return (
    <span className={`mt-[.35rem] grid grid-cols-[minmax(0,1fr)_minmax(5.5rem,auto)] gap-2 ${className}`}>
      <select aria-label={`${label} - ay`} value={month} onChange={(event) => emit(year, Number(event.target.value))} className="field min-h-11 bg-white text-xs">
        {MONTHS.map((name, index) => <option key={name} value={index + 1}>{name}</option>)}
      </select>
      <select aria-label={`${label} - yıl`} value={year} onChange={(event) => emit(Number(event.target.value), month)} className="field min-h-11 bg-white text-xs tabular-nums">
        {years.map((item) => <option key={item} value={item}>{item}</option>)}
      </select>
    </span>
  );
}
