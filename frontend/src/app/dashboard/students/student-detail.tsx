"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Icon, instrumentBadgeStyle } from "@/components/icons";
import { AddButton, FormActions, FormMessage, Modal, RowMenu, RowMenuItem, SectionHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  useCreateAndLinkGuardian,
  useCreateEnrollment,
  useEndEnrollment,
  useEnrollments,
  useInstruments,
  useStudentGuardians,
  useTeachers,
  useUpdateGuardian,
  useUpdateStudent,
  type Student,
  type StudentGuardianLink,
} from "@/lib/people";

const ENROLLMENT_STATUS_LABEL: Record<string, string> = { Active: "aktif", Paused: "durduruldu", Ended: "sona erdi" };

// isAdmin=false (Teacher) iken veli bilgisi hiç istenmez - /api/students/{id}/guardians
// Admin-only olduğu için Teacher'a 403 dönerdi (docs/04-permissions.md).
export function StudentDetail({ student, isAdmin }: { student: Student; isAdmin: boolean }) {
  const studentId = student.id;
  const [showGuardianForm, setShowGuardianForm] = useState(false);
  const [showEnrollmentForm, setShowEnrollmentForm] = useState(false);
  const [editingStudent, setEditingStudent] = useState(false);
  const [editingGuardian, setEditingGuardian] = useState<StudentGuardianLink | null>(null);
  const { data: guardians } = useStudentGuardians(isAdmin ? studentId : "");
  const { data: enrollments } = useEnrollments(studentId);
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();
  const updateStudent = useUpdateStudent();
  const activeEnrollments = enrollments?.filter((enrollment) => enrollment.status === "Active") ?? [];
  const fullName = `${student.firstName} ${student.lastName}`;

  function toggleStatus() {
    updateStudent.mutate({
      studentId,
      firstName: student.firstName,
      lastName: student.lastName,
      birthDate: student.birthDate,
      status: student.status === "Active" ? "Inactive" : "Active",
    });
  }

  return (
    <div className="space-y-3 border-t border-[var(--line)] bg-[var(--surface-muted)] p-3 sm:p-4">
      {/* Künye: satır kapalıyken yalnızca ad ve doğum tarihi görünüyor; açılınca öğrencinin
          kim olduğu (yaş, durum) ve üzerinde yapılabilecek işler tek bakışta belli olsun.
          Eylemler tek bir "düzenle" ikonu değil: birincil eylem yazıyla, ikincil olanlar
          "⋮" menüsünde (kullanıcı isteği). */}
      <section className="app-card flex flex-wrap items-center gap-3 p-3.5 sm:gap-4">
        <span className="grid h-14 w-14 shrink-0 place-items-center rounded-2xl bg-[var(--brand-soft)] font-serif text-lg font-bold italic text-[var(--brand-strong)]">
          {initials(fullName)}
        </span>
        <div className="min-w-0 flex-1">
          <h3 className="truncate font-serif text-lg font-bold italic">{fullName}</h3>
          <p className="text-meta mt-0.5">{student.birthDate}{ageOf(student.birthDate) !== null && ` · ${ageOf(student.birthDate)} yaş`}</p>
          <span className={`mt-1.5 inline-flex rounded-full px-2 py-0.5 text-[.62rem] font-bold ${student.status === "Active" ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>
            {student.status === "Active" ? "Aktif öğrenci" : "Pasif öğrenci"}
          </span>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <Link href={`/dashboard/progress?studentId=${studentId}`} className="btn btn-quiet">
            <Icon name="activity" className="h-4 w-4" /> Gelişim
          </Link>
          {isAdmin && (
            <>
              <button type="button" onClick={() => setEditingStudent(true)} className="btn btn-quiet">
                <Icon name="pencil" className="h-4 w-4" /> Düzenle
              </button>
              <RowMenu label={`${fullName} için diğer işlemler`}>
                {(close) => (
                  <>
                    <RowMenuItem icon="plus" onClick={() => { close(); setShowEnrollmentForm(true); }}>Kurs ekle</RowMenuItem>
                    <RowMenuItem icon="students" onClick={() => { close(); setShowGuardianForm(true); }}>Veli ekle</RowMenuItem>
                    <RowMenuItem
                      icon={student.status === "Active" ? "x" : "check"}
                      tone={student.status === "Active" ? "danger" : "default"}
                      onClick={() => { close(); toggleStatus(); }}
                    >
                      {student.status === "Active" ? "Pasife al" : "Yeniden aktif et"}
                    </RowMenuItem>
                  </>
                )}
              </RowMenu>
            </>
          )}
        </div>
      </section>

      <div className="grid gap-3 lg:grid-cols-2">
        {isAdmin && (
          <section className="app-card overflow-hidden">
            <div className="border-b border-[var(--line)] p-3.5">
              <SectionHeader
                title="Veliler"
                description={`${fullName} adına iletişime geçilecek kişiler`}
                actions={<AddButton label="Veli ekle" tone="quiet" onClick={() => setShowGuardianForm(true)} />}
              />
            </div>
            <ul className="divide-y divide-[var(--line)]">
              {guardians?.map((guardian) => (
                <li key={guardian.id} className="flex items-center gap-3 px-3.5 py-3">
                  <span className="grid h-9 w-9 shrink-0 place-items-center rounded-xl bg-[var(--surface-muted)] text-[.62rem] font-bold text-[var(--brand-strong)]">
                    {initials(`${guardian.firstName} ${guardian.lastName}`)}
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-bold">{guardian.firstName} {guardian.lastName}</span>
                    <span className="text-meta mt-0.5 block truncate">
                      {guardian.phoneNumber}{guardian.relationship && ` · ${guardian.relationship}`}
                    </span>
                  </span>
                  {guardian.isPrimary && (
                    <span className="shrink-0 rounded-full bg-[var(--success-soft)] px-2 py-0.5 text-[.62rem] font-bold text-[var(--success-strong)]">Birincil</span>
                  )}
                  <RowMenu label={`${guardian.firstName} ${guardian.lastName} için işlemler`}>
                    {(close) => (
                      <>
                        <RowMenuItem icon="pencil" onClick={() => { close(); setEditingGuardian(guardian); }}>Veliyi düzenle</RowMenuItem>
                        <RowMenuItem icon="phone" onClick={() => { close(); window.location.href = `tel:${guardian.phoneNumber}`; }}>Ara</RowMenuItem>
                      </>
                    )}
                  </RowMenu>
                </li>
              ))}
              {guardians?.length === 0 && <li className="text-meta px-3.5 py-6 text-center">Henüz veli eklenmemiş.</li>}
            </ul>
          </section>
        )}

        <section className="app-card overflow-hidden">
          <div className="border-b border-[var(--line)] p-3.5">
            <SectionHeader
              title="Kurslar"
              description={`${fullName} adına açık kurs kayıtları`}
              actions={isAdmin ? <AddButton label="Kurs ekle" tone="quiet" onClick={() => setShowEnrollmentForm(true)} /> : undefined}
            />
          </div>
          <ul className="divide-y divide-[var(--line)]">
            {activeEnrollments.map((enrollment) => {
              const teacher = teachers?.find((item) => item.id === enrollment.teacherId);
              const instrument = instruments?.find((item) => item.id === enrollment.instrumentId);
              return (
                <EnrollmentRow
                  key={enrollment.id}
                  studentId={studentId}
                  enrollmentId={enrollment.id}
                  teacherId={enrollment.teacherId}
                  instrumentName={instrument?.name ?? "Enstrüman"}
                  teacherName={teacher ? `${teacher.firstName} ${teacher.lastName}` : "Öğretmen"}
                  status={ENROLLMENT_STATUS_LABEL[enrollment.status] ?? enrollment.status}
                  isAdmin={isAdmin}
                />
              );
            })}
            {!activeEnrollments.length && <li className="text-meta px-3.5 py-6 text-center">Henüz aktif kurs yok.</li>}
          </ul>
        </section>
      </div>

      {isAdmin && (
        <>
          <AddGuardianForm studentId={studentId} open={showGuardianForm} onClose={() => setShowGuardianForm(false)} />
          <Modal open={showEnrollmentForm} title="Kurs ekle" description="Öğretmen ve enstrümanı seçerek bu öğrenciye bağla." onClose={() => setShowEnrollmentForm(false)} size="sm">
            <AddEnrollmentForm studentId={studentId} teachers={teachers ?? []} instruments={instruments ?? []} onClose={() => setShowEnrollmentForm(false)} />
          </Modal>
          <Modal open={editingStudent} title="Öğrenciyi düzenle" onClose={() => setEditingStudent(false)} size="sm">
            <EditStudentForm student={student} onClose={() => setEditingStudent(false)} />
          </Modal>
          {editingGuardian && (
            <Modal open title="Veliyi düzenle" onClose={() => setEditingGuardian(null)} size="sm">
              <EditGuardianForm studentId={studentId} guardian={editingGuardian} onClose={() => setEditingGuardian(null)} />
            </Modal>
          )}
        </>
      )}
    </div>
  );
}

function initials(name: string) {
  return name.split(" ").map((part) => part[0]).filter(Boolean).slice(0, 2).join("").toLocaleUpperCase("tr-TR");
}

// Doğum tarihi "yyyy-MM-dd" gelir; geçersiz/boş değerde yaş yazılmaz (uydurma bilgi göstermeyiz).
function ageOf(birthDate: string) {
  const born = new Date(birthDate);
  if (Number.isNaN(born.getTime())) return null;
  const today = new Date();
  let age = today.getFullYear() - born.getFullYear();
  const monthDiff = today.getMonth() - born.getMonth();
  if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < born.getDate())) age -= 1;
  return age >= 0 && age < 120 ? age : null;
}

function EditStudentForm({ student, onClose }: { student: Student; onClose: () => void }) {
  const updateStudent = useUpdateStudent();
  const [firstName, setFirstName] = useState(student.firstName);
  const [lastName, setLastName] = useState(student.lastName);
  const [birthDate, setBirthDate] = useState(student.birthDate);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await updateStudent.mutateAsync({ studentId: student.id, firstName, lastName, birthDate, status: student.status });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci güncellenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Ad<input value={firstName} onChange={(event) => setFirstName(event.target.value)} required className="field text-sm" /></label>
        <label className="form-label">Soyad<input value={lastName} onChange={(event) => setLastName(event.target.value)} required className="field text-sm" /></label>
      </div>
      <label className="form-label">Doğum tarihi<input type="date" value={birthDate} onChange={(event) => setBirthDate(event.target.value)} required className="field text-sm" /></label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Değişiklikleri kaydet" pending={updateStudent.isPending} />
    </form>
  );
}

function EditGuardianForm({ studentId, guardian, onClose }: { studentId: string; guardian: StudentGuardianLink; onClose: () => void }) {
  const updateGuardian = useUpdateGuardian(studentId);
  const [firstName, setFirstName] = useState(guardian.firstName);
  const [lastName, setLastName] = useState(guardian.lastName);
  const [phoneNumber, setPhoneNumber] = useState(guardian.phoneNumber);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await updateGuardian.mutateAsync({ guardianId: guardian.id, firstName, lastName, phoneNumber });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Veli güncellenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Ad<input value={firstName} onChange={(event) => setFirstName(event.target.value)} required className="field text-sm" /></label>
        <label className="form-label">Soyad<input value={lastName} onChange={(event) => setLastName(event.target.value)} required className="field text-sm" /></label>
      </div>
      <label className="form-label">Telefon<input value={phoneNumber} onChange={(event) => setPhoneNumber(event.target.value)} required className="field text-sm" /></label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Değişiklikleri kaydet" pending={updateGuardian.isPending} />
    </form>
  );
}

function EnrollmentRow({ studentId, enrollmentId, teacherId, instrumentName, teacherName, status, isAdmin }: { studentId: string; enrollmentId: string; teacherId: string; instrumentName: string; teacherName: string; status: string; isAdmin: boolean }) {
  const endEnrollment = useEndEnrollment(studentId);
  const router = useRouter();
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const badge = instrumentBadgeStyle(instrumentName);

  async function remove() {
    setError(null);
    try {
      await endEnrollment.mutateAsync(enrollmentId);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Kurs kaldırılamadı.");
      setConfirming(false);
    }
  }

  return (
    <li className="px-3.5 py-3">
      <div className="flex items-center gap-3">
        {/* Enstrüman rozeti liste satırındakiyle aynı kimlikte (icons.tsx) - öğrenci
            listesinde gördüğü ikonu detayda da görsün. */}
        <span className={`grid h-9 w-9 shrink-0 place-items-center rounded-xl ${badge.className}`}>
          <Icon name={badge.icon} className="h-4 w-4" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-bold">{instrumentName}</span>
          <span className="text-meta mt-0.5 block truncate">{teacherName}</span>
        </span>
        <span className="shrink-0 rounded-full bg-[var(--success-soft)] px-2 py-0.5 text-[.62rem] font-bold text-[var(--success-strong)]">{status}</span>
        {isAdmin && (
          <RowMenu label={`${instrumentName} kursu için işlemler`}>
            {(close) => (
              <>
                <RowMenuItem icon="teachers" onClick={() => { close(); router.push(`/dashboard/teachers#teacher-${teacherId}`); }}>
                  Öğretmene git
                </RowMenuItem>
                <RowMenuItem icon="x" tone="danger" onClick={() => { close(); setConfirming(true); }}>
                  Kursu sonlandır
                </RowMenuItem>
              </>
            )}
          </RowMenu>
        )}
      </div>
      {confirming && (
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2 rounded-lg bg-[var(--danger-soft)] px-3 py-2">
          <p className="text-[.66rem] font-semibold text-[var(--danger-strong)]">Kurs sonlandırılsın mı? Gelecekteki dersler durdurulur.</p>
          <span className="flex gap-1.5">
            <button type="button" onClick={() => setConfirming(false)} className="pressable min-h-8 rounded-lg bg-white px-2.5 text-[.64rem] font-bold">Vazgeç</button>
            <button type="button" onClick={remove} disabled={endEnrollment.isPending} className="pressable min-h-8 rounded-lg bg-[var(--danger)] px-2.5 text-[.64rem] font-bold text-white disabled:opacity-50">
              {endEnrollment.isPending ? "Sonlandırılıyor…" : "Sonlandır"}
            </button>
          </span>
        </div>
      )}
      {error && <p role="alert" className="mt-2 text-[.66rem] font-semibold text-[var(--danger-strong)]">{error}</p>}
    </li>
  );
}

