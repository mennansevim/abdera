"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { AdminGate, PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useApproveChangeRequest, usePendingChangeRequests, useRejectChangeRequest } from "@/lib/attendance";
import { useDecideStudentDeletionRequest, useStudentDeletionRequests } from "@/lib/people";

// docs/00-master-prompt.md Admin UX: "lesson-change queue". docs/05-state-models.md:
// PENDING -> APPROVED/REJECTED (ALTERNATIVE_PROPOSED/PARENT_* Phase 5'te - WhatsApp gerekir).
export default function ChangeRequestsPage() {
  return <AdminGate><ChangeRequestsPageContent /></AdminGate>;
}

// Talep inceleme/onaylama tamamen Admin'e özel (docs/04-permissions.md) - bir öğretmenin
// KENDİ talebini oluşturması ayrı bir uçtan (Takvim ekranı) yapılır, bu ekrana gerek duymaz.
function ChangeRequestsPageContent() {
  const { data: requests, isLoading } = usePendingChangeRequests();
  const approve = useApproveChangeRequest();
  const reject = useRejectChangeRequest();
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  async function handleApprove(id: string) {
    setError(null);
    setBusyId(id);
    try {
      await approve.mutateAsync(id);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Onaylanamadı.");
    } finally {
      setBusyId(null);
    }
  }

  async function handleReject(id: string) {
    setError(null);
    setBusyId(id);
    try {
      await reject.mutateAsync(id);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Reddedilemedi.");
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="space-y-4">
      <PageHeader title="Talepler" description="Öğretmenlerden gelen ders saati ve öğrenci silme isteklerini karara bağla." />

      {/* Öğrenci silme talepleri (J2). Ders değişikliğinden ÖNCE geliyor: geri alınamaz
          bir işlem ve bekleyen bir talep öğretmeni bloke ediyor. */}
      <StudentDeletionRequestsSection />

      <h2 className="text-title pt-2">Ders değişikliği</h2>

      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}

      {isLoading && <div className="space-y-3">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-24 rounded-2xl" />)}</div>}

      {!isLoading && requests?.length === 0 && (
        <div className="app-card grid min-h-40 place-items-center border-dashed p-8 text-center">
          <div>
            <span className="mx-auto grid h-12 w-12 place-items-center rounded-2xl bg-[var(--brand-soft)] text-[var(--brand)]"><Icon name="swap" className="h-6 w-6" /></span>
            <p className="mt-4 text-sm font-bold">Bekleyen talep yok</p>
          </div>
        </div>
      )}

      <ul className="space-y-3">
        {requests?.map((request) => (
          <li key={request.id} className="app-card p-4">
            <div className="mb-3 text-sm">
              <p>
                Önerilen saat:{" "}
                <strong className="font-bold">
                  {new Date(request.proposedStartAt).toLocaleString("tr-TR", {
                    weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit",
                  })}
                </strong>
              </p>
              {request.reason && <p className="text-meta mt-1">Sebep: {request.reason}</p>}
              <p className="text-meta mt-1">
                Talep tarihi: {new Date(request.createdAt).toLocaleString("tr-TR")}
              </p>
            </div>
            <div className="flex gap-2">
              <button
                onClick={() => handleApprove(request.id)}
                disabled={busyId === request.id}
                className="pressable flex min-h-11 items-center gap-2 rounded-xl bg-[var(--brand)] px-4 text-xs font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)] disabled:opacity-50"
              >
                <Icon name="check" className="h-4 w-4" /> Onayla
              </button>
              <button
                onClick={() => handleReject(request.id)}
                disabled={busyId === request.id}
                className="pressable flex min-h-11 items-center gap-2 rounded-xl border border-[var(--line)] bg-white px-4 text-xs font-bold text-[var(--muted)] hover:bg-[var(--surface-muted)] disabled:opacity-50"
              >
                <Icon name="x" className="h-4 w-4" /> Reddet
              </button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}

// Öğretmenin açtığı öğrenci silme talepleri. Yönetici neyi onayladığını görmeden karar
// vermesin diye her satır silinecek kayıtların dökümünü taşır (sunucudan gelir).
function StudentDeletionRequestsSection() {
  const { data: requests, isLoading } = useStudentDeletionRequests("Pending");
  const decide = useDecideStudentDeletionRequest();
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handle(requestId: string, decision: "approve" | "reject") {
    if (decision === "approve" && !window.confirm("Öğrenci ve tüm geçmişi kalıcı olarak silinecek. Onaylıyor musun?")) return;
    setError(null);
    setBusyId(requestId);
    try {
      await decide.mutateAsync({ requestId, decision });
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "İşlem tamamlanamadı.");
    } finally {
      setBusyId(null);
    }
  }

  if (isLoading) return <div className="skeleton h-24 rounded-2xl" />;
  if (!requests?.length) return null;

  return (
    <section className="space-y-2">
      <h2 className="text-title">Öğrenci silme talepleri</h2>
      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}

      {requests.map((request) => (
        <article key={request.id} className="app-card border-l-4 border-l-[var(--danger)] p-4">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <p className="text-sm font-bold">{request.studentName}</p>
              <p className="text-meta mt-0.5">
                {request.requestedByName} · {new Date(request.createdAt).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}
              </p>
              <p className="mt-2 rounded-lg bg-[var(--surface-muted)] px-3 py-2 text-xs">{request.reason}</p>
            </div>

            {request.impact && (
              <dl className="grid shrink-0 grid-cols-2 gap-x-4 gap-y-1 text-[.75rem] sm:grid-cols-3">
                {[
                  { label: "Kurs", value: request.impact.enrollments },
                  { label: "Ders", value: request.impact.lessons },
                  { label: "Yoklama", value: request.impact.attendances },
                  { label: "Aidat", value: request.impact.receivables },
                  { label: "Ödeme", value: request.impact.payments },
                ].filter((cell) => cell.value > 0).map((cell) => (
                  <div key={cell.label}>
                    <dt className="text-[var(--muted)]">{cell.label}</dt>
                    <dd className="font-bold tabular-nums">{cell.value}</dd>
                  </div>
                ))}
              </dl>
            )}
          </div>

          {request.impact && request.impact.payments > 0 && (
            <p className="mt-3 rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-xs font-bold text-[var(--danger-strong)]">
              {request.impact.payments} ödeme kaydı da silinecek · {request.impact.collectedAmount.toLocaleString("tr-TR")} {request.impact.currency}
            </p>
          )}

          <div className="mt-3 flex flex-wrap justify-end gap-2">
            <button type="button" onClick={() => handle(request.id, "reject")} disabled={busyId === request.id} className="btn btn-quiet">
              {busyId === request.id ? "…" : "Reddet"}
            </button>
            <button
              type="button"
              onClick={() => handle(request.id, "approve")}
              disabled={busyId === request.id}
              className="pressable min-h-11 rounded-xl bg-[var(--danger-strong)] px-4 text-sm font-bold text-white disabled:opacity-50"
            >
              {busyId === request.id ? "Siliniyor…" : "Onayla ve sil"}
            </button>
          </div>
        </article>
      ))}
    </section>
  );
}
