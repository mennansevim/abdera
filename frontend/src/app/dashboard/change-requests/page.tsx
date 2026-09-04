"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useApproveChangeRequest, usePendingChangeRequests, useRejectChangeRequest } from "@/lib/attendance";

// docs/00-master-prompt.md Admin UX: "lesson-change queue". docs/05-state-models.md:
// PENDING -> APPROVED/REJECTED (ALTERNATIVE_PROPOSED/PARENT_* Phase 5'te - WhatsApp gerekir).
export default function ChangeRequestsPage() {
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
      <PageHeader title="Ders değişikliği talepleri" description="Öğretmenlerin gönderdiği saat değişikliği isteklerini onayla veya reddet." />

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
