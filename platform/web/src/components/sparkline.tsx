// Küçük çizgi grafik (sunucu ölçüm eğrisi). Kütüphane yok: tek seri, eksensiz, en son değer yanında yazılır.
// Renk temadan gelir (currentColor), boş değerler çizgiyi böler.
export function Sparkline({ values, max, label, className = "" }: { values: (number | null)[]; max?: number; label: string; className?: string }) {
  const width = 240;
  const height = 48;
  const numbers = values.filter((value): value is number => value !== null);
  if (numbers.length < 2) return <div className={`text-meta grid h-12 place-items-center ${className}`}>Henüz yeterli ölçüm yok</div>;
  const top = Math.max(max ?? 0, ...numbers) || 1;
  const step = width / (values.length - 1);
  const segments: string[] = [];
  let current = "";
  values.forEach((value, index) => {
    if (value === null) {
      if (current) segments.push(current);
      current = "";
      return;
    }
    const x = (index * step).toFixed(1);
    const y = (height - 2 - ((height - 4) * value) / top).toFixed(1);
    current += `${current ? "L" : "M"}${x},${y}`;
  });
  if (current) segments.push(current);
  return (
    <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" className={`h-12 w-full ${className}`} role="img" aria-label={label}>
      <line x1="0" y1={height - 1} x2={width} y2={height - 1} stroke="var(--line)" strokeWidth="1" />
      {segments.map((d) => <path key={d} d={d} fill="none" stroke="currentColor" strokeWidth="1.8" vectorEffect="non-scaling-stroke" strokeLinejoin="round" />)}
    </svg>
  );
}