function AddGuardianForm({ studentId, open, onClose }: { studentId: string; open: boolean; onClose: () => void }) {
  const createAndLink = useCreateAndLinkGuardian(studentId);
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [relationship, setRelationship] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createAndLink.mutateAsync({ firstName, lastName, phoneNumber, relationship, isPrimary: true });
      setFirstName("");
      setLastName("");
      setPhoneNumber("");
      setRelationship("");
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Veli eklenemedi.");
    }
  }

  return (
    <Modal open={open} title="Veli ekle" onClose={onClose} size="sm">
      <form onSubmit={handleSubmit} className="space-y-3.5">
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="form-label">Ad<input value={firstName} onChange={(e) => setFirstName(e.target.value)} required className="field text-sm" /></label>
          <label className="form-label">Soyad<input value={lastName} onChange={(e) => setLastName(e.target.value)} required className="field text-sm" /></label>
          <label className="form-label">Telefon<input placeholder="0555 111 22 33" value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)} required className="field text-sm" /></label>
          <label className="form-label">Yakınlık<input placeholder="Anne / baba" value={relationship} onChange={(e) => setRelationship(e.target.value)} className="field text-sm" /></label>
        </div>
        {error && <FormMessage tone="error">{error}</FormMessage>}
        <FormActions onCancel={onClose} submitLabel="Veli ekle" pending={createAndLink.isPending} pendingLabel="Ekleniyor…" />
      </form>
    </Modal>
  );
}

