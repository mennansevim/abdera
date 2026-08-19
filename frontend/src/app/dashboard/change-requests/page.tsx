"use client";

import { useState } from "react";
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
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Ders Değişikliği Talepleri</h1>

      {error && <p className="text-sm text-red-600">{error}</p>}
      {isLoading && <p className="text-sm text-neutral-500">Yükleniyor…</p>}
      {requests?.length === 0 && (
        <p className="rounded-lg border border-dashed border-neutral-300 p-6 text-sm text-neutral-500">
          Bekleyen talep yok.
        </p>
      )}

      <ul className="space-y-3">
        {requests?.map((request) => (
          <li key={request.id} className="rounded-lg border border-neutral-200 bg-white p-4">
            <div className="mb-2 text-sm">
              <p>
                Önerilen saat:{" "}
                <strong>
                  {new Date(request.proposedStartAt).toLocaleString("tr-TR", {
                    weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit",
                  })}
                </strong>
              </p>
              {request.reason && <p className="text-neutral-500">Sebep: {request.reason}</p>}
              <p className="text-xs text-neutral-400">
                Talep tarihi: {new Date(request.createdAt).toLocaleString("tr-TR")}
              </p>
            </div>
            <div className="flex gap-2">
              <button
                onClick={() => handleApprove(request.id)}
                disabled={busyId === request.id}
                className="rounded-md bg-neutral-900 px-3 py-1.5 text-sm text-white disabled:opacity-50"
              >
                Onayla
              </button>
              <button
                onClick={() => handleReject(request.id)}
                disabled={busyId === request.id}
                className="rounded-md border border-neutral-300 px-3 py-1.5 text-sm text-neutral-700 hover:bg-neutral-100 disabled:opacity-50"
              >
                Reddet
              </button>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
