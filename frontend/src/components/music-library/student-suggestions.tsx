"use client";

import Link from "next/link";
import { Icon } from "@/components/icons";
import { errorMessage, useRemoveSuggestion, useStudentSuggestions } from "@/lib/library";

const dateFormat = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long" });

// Öğretmenin/yöneticinin kütüphaneden bu öğrenciye önerdiği eserler. Öğretmen yalnızca kendi
// öğrencisininkileri görür (API 403 döner); eser adı öneri anında satıra dondurulduğu için
// katalog değişse de liste okunur kalır.
export function StudentLibrarySuggestions({ studentId }: { studentId: string }) {
  const { data: suggestions, isLoading, isError } = useStudentSuggestions(studentId);
  const remove = useRemoveSuggestion(studentId);

  return (
    <section className="app-card p-4" aria-label="Önerilen eserler">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-title">Önerilen eserler</h2>
        <Link href="/dashboard/library" className="pressable inline-flex min-h-10 items-center gap-1 rounded-lg px-2 text-xs font-bold text-[var(--brand-strong)]">
          Kütüphane <Icon name="arrow-right" className="h-4 w-4" />
        </Link>
      </div>
      {isLoading && <div className="skeleton mt-3 h-16 rounded-xl" />}
      {isError && <p className="text-meta mt-2">Öneriler yüklenemedi.</p>}
      {!isLoading && !isError && !suggestions?.length && (
        <p className="text-meta mt-2">Henüz önerilen eser yok. Kütüphanede bir eseri açıp &quot;Öğrenciye öner&quot; ile ekleyebilirsin.</p>
      )}
      {!!suggestions?.length && (
        <ul className="mt-2 divide-y divide-[var(--line)]">
          {suggestions.map((suggestion) => (
            <li key={suggestion.id} className="flex items-start gap-3 py-2.5">
              <span className="mt-0.5 grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name="music" className="h-4 w-4" /></span>
              <div className="min-w-0 flex-1">
                <Link href={`/dashboard/library?eser=${encodeURIComponent(suggestion.entryId)}`} className="block truncate text-sm font-bold hover:underline">{suggestion.title}</Link>
                {suggestion.composer && <p className="text-meta truncate">{suggestion.composer}</p>}
                {suggestion.note && <p className="mt-1 text-xs text-[var(--foreground)]">{suggestion.note}</p>}
                <p className="mt-1 text-[.6875rem] text-[var(--muted)]">{suggestion.suggestedByName} · {dateFormat.format(new Date(suggestion.createdAt))}</p>
              </div>
              {suggestion.canRemove && (
                <button
                  type="button"
                  onClick={() => remove.mutate(suggestion.id)}
                  disabled={remove.isPending}
                  aria-label={`${suggestion.title} önerisini kaldır`}
                  className="pressable grid h-10 w-10 shrink-0 place-items-center rounded-lg text-[var(--muted)] hover:text-[var(--danger-strong)]"
                >
                  <Icon name="close" className="h-4 w-4" />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
      {remove.isError && <p className="mt-2 text-xs text-[var(--danger-strong)]" role="alert">{errorMessage(remove.error, "Öneri kaldırılamadı.")}</p>}
    </section>
  );
}
