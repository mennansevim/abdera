"use client";

import { useState } from "react";
import { Icon } from "@/components/icons";
import { FormMessage, Modal } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  useDeleteStudent,
  useDeleteTeacher,
  useStudentDeletionImpact,
  useTeacherDeletionImpact,
  useTeachers,
} from "@/lib/people";

// Kalıcı silme onayı. İki şeyi aynı ekranda yapar:
//   1. NE SİLİNECEĞİNİ SAYAR. "3 kurs kaydı, 47 ders, 12 yoklama silinecek" - kullanıcı
//      neyi kaybedeceğini işlemden önce görür. Sayılar sunucudan gelir, tahmin değildir.
//   2. PARA VARSA AYRICA ONAY İSTER. Tahsilat geçmişi silinecekse ek bir kutu işaretlenir;
//      sunucu da aynı kuralı uygular (force), yani onay yalnızca görsel bir süs değil.
//
// Öğretmende üçüncü bir yol var: öğrencileri başka bir öğretmene devretmek. Gerçek hayatta
// olan budur ve öğrencilerin ders/aidat geçmişine hiç dokunmadan öğretmeni kaldırır.

function money(value: number, currency: string) {
  return new Intl.NumberFormat("tr-TR", { style: "currency", currency, maximumFractionDigits: 0 }).format(value);
}

function ImpactList({ rows }: { rows: Array<{ label: string; count: number }> }) {
  const visible = rows.filter((row) => row.count > 0);
  if (visible.length === 0) return <p className="text-meta">Bu kayda bağlı başka bir veri yok.</p>;

  return (
    <ul className="grid gap-1.5 sm:grid-cols-2">
      {visible.map((row) => (
        <li key={row.label} className="flex items-baseline justify-between gap-2 rounded-lg bg-[var(--surface-muted)] px-3 py-1.5 text-xs">
          <span className="text-[var(--muted)]">{row.label}</span>
          <strong className="tabular-nums">{row.count}</strong>
        </li>
      ))}
    </ul>
  );
}

export function DeleteStudentDialog({
  studentId, studentName, onClose, onDeleted,
}: {
  studentId: string;
  studentName: string;
  onClose: () => void;
  onDeleted?: () => void;
}) {
  const { data: impact, isLoading } = useStudentDeletionImpact(studentId);
  const deleteStudent = useDeleteStudent();
  const [acceptsMoneyLoss, setAcceptsMoneyLoss] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const hasPayments = (impact?.payments ?? 0) > 0;
  const blocked = hasPayments && !acceptsMoneyLoss;

  async function confirm() {
    setError(null);
    try {
      await deleteStudent.mutateAsync({ studentId, force: hasPayments });
      onDeleted?.();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci silinemedi.");
    }
  }

  return (
    <Modal open title={`${studentName} kalıcı olarak silinsin mi?`} description="Bu işlem geri alınamaz." onClose={onClose}>
      <div className="space-y-3.5">
        {isLoading && <div className="skeleton h-24 rounded-xl" />}

        {impact && (
          <>
            <ImpactList rows={[
              { label: "Kurs kaydı", count: impact.enrollments },
              { label: "Ders", count: impact.lessons },
              { label: "Yoklama", count: impact.attendances },
              { label: "Aidat", count: impact.receivables },
              { label: "Telafi hakkı", count: impact.makeupCredits },
              { label: "Yetenek değerlendirmesi", count: impact.assessments },
              { label: "Gösteri sırası", count: impact.showItems },
            ]} />

            {hasPayments && (
              <div className="rounded-xl border border-[var(--danger)]/40 bg-[var(--danger-soft)]/50 p-3">
                <p className="text-xs font-bold text-[var(--danger-strong)]">
                  {impact.payments} ödeme kaydı da silinecek · {money(impact.collectedAmount, impact.currency)}
                </p>
                <p className="text-meta mt-1">Bu, tahsil edilmiş paranın muhasebe geçmişidir. Silindikten sonra geri getirilemez.</p>
                <label className="mt-2 flex items-start gap-2 text-xs font-semibold">
                  <input type="checkbox" checked={acceptsMoneyLoss} onChange={(event) => setAcceptsMoneyLoss(event.target.checked)} className="mt-0.5" />
                  <span>Tahsilat geçmişinin de silineceğini anlıyorum.</span>
                </label>
              </div>
            )}

            {impact.guardiansLeftWithoutStudents > 0 && (
              <p className="rounded-lg bg-[var(--warning-soft)]/60 px-3 py-2 text-xs text-[var(--warning-strong)]">
                {impact.guardiansLeftWithoutStudents} veli hiçbir öğrenciye bağlı kalmayacak. Veli kayıtları silinmez;
                istersen Veliler ekranından ayrıca kaldırabilirsin.
              </p>
            )}
          </>
        )}

        {error && <FormMessage tone="error">{error}</FormMessage>}

        <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-3.5">
          <button type="button" onClick={onClose} className="btn btn-quiet">Vazgeç</button>
          <button
            type="button"
            onClick={confirm}
            disabled={deleteStudent.isPending || blocked || isLoading}
            className="pressable min-h-11 rounded-xl bg-[var(--danger-strong)] px-4 text-sm font-bold text-white disabled:opacity-50"
          >
            {deleteStudent.isPending ? "Siliniyor…" : "Kalıcı olarak sil"}
          </button>
        </div>
      </div>
    </Modal>
  );
}

