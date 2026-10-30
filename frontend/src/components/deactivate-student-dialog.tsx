"use client";

import { useState, type FormEvent } from "react";
import { FormMessage, Modal } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  ENROLLMENT_END_NOTE_MAX_LENGTH,
  ENROLLMENT_END_REASON_LABELS,
  useStudentDeactivationImpact,
  useUpdateStudent,
  type EnrollmentEndReason,
  type Student,
} from "@/lib/people";

// "Pasife al" onayı (docs/10-decisions.md AI): sayılar sunucudan gelir, onaylanmadan hiçbir şey
// değişmez. Kurs kaydı da bittiği için ayrılma nedeni sorulur (X4). Grup dersinde yalnızca bu
// öğrenci çıkar, sınıf devam eder.
export function DeactivateStudentDialog({ student, onClose }: { student: Student; onClose: () => void }) {
  const fullName = `${student.firstName} ${student.lastName}`;
  const { data: impact, isLoading, isError } = useStudentDeactivationImpact(student.id);
  const updateStudent = useUpdateStudent();
  const [reason, setReason] = useState<EnrollmentEndReason | "">("");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const needsReason = (impact?.activeEnrollments ?? 0) > 0;
  const blocked = !impact || impact.requiresAdmin || (needsReason && !reason);

  async function confirm(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (blocked) return;
    setError(null);
    try {
      await updateStudent.mutateAsync({
        studentId: student.id,
        firstName: student.firstName,
        lastName: student.lastName,
        birthDate: student.birthDate,
        status: "Inactive",
        endReason: reason || undefined,
        endNote: note.trim() || undefined,
      });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci pasife alınamadı.");
    }
  }

  return (
    <Modal open title={`${fullName} pasife alınsın mı?`} onClose={onClose} size="sm">
      <form onSubmit={confirm} className="space-y-3.5">
        {isLoading && <div className="skeleton h-24 rounded-xl" />}
        {isError && <FormMessage tone="error">Dersler sayılamadı. Pencereyi kapatıp yeniden dene.</FormMessage>}

        {impact?.requiresAdmin && (
          <FormMessage tone="error">Bu öğrencinin başka öğretmenle de dersi var; pasife almayı yönetici yapabilir.</FormMessage>
        )}

        {impact && !impact.requiresAdmin && (
          <>
            <p className="text-sm">
              {impact.futureLessons > 0
                ? <><strong>{impact.futureLessons} gelecek ders</strong> takvimden ve {impact.teachers.length > 0 ? <strong>{impact.teachers.join(", ")}</strong> : "öğretmenin"} programından kaldırılacak.</>
                : "Takvimde kaldırılacak gelecek ders yok."}
              {impact.activeEnrollments > 0 && ` ${impact.activeEnrollments} kurs kaydı sonlandırılacak.`}
            </p>
            <ul className="text-meta list-disc space-y-1 pl-4">
              <li>Geçmiş dersler, yoklamalar, aidat ve ödemeler olduğu gibi kalır.</li>
              <li>Kaldırılan dersler için telafi hakkı doğmaz.</li>
              <li>Yeniden aktif edersen dersler kendiliğinden geri gelmez; kurs ve ders programı yeniden girilir.</li>
            </ul>

            {needsReason && (
              <>
                <label className="form-label block">Ayrılma nedeni
                  <select value={reason} onChange={(event) => setReason(event.target.value as EnrollmentEndReason | "")} required className="field text-sm">
                    <option value="">Neden seç</option>
                    {(Object.keys(ENROLLMENT_END_REASON_LABELS) as EnrollmentEndReason[]).map((item) => <option key={item} value={item}>{ENROLLMENT_END_REASON_LABELS[item]}</option>)}
                  </select>
                </label>
                <label className="form-label block">Not <span className="font-medium text-[var(--muted)]">· opsiyonel</span>
                  <input value={note} onChange={(event) => setNote(event.target.value)} maxLength={ENROLLMENT_END_NOTE_MAX_LENGTH} className="field text-sm" />
                </label>
              </>
            )}
          </>
        )}

        {error && <FormMessage tone="error">{error}</FormMessage>}

        <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-4">
          <button type="button" onClick={onClose} className="btn btn-quiet">Vazgeç</button>
          <button type="submit" disabled={blocked || updateStudent.isPending} className="btn bg-[var(--danger)] text-white hover:bg-[var(--danger-strong)] disabled:opacity-50">
            {updateStudent.isPending ? "Pasife alınıyor…" : "Pasife al"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
