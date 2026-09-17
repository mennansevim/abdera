"use client";

import { useState } from "react";
import { ApiError } from "@/lib/api";
import { useCreateTeacherAvailability, useDeleteTeacherAvailability, useTeacherAvailability, type TeacherAvailability } from "@/lib/scheduling";

// Takvimin/telafi asistanının kullandığı gün sırası ve TR etiketleri (calendar/page.tsx ile
// aynı) - Pazartesi'den başlar, backend'in DayOfWeek string'leriyle (Sunday/Monday/...) eşleşir.
const AVAILABILITY_DAYS: Array<{ key: string; label: string }> = [
  { key: "Monday", label: "Pzt" },
  { key: "Tuesday", label: "Sal" },
  { key: "Wednesday", label: "Çar" },
  { key: "Thursday", label: "Per" },
  { key: "Friday", label: "Cum" },
  { key: "Saturday", label: "Cmt" },
  { key: "Sunday", label: "Paz" },
];
// Okulun varsayılan çalışma penceresi - takvim ızgarasının da varsayılanı (week-grid-layout.ts
// DEFAULT_START_HOUR/END_HOUR). Bir gün "açılırken" bu aralık kullanılır.
const DEFAULT_AVAILABILITY_START = "09:00";
const DEFAULT_AVAILABILITY_END = "19:00";

// "Öğretmeni tıklayınca açılan sekme içinde uygun günler yeşil olsun, tek tıkla seçilebilsin"
// - telafi/akıllı zamanlama önerileri (lib/smart-scheduling.ts) bu günleri kullanır: bir gün
// için hiç kayıt yoksa öğretmen o gün açık sayılır (varsayılan), ama en az bir gün
// işaretlenince yalnızca işaretli günler açık kalır - bu yüzden burada seçilen günler kadar
// önemli olan, HİÇBİR gün seçilmemiş olma durumunun ne anlama geldiğini de göstermek.
//
// İki yerden kullanılır: Öğretmenler ekranı (yönetici, herhangi bir öğretmen için) ve Ayarlar
// ekranı (öğretmen, yalnızca kendisi için - docs/10-decisions.md K1). Yetki sınırını backend
// zorlar (SchedulingAuthorization), buradaki `self` yalnızca metni doğru kişiye göre yazar.
export function TeacherAvailabilityDays({ teacherId, enabled = true, self = false }: { teacherId: string; enabled?: boolean; self?: boolean }) {
  const { data: availability, isLoading } = useTeacherAvailability(teacherId, { enabled });
  const createAvailability = useCreateTeacherAvailability(teacherId);
  const deleteAvailability = useDeleteTeacherAvailability(teacherId);
  const [pendingDay, setPendingDay] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const rowsByDay = new Map<string, TeacherAvailability[]>();
  for (const row of availability ?? []) {
    rowsByDay.set(row.dayOfWeek, [...(rowsByDay.get(row.dayOfWeek) ?? []), row]);
  }

  async function toggleDay(day: string) {
    setError(null);
    setPendingDay(day);
    try {
      const existing = rowsByDay.get(day) ?? [];
      if (existing.length) {
        // Normalde günde tek bir pencere olur; birden fazlaysa (elle/eski veriden) hepsini
        // kaldır - arayüz bir günü "açık/kapalı" olarak modelliyor, birden fazla aralık değil.
        await Promise.all(existing.map((row) => deleteAvailability.mutateAsync(row.id)));
      } else {
        await createAvailability.mutateAsync({ dayOfWeek: day, startTime: DEFAULT_AVAILABILITY_START, endTime: DEFAULT_AVAILABILITY_END });
      }
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Uygunluk güncellenemedi.");
    } finally {
      setPendingDay(null);
    }
  }

  return (
    <div className={self ? "" : "mt-3 border-t border-[var(--line)] pt-3"}>
      {!self && <p className="text-meta font-bold">Uygun günler</p>}
      {isLoading ? (
        <div className="mt-2 flex gap-1.5">{AVAILABILITY_DAYS.map((day) => <div key={day.key} className="skeleton h-9 w-14 rounded-lg" />)}</div>
      ) : (
        <div className="mt-2 flex flex-wrap gap-1.5" role="group" aria-label="Uygun günler">
          {AVAILABILITY_DAYS.map((day) => {
            const active = rowsByDay.has(day.key);
            const busy = pendingDay === day.key;
            return (
              <button
                key={day.key}
                type="button"
                onClick={() => void toggleDay(day.key)}
                disabled={busy}
                aria-pressed={active}
                title={active ? `${day.label}: uygun (${DEFAULT_AVAILABILITY_START}–${DEFAULT_AVAILABILITY_END}) - kapatmak için tıkla` : `${day.label}: uygun değil - açmak için tıkla`}
                className={`pressable min-h-9 w-14 rounded-lg border text-xs font-bold disabled:opacity-50 ${active ? "border-[var(--success-strong)] bg-[var(--success-soft)] text-[var(--success-strong)]" : "border-[var(--line)] bg-white text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]"}`}
              >
                {day.label}
              </button>
            );
          })}
        </div>
      )}
      <p className="text-meta mt-2">
        {!isLoading && !rowsByDay.size
          ? self
            ? "Hiçbir gün seçilmedi - şu an her gün uygun sayılıyorsun."
            : "Hiçbir gün seçilmedi - öğretmen şu an her gün uygun sayılıyor."
          : "Telafi ve uygun slot önerileri bu günleri kullanır."}
      </p>
      {error && <p role="alert" className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
    </div>
  );
}
