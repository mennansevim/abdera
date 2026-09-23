"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { AddButton, FormActions, FormMessage, Modal, PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/use-auth";
import { SHOW_STATUS_LABEL, useCreateShow, useShows, type ShowStatus } from "@/lib/shows";

// Gösteri listesi. AdminGate YOK - kulisteki öğretmenin kendi öğrencisinin kaçıncı sırada
// olduğunu görebilmesi gerekiyor. Düzenleme eylemleri role göre gizlenir; sunucu zaten
// Admin-only (Shows.cs).

const STATUS_TONES: Record<ShowStatus, string> = {
  Draft: "bg-[var(--surface-muted)] text-[var(--muted)]",
  Live: "bg-[var(--danger-soft)] text-[var(--danger-strong)]",
  Completed: "bg-[var(--success-soft)] text-[var(--success-strong)]",
};

function formatDateTime(value: string) {
  return new Date(value).toLocaleString("tr-TR", {
    day: "numeric", month: "long", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

function formatDuration(minutes: number) {
  if (minutes <= 0) return "süre girilmedi";
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours ? `${hours} sa ${rest} dk` : `${rest} dk`;
}

export default function ShowsPage() {
  const { data: me } = useMe();
  const { data: shows, isLoading, isError } = useShows();
  const [showCreate, setShowCreate] = useState(false);
  const isAdmin = me?.role === "Admin";

  return (
    <div className="space-y-4">
      <PageHeader
        title="Yıl sonu gösterisi"
        description="Programı sıraya diz, gösteri gecesi sahne ekranından tek tuşla ilerlet."
        actions={isAdmin ? <AddButton label="Gösteri oluştur" onClick={() => setShowCreate(true)} /> : undefined}
      />

      {isLoading && <div className="space-y-3">{[1, 2].map((item) => <div key={item} className="skeleton h-28 rounded-2xl" />)}</div>}

      {isError && <FormMessage tone="error">Gösteriler yüklenemedi. Bağlantınızı kontrol edip sayfayı yenileyin.</FormMessage>}

      {!isLoading && shows?.length === 0 && (
        <div className="app-card grid min-h-56 place-items-center p-8 text-center">
          <div>
            <span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand-strong)]">
              <Icon name="music" className="h-5 w-5" />
            </span>
            <p className="mt-4 text-sm font-bold">Henüz gösteri yok</p>
            <p className="text-meta mt-1 max-w-md">
              Bir gösteri oluştur, öğrencileri sırayla programa ekle. Gösteri gecesi sahne ekranı
              çalınan eseri büyük punto ile gösterir, sıradaki öğrenciyi kulise bildirir.
            </p>
            {isAdmin && <button type="button" onClick={() => setShowCreate(true)} className="btn btn-primary mt-4">Gösteri oluştur</button>}
          </div>
        </div>
      )}

      <div className="grid gap-3 lg:grid-cols-2">
        {shows?.map((show) => (
          <article key={show.id} className="app-card overflow-hidden">
            <div className="flex flex-wrap items-start justify-between gap-3 p-4">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="text-title truncate">{show.title}</h2>
                  <span className={`rounded-full px-2 py-0.5 text-[.75rem] font-bold ${STATUS_TONES[show.status]}`}>
                    {show.status === "Live" && <span className="mr-1 inline-block h-1.5 w-1.5 animate-pulse rounded-full bg-current align-middle" />}
                    {SHOW_STATUS_LABEL[show.status]}
                  </span>
                </div>
                <p className="text-meta mt-1">
                  {formatDateTime(show.startsAt)}{show.venueName ? ` · ${show.venueName}` : ""}
                </p>
              </div>
            </div>

            <dl className="grid grid-cols-3 gap-px border-y border-[var(--line)] bg-[var(--line)] text-center">
              {[
                { label: "Sıra", value: `${show.itemCount}` },
                { label: "Öğrenci", value: `${show.performerCount}` },
                { label: "Süre", value: formatDuration(show.totalDurationMinutes) },
              ].map((cell) => (
                <div key={cell.label} className="bg-white px-2 py-2.5">
                  <dt className="text-[.75rem] font-bold text-[var(--muted)]">{cell.label}</dt>
                  <dd className="mt-0.5 text-sm font-bold tabular-nums">{cell.value}</dd>
                </div>
              ))}
            </dl>

            <div className="flex flex-wrap gap-2 p-4">
              <Link href={`/dashboard/shows/${show.id}`} className="btn btn-quiet flex-1">
                <Icon name="calendar" className="h-4 w-4" />Program
              </Link>
              <Link href={`/dashboard/shows/${show.id}/stage`} className="btn btn-primary flex-1">
                <Icon name="music" className="h-4 w-4" />Sahne ekranı
              </Link>
            </div>
          </article>
        ))}
      </div>

      {showCreate && <CreateShowModal onClose={() => setShowCreate(false)} />}
    </div>
  );
}

function CreateShowModal({ onClose }: { onClose: () => void }) {
  const createShow = useCreateShow();
  const [title, setTitle] = useState("");
  const [venueName, setVenueName] = useState("");
  // Varsayılan: bugünden bir ay sonra, akşam 19:00 - resitaller akşam yapılır.
  const [startsAt, setStartsAt] = useState(() => {
    const date = new Date();
    date.setMonth(date.getMonth() + 1);
    date.setHours(19, 0, 0, 0);
    const pad = (value: number) => String(value).padStart(2, "0");
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  });
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createShow.mutateAsync({
        title,
        venueName: venueName.trim() || null,
        // datetime-local yerel saati verir; sunucu timestamptz beklediği için mutlak
        // ana çevrilir (CLAUDE.md: veritabanında her zaman UTC instant).
        startsAt: new Date(startsAt).toISOString(),
      });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Gösteri oluşturulamadı.");
    }
  }

  return (
    <Modal open title="Gösteri oluştur" description="Adını, yerini ve saatini gir; programı sonraki ekranda sıraya dizersin." onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3.5">
        <label className="form-label">Gösteri adı
          <input value={title} onChange={(event) => setTitle(event.target.value)} required autoFocus maxLength={150} className="field text-sm" placeholder="2027 Yıl Sonu Gösterisi" />
        </label>
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="form-label">Yer
            <input value={venueName} onChange={(event) => setVenueName(event.target.value)} maxLength={150} className="field text-sm" placeholder="Kültür Merkezi" />
          </label>
          <label className="form-label">Tarih ve saat
            <input type="datetime-local" value={startsAt} onChange={(event) => setStartsAt(event.target.value)} required className="field text-sm" />
          </label>
        </div>
        {error && <FormMessage tone="error">{error}</FormMessage>}
        <FormActions onCancel={onClose} submitLabel="Gösteriyi oluştur" pending={createShow.isPending} pendingLabel="Oluşturuluyor…" />
      </form>
    </Modal>
  );
}
