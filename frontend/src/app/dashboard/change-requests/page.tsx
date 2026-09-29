"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { AdminGate, EmptyState, PageHeader, Panel } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useApproveChangeRequest, usePendingChangeRequests, useRejectChangeRequest } from "@/lib/attendance";
import { useDecideStudentDeletionRequest, useStudentDeletionRequests } from "@/lib/people";

function initials(name: string) {
  return name.split(" ").map((part) => part[0]).filter(Boolean).slice(0, 2).join("").toLocaleUpperCase("tr-TR");
}

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
    <div className="space-y-3">
      <PageHeader title="Talepler" description="Öğretmenlerden gelen ders saati ve öğrenci silme isteklerini karara bağla." />

      <div className="grid items-start gap-3 xl:grid-cols-2">
        <Panel flush title="Ders değişikliği" meta={requests ? `${requests.length} bekleyen` : undefined}>
          {error && <p role="alert" className="m-3 rounded-xl bg-[var(--danger-soft)] px-3 py-2 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}
          {isLoading && <div className="space-y-2 p-4">{Array.from({ length: 3 }, (_, index) => <div key={index} className="skeleton h-12 rounded-lg" />)}</div>}
          {!isLoading && requests?.length === 0 && <EmptyState icon="swap" title="Bekleyen talep yok" />}
          <ul className="divide-y divide-[var(--line)]">
            {requests?.map((request) => (
              <li key={request.id} className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-2.5">
                <div className="min-w-0 flex-1 text-sm">
                  <p className="font-bold">
                    {new Date(request.proposedStartAt).toLocaleString("tr-TR", {
                      weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit",
                    })}
                  </p>
                  <p className="text-meta truncate">
                    {request.reason ? `${request.reason} · ` : ""}talep {new Date(request.createdAt).toLocaleString("tr-TR", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}
                  </p>
                </div>
                <div className="flex shrink-0 gap-1.5">
                  <button onClick={() => handleApprove(request.id)} disabled={busyId === request.id} className="btn btn-primary px-3 text-xs">
                    <Icon name="check" className="h-4 w-4" /> Onayla
                  </button>
                  <button onClick={() => handleReject(request.id)} disabled={busyId === request.id} className="btn btn-quiet px-3 text-xs">
                    <Icon name="x" className="h-4 w-4" /> Reddet
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </Panel>

        {/* Öğrenci silme talepleri (J2): geri alınamaz bir işlem ve bekleyen bir talep
            öğretmeni bloke ediyor. */}
        <StudentDeletionRequestsSection />
      </div>
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

  return (
    <Panel flush title="Öğrenci silme talepleri" meta={requests ? `${requests.length} bekleyen` : undefined}>
      {error && <p role="alert" className="m-3 rounded-xl bg-[var(--danger-soft)] px-3 py-2 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}

      {!requests?.length && <EmptyState icon="students" title="Bekleyen silme talebi yok" />}
      <div className="divide-y divide-[var(--line)]">

      {requests?.map((request) => {
        const stats = request.impact
          ? [
              { label: "Kurs", value: request.impact.enrollments },
              { label: "Ders", value: request.impact.lessons },
              { label: "Yoklama", value: request.impact.attendances },
              { label: "Aidat", value: request.impact.receivables },
              { label: "Ödeme", value: request.impact.payments },
            ].filter((cell) => cell.value > 0)
          : [];

        return (
          <article key={request.id} className="border-l-4 border-l-[var(--danger)] px-4 py-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex min-w-0 items-center gap-3">
                <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--danger-soft)] text-xs font-bold text-[var(--danger-strong)]">
                  {initials(request.studentName)}
                </span>
                <div className="min-w-0">
                  <p className="truncate text-sm font-bold">{request.studentName}</p>
                  <p className="text-meta mt-0.5 truncate">
                    {request.requestedByName} · {new Date(request.createdAt).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}
                  </p>
                </div>
              </div>

              <div className="flex shrink-0 gap-2">
                <button type="button" onClick={() => handle(request.id, "reject")} disabled={busyId === request.id} className="btn btn-quiet">
                  {busyId === request.id ? "…" : "Reddet"}
                </button>
                <button
                  type="button"
                  onClick={() => handle(request.id, "approve")}
                  disabled={busyId === request.id}
                  className="btn bg-[var(--danger-strong)] text-white"
                >
                  {busyId === request.id ? "Siliniyor…" : "Onayla ve sil"}
                </button>
              </div>
            </div>

            <p className="mt-2 rounded-lg bg-[var(--surface-muted)] px-3 py-1.5 text-xs">{request.reason}</p>

            {stats.length > 0 && (
              <div className="mt-2 flex flex-wrap gap-1.5">
                {stats.map((cell) => (
                  <span key={cell.label} className="inline-flex items-center gap-1.5 rounded-lg bg-[var(--surface-muted)] px-2.5 py-1 text-xs">
                    <strong className="tabular-nums">{cell.value}</strong>
                    <span className="text-[var(--muted)]">{cell.label}</span>
                  </span>
                ))}
              </div>
            )}

            {request.impact && request.impact.payments > 0 && (
              <p className="mt-2 rounded-lg bg-[var(--danger-soft)] px-3 py-2 text-xs font-bold text-[var(--danger-strong)]">
                {request.impact.payments} ödeme kaydı da silinecek · {request.impact.collectedAmount.toLocaleString("tr-TR")} {request.impact.currency}
              </p>
            )}
          </article>
        );
      })}
      </div>
    </Panel>
  );
}