function AddEnrollmentForm({
  studentId,
  teachers,
  instruments,
  onClose,
}: {
  studentId: string;
  teachers: { id: string; firstName: string; lastName: string; instrumentIds: string[] }[];
  instruments: { id: string; name: string }[];
  onClose: () => void;
}) {
  const createEnrollment = useCreateEnrollment(studentId);
  const [teacherId, setTeacherId] = useState("");
  const [instrumentId, setInstrumentId] = useState("");
  const [startedAt, setStartedAt] = useState(() => new Date().toISOString().slice(0, 10));
  const [error, setError] = useState<string | null>(null);

  const availableInstruments = teacherId
    ? instruments.filter((i) => teachers.find((t) => t.id === teacherId)?.instrumentIds.includes(i.id))
    : instruments;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createEnrollment.mutateAsync({ teacherId, instrumentId, startedAt });
      setTeacherId("");
      setInstrumentId("");
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Kayıt oluşturulamadı.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Öğretmen
          <select value={teacherId} onChange={(e) => { setTeacherId(e.target.value); setInstrumentId(""); }} required className="field text-sm">
            <option value="">Öğretmen seç</option>
            {teachers.map((t) => <option key={t.id} value={t.id}>{t.firstName} {t.lastName}</option>)}
          </select>
        </label>
        <label className="form-label">Enstrüman
          <select value={instrumentId} onChange={(e) => setInstrumentId(e.target.value)} required disabled={!teacherId} className="field text-sm">
            <option value="">Enstrüman seç</option>
            {availableInstruments.map((i) => <option key={i.id} value={i.id}>{i.name}</option>)}
          </select>
        </label>
      </div>
      <label className="form-label">Başlangıç tarihi<input type="date" value={startedAt} onChange={(e) => setStartedAt(e.target.value)} required className="field text-sm" /></label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Kursu ekle" pending={createEnrollment.isPending} pendingLabel="Ekleniyor…" />
    </form>
  );
}