export function DeleteTeacherDialog({
  teacherId, teacherName, onClose, onDeleted,
}: {
  teacherId: string;
  teacherName: string;
  onClose: () => void;
  onDeleted?: () => void;
}) {
  const { data: impact, isLoading } = useTeacherDeletionImpact(teacherId);
  const { data: teachers } = useTeachers();
  const deleteTeacher = useDeleteTeacher();
  const [reassignTo, setReassignTo] = useState("");
  const [acceptsMoneyLoss, setAcceptsMoneyLoss] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const successors = (teachers ?? []).filter((teacher) => teacher.id !== teacherId && teacher.status === "Active");
  const hasStudents = (impact?.enrollments ?? 0) > 0;
  const hasPayments = (impact?.payments ?? 0) > 0;
  const willDestroyMoney = hasPayments && !reassignTo;
  const blocked = willDestroyMoney && !acceptsMoneyLoss;

  async function confirm() {
    setError(null);
    try {
      await deleteTeacher.mutateAsync({
        teacherId,
        reassignTo: reassignTo || null,
        force: willDestroyMoney,
      });
      onDeleted?.();
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğretmen silinemedi.");
    }
  }

  return (
    <Modal open title={`${teacherName} kalıcı olarak silinsin mi?`} description="Bu işlem geri alınamaz." onClose={onClose}>
      <div className="space-y-3.5">
        {isLoading && <div className="skeleton h-24 rounded-xl" />}

        {impact && (
          <>
            <ImpactList rows={[
              { label: "Kurs kaydı", count: impact.enrollments },
              { label: "Etkilenen öğrenci", count: impact.affectedStudents },
              { label: "Ders", count: impact.lessons },
              { label: "Müsaitlik kaydı", count: impact.availabilities },
              { label: "İzin kaydı", count: impact.timeOffs },
              { label: "Ders notu", count: impact.lessonNotes },
              { label: "Yetenek değerlendirmesi", count: impact.assessments },
            ]} />

            {hasStudents && (
              <label className="form-label block">
                <span>Öğrencileri devret (önerilir)</span>
                <select value={reassignTo} onChange={(event) => { setReassignTo(event.target.value); setError(null); }} className="field text-sm">
                  <option value="">Devretme - kayıtları da sil</option>
                  {successors.map((teacher) => (
                    <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>
                  ))}
                </select>
                <span className="text-meta mt-1 block">
                  {reassignTo
                    ? `${impact.affectedStudents} öğrencinin kaydı, dersleri ve aidat geçmişi seçilen öğretmene geçer; hiçbir veri silinmez.`
                    : "Devretmezsen bu öğretmene bağlı kurs kayıtları, dersler ve aidatlar da silinir."}
                </span>
              </label>
            )}

            {willDestroyMoney && (
              <div className="rounded-xl border border-[var(--danger)]/40 bg-[var(--danger-soft)]/50 p-3">
                <p className="text-xs font-bold text-[var(--danger-strong)]">
                  {impact.affectedStudents} öğrencinin {impact.payments} ödeme kaydı silinecek · {money(impact.collectedAmount, impact.currency)}
                </p>
                <p className="text-meta mt-1">Yukarıdan bir öğretmen seçersen bu geçmiş korunur.</p>
                <label className="mt-2 flex items-start gap-2 text-xs font-semibold">
                  <input type="checkbox" checked={acceptsMoneyLoss} onChange={(event) => setAcceptsMoneyLoss(event.target.checked)} className="mt-0.5" />
                  <span>Öğrencilerin tahsilat geçmişinin de silineceğini anlıyorum.</span>
                </label>
              </div>
            )}

            {impact.hasUserAccount && (
              <p className="flex items-start gap-2 rounded-lg bg-[var(--surface-muted)] px-3 py-2 text-xs text-[var(--muted)]">
                <Icon name="shield" className="mt-0.5 h-3.5 w-3.5 shrink-0" />
                Öğretmenin giriş hesabı da silinir; bu e-posta ile artık oturum açılamaz.
              </p>
            )}
          </>
        )}

        {error && <FormMessage tone="error">{error}</FormMessage>}

        <div className="flex justify-end gap-2 border-t border-[var(--line)] pt-3.5">
          <button type="button" onClick={onClose} className="btn btn-quiet">Vazgeç</button>
          <button
            type="button"
            onClick={confirm}
            disabled={deleteTeacher.isPending || blocked || isLoading}
            className="pressable min-h-11 rounded-xl bg-[var(--danger-strong)] px-4 text-sm font-bold text-white disabled:opacity-50"
          >
            {deleteTeacher.isPending ? "Siliniyor…" : reassignTo ? "Devret ve sil" : "Kalıcı olarak sil"}
          </button>
        </div>
      </div>
    </Modal>
  );
}
