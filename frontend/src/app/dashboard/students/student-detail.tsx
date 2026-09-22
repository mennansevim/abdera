"use client";

import { useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Icon } from "@/components/icons";
import { FormActions, FormMessage, Modal, RowMenu, RowMenuItem } from "@/components/ui";
import { ApiError } from "@/lib/api";
import {
  formatWeeklySchedule,
  useCreateLessonSeries,
  useRescheduleLessonSeries,
  useStudentLessonSeries,
  type StudentLessonSeries,
} from "@/lib/scheduling";
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

// isAdmin=false (Teacher) iken veli bilgisi hiç istenmez - /api/students/{id}/guardians
// Admin-only olduğu için Teacher'a 403 dönerdi (docs/04-permissions.md).
// canManage: öğretmen artık KENDİ öğrencisinin velisini ve kursunu ekleyebiliyor (J1).
// isAdmin ise yalnızca geri alınamaz işlemler için ayrı tutuluyor - kurs kaldırma bir
// silmedir ve yöneticide kalır (J2'nin aynı gerekçesi).
export function StudentDetail({
  student,
  isAdmin,
  canManage = isAdmin,
  onDelete,
}: { student: Student; isAdmin: boolean; canManage?: boolean; onDelete?: () => void }) {
  const studentId = student.id;
  const [showGuardianForm, setShowGuardianForm] = useState(false);
  const [showEnrollmentForm, setShowEnrollmentForm] = useState(false);
  const [editingStudent, setEditingStudent] = useState(false);
  const [editingGuardian, setEditingGuardian] = useState<StudentGuardianLink | null>(null);
  const [programEnrollmentId, setProgramEnrollmentId] = useState<string | null>(null);
  const { data: guardians } = useStudentGuardians(canManage ? studentId : "");
  const { data: enrollments } = useEnrollments(studentId);
  // "Her hafta Pazartesi 18:00 piyano" - kurs satırının altında haftalık program görünür ve
  // buradan taşınabilir (kullanıcı isteği). Liste kurs kaydına göre eşlenir.
  const { data: lessonSeries } = useStudentLessonSeries(studentId);
  const { data: teachers } = useTeachers();
  const { data: instruments } = useInstruments();
  const updateStudent = useUpdateStudent();
  const activeEnrollments = enrollments?.filter((enrollment) => enrollment.status === "Active") ?? [];
  const fullName = `${student.firstName} ${student.lastName}`;
  const selectedProgramEnrollment = activeEnrollments.find((enrollment) => enrollment.id === programEnrollmentId) ?? null;
  const selectedProgramSeries = lessonSeries?.find((item) => item.enrollmentId === programEnrollmentId) ?? null;
  const selectedProgramInstrument = instruments?.find((item) => item.id === selectedProgramEnrollment?.instrumentId);
  const selectedProgramTeacher = teachers?.find((item) => item.id === selectedProgramEnrollment?.teacherId);

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
    <div className="space-y-3 border-t border-[var(--line)] bg-[var(--surface-muted)]/30 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <p className="text-meta mr-auto">{ageOf(student.birthDate) !== null && `${ageOf(student.birthDate)} yaş · `}<span className={student.status === "Active" ? "font-bold text-[var(--success-strong)]" : "font-bold"}>{student.status === "Active" ? "Aktif öğrenci" : "Pasif öğrenci"}</span>{isAdmin && student.siblingDiscount && <span className="ml-1.5 rounded-full bg-[var(--success-soft)] px-1.5 py-0.5 text-[.75rem] font-bold text-[var(--success-strong)]">Kardeş indirimi</span>}</p>
        <Link href={`/dashboard/progress?studentId=${studentId}`} className="btn btn-quiet h-9 min-h-9 text-xs"><Icon name="activity" className="h-3.5 w-3.5" /> Gelişim</Link>
        {canManage && <RowMenu label={`${fullName} için işlemler`}>{(close) => <>
          {activeEnrollments.length > 0 && <RowMenuItem icon="calendar" onClick={() => { close(); setProgramEnrollmentId(activeEnrollments.length === 1 ? activeEnrollments[0]!.id : "__choose__"); }}>Ders programı</RowMenuItem>}
          <RowMenuItem icon="pencil" onClick={() => { close(); setEditingStudent(true); }}>Bilgileri düzenle</RowMenuItem>
          <RowMenuItem icon="plus" onClick={() => { close(); setShowEnrollmentForm(true); }}>Kurs ekle</RowMenuItem>
          <RowMenuItem icon="students" onClick={() => { close(); setShowGuardianForm(true); }}>Veli ekle</RowMenuItem>
          <RowMenuItem icon={student.status === "Active" ? "x" : "check"} tone={student.status === "Active" ? "danger" : "default"} onClick={() => { close(); toggleStatus(); }}>{student.status === "Active" ? "Pasife al" : "Yeniden aktif et"}</RowMenuItem>
          {onDelete && <RowMenuItem icon="x" tone="danger" onClick={() => { close(); onDelete(); }}>{isAdmin ? "Kalıcı olarak sil" : "Silme talebi oluştur"}</RowMenuItem>}
        </>}</RowMenu>}
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        {canManage && (
          <section className="app-card relative">
            <div className="flex min-h-12 items-center gap-2 border-b border-[var(--line)] px-3 py-2">
              <div className="min-w-0 flex-1"><h3 className="text-sm font-bold">Veliler</h3><p className="text-meta">{guardians?.length ?? 0} kayıtlı kişi</p></div>
              <button type="button" onClick={() => setShowGuardianForm(true)} className="pressable min-h-9 rounded-lg px-2.5 text-xs font-bold text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">+ Veli</button>
            </div>
            <ul className="divide-y divide-[var(--line)]">
              {guardians?.map((guardian) => (
                <li key={guardian.id} className="flex min-h-12 items-center gap-2 px-3 py-2">
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-bold">{guardian.firstName} {guardian.lastName}</span>
                    <span className="text-meta mt-0.5 block truncate">
                      {guardian.phoneNumber}{guardian.relationship && ` · ${guardian.relationship}`}
                    </span>
                  </span>
                  {guardian.isPrimary && (
                    <span className="shrink-0 rounded-full bg-[var(--success-soft)] px-2 py-0.5 text-[.75rem] font-bold text-[var(--success-strong)]">Birincil</span>
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
              {guardians?.length === 0 && <li className="text-meta grid min-h-20 place-items-center px-4 py-6 text-center">Henüz veli eklenmemiş.</li>}
            </ul>
          </section>
        )}

        <section className="app-card relative">
          <div className="flex min-h-12 items-center gap-2 border-b border-[var(--line)] px-3 py-2">
            <div className="min-w-0 flex-1"><h3 className="text-sm font-bold">Kurslar</h3><p className="text-meta">{activeEnrollments.length} aktif kayıt</p></div>
            {canManage && <button type="button" onClick={() => setShowEnrollmentForm(true)} className="pressable min-h-9 rounded-lg px-2.5 text-xs font-bold text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">+ Kurs</button>}
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
                  series={lessonSeries?.find((item) => item.enrollmentId === enrollment.id) ?? null}
                  isAdmin={isAdmin}
                  canManage={canManage}
                />
              );
            })}
            {!activeEnrollments.length && <li className="text-meta grid min-h-20 place-items-center px-4 py-6 text-center">Henüz aktif kurs yok.</li>}
          </ul>
        </section>
      </div>

      {canManage && (
        <>
          <AddGuardianForm studentId={studentId} open={showGuardianForm} onClose={() => setShowGuardianForm(false)} />
          <Modal open={showEnrollmentForm} title="Kurs ekle" description="Öğretmen ve enstrümanı seçerek bu öğrenciye bağla." onClose={() => setShowEnrollmentForm(false)} size="sm">
            <AddEnrollmentForm studentId={studentId} teachers={teachers ?? []} instruments={instruments ?? []} onClose={() => setShowEnrollmentForm(false)} />
          </Modal>
          <Modal open={editingStudent} title="Öğrenciyi düzenle" onClose={() => setEditingStudent(false)} size="sm">
            <EditStudentForm student={student} isAdmin={isAdmin} onClose={() => setEditingStudent(false)} />
          </Modal>
          {editingGuardian && (
            <Modal open title="Veliyi düzenle" onClose={() => setEditingGuardian(null)} size="sm">
              <EditGuardianForm studentId={studentId} guardian={editingGuardian} onClose={() => setEditingGuardian(null)} />
            </Modal>
          )}
          {programEnrollmentId && (
            <Modal open title="Ders programı" description={fullName} onClose={() => setProgramEnrollmentId(null)}>
              {programEnrollmentId === "__choose__" ? (
                <div className="space-y-3">
                  <p className="text-meta">Düzenlemek istediğin dersi seç.</p>
                  <div className="grid gap-2 sm:grid-cols-2">
                    {activeEnrollments.map((enrollment) => {
                      const instrument = instruments?.find((item) => item.id === enrollment.instrumentId);
                      const teacher = teachers?.find((item) => item.id === enrollment.teacherId);
                      return (
                        <button key={enrollment.id} type="button" onClick={() => setProgramEnrollmentId(enrollment.id)} className="pressable flex min-h-14 items-center gap-3 rounded-xl border border-[var(--line)] bg-white px-3 text-left hover:border-[var(--brand)]">
                          <Icon name="calendar" className="h-4 w-4 shrink-0 text-[var(--brand)]" />
                          <span><span className="block text-sm font-bold">{instrument?.name ?? "Ders"}</span><span className="text-meta mt-0.5 block">{teacher ? `${teacher.firstName} ${teacher.lastName}` : "Öğretmen"}</span></span>
                        </button>
                      );
                    })}
                  </div>
                </div>
              ) : selectedProgramEnrollment ? (
                <div className="space-y-3">
                  <div className="rounded-xl bg-[var(--brand-soft)] px-3 py-2.5 text-sm font-bold text-[var(--brand-strong)]">
                    {selectedProgramInstrument?.name ?? "Ders"}{selectedProgramTeacher ? ` · ${selectedProgramTeacher.firstName} ${selectedProgramTeacher.lastName}` : ""}
                  </div>
                  <ScheduleForm studentId={studentId} enrollmentId={selectedProgramEnrollment.id} series={selectedProgramSeries} onClose={() => setProgramEnrollmentId(null)} />
                </div>
              ) : (
                <p className="text-meta">Aktif kurs bulunamadı.</p>
              )}
            </Modal>
          )}
        </>
      )}
    </div>
  );
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

function EditStudentForm({ student, isAdmin, onClose }: { student: Student; isAdmin: boolean; onClose: () => void }) {
  const updateStudent = useUpdateStudent();
  const [firstName, setFirstName] = useState(student.firstName);
  const [lastName, setLastName] = useState(student.lastName);
  const [birthDate, setBirthDate] = useState(student.birthDate);
  const [siblingDiscount, setSiblingDiscount] = useState(student.siblingDiscount);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      // Alan yalnızca Admin oturumunda gönderilir; öğretmenin künye düzenlemesi
      // kardeş indirimine dokunmamalı (sunucu da aynı kuralı ayrıca uygular).
      await updateStudent.mutateAsync({
        studentId: student.id, firstName, lastName, birthDate, status: student.status,
        ...(isAdmin ? { siblingDiscount } : {}),
      });
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

      {/* Kardeş indirimi artık çıkarım değil, açık bir karar (docs/10-decisions.md H13).
          Ortak veli üzerinden tahmin etmek iki yönde de yanılıyordu: aynı veli iki kez
          kaydedildiyse gerçek kardeşler indirim alamıyor, bir veli akraba çocuğuna da
          bağlıysa kardeş olmayan alıyordu. */}
      {isAdmin && (
        <label className="flex cursor-pointer items-start gap-2.5 rounded-xl border border-[var(--line)] bg-white p-3">
          <input
            type="checkbox"
            checked={siblingDiscount}
            onChange={(event) => setSiblingDiscount(event.target.checked)}
            className="mt-0.5 h-4 w-4 shrink-0 accent-[var(--brand)]"
          />
          <span className="min-w-0">
            <span className="block text-sm font-bold">Kardeş indirimi uygulansın</span>
            <span className="text-meta mt-0.5 block leading-snug">
              Bu öğrencinin okulda kardeşi var. İşaretliyken aidata kardeş indirimi uygulanır.
              Oran Aidat yönetimi &gt; Fiyat politikası ekranından gelir; 2 kurs indirimiyle
              birlikte geçerliyse yüksek olan uygulanır, ikisi toplanmaz.
            </span>
          </span>
        </label>
      )}

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

function EnrollmentRow({ studentId, enrollmentId, teacherId, instrumentName, teacherName, series, isAdmin, canManage }: { studentId: string; enrollmentId: string; teacherId: string; instrumentName: string; teacherName: string; series: StudentLessonSeries | null; isAdmin: boolean; canManage: boolean }) {
  const endEnrollment = useEndEnrollment(studentId);
  const router = useRouter();
  const [confirming, setConfirming] = useState(false);
  const [editingSchedule, setEditingSchedule] = useState(false);
  const [error, setError] = useState<string | null>(null);

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
    <li className="px-3 py-2.5">
      <div className="flex items-center gap-2">
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-bold">{instrumentName}</span>
          <span className="text-meta mt-0.5 block truncate">{teacherName}</span>
        </span>
        {(isAdmin || canManage) && (
          <RowMenu label={`${instrumentName} kursu için işlemler`}>
            {(close) => (
              <>
                {canManage && (
                  <RowMenuItem icon="calendar" onClick={() => { close(); setEditingSchedule(true); }}>
                    {series ? "Ders saatini değiştir" : "Ders programı gir"}
                  </RowMenuItem>
                )}
                {isAdmin && (
                  <RowMenuItem icon="teachers" onClick={() => { close(); router.push(`/dashboard/teachers#teacher-${teacherId}`); }}>
                    Öğretmene git
                  </RowMenuItem>
                )}
                {isAdmin && (
                  <RowMenuItem icon="x" tone="danger" onClick={() => { close(); setConfirming(true); }}>
                    Kursu sonlandır
                  </RowMenuItem>
                )}
              </>
            )}
          </RowMenu>
        )}
      </div>

      {/* Haftalık program satırı: kullanıcı isteği "öğrenci altında program görünebilir
          olmalı, buradan ders saatini güncelleyebilmeliyim". Program yoksa bunu da açıkça
          söylüyoruz - sessiz boşluk "ders yok mu, veri mi gelmedi" sorusunu doğuruyordu. */}
      <div className="mt-1.5 flex flex-wrap items-center gap-2">
        {series ? (
          <span className="inline-flex items-center gap-1.5 text-[.75rem] font-semibold text-[var(--brand-strong)]">
            <Icon name="calendar" className="h-3.5 w-3.5" />
            {formatWeeklySchedule(series)}
          </span>
        ) : (
          <span className="text-meta">Haftalık program girilmemiş.</span>
        )}
        {canManage && (
          <button type="button" onClick={() => setEditingSchedule(true)} className="pressable min-h-8 rounded-lg px-2 text-[.75rem] font-bold text-[var(--brand-strong)] hover:bg-[var(--brand-soft)]">
            {series ? "Değiştir" : "Program gir"}
          </button>
        )}
      </div>

      {editingSchedule && (
        <Modal
          open
          title={series ? "Ders saatini değiştir" : "Ders programı gir"}
          description={`${instrumentName} · ${teacherName}`}
          onClose={() => setEditingSchedule(false)}
          size="sm"
        >
          <ScheduleForm
            studentId={studentId}
            enrollmentId={enrollmentId}
            series={series}
            onClose={() => setEditingSchedule(false)}
          />
        </Modal>
      )}
      {confirming && (
        <div className="mt-2 flex flex-wrap items-center justify-between gap-2 rounded-lg bg-[var(--danger-soft)] px-3 py-2">
          <p className="text-[.75rem] font-semibold text-[var(--danger-strong)]">Kurs sonlandırılsın mı? Gelecekteki dersler durdurulur.</p>
          <span className="flex gap-1.5">
            <button type="button" onClick={() => setConfirming(false)} className="pressable min-h-8 rounded-lg bg-white px-2.5 text-[.75rem] font-bold">Vazgeç</button>
            <button type="button" onClick={remove} disabled={endEnrollment.isPending} className="pressable min-h-8 rounded-lg bg-[var(--danger)] px-2.5 text-[.75rem] font-bold text-white disabled:opacity-50">
              {endEnrollment.isPending ? "Sonlandırılıyor…" : "Sonlandır"}
            </button>
          </span>
        </div>
      )}
      {error && <p role="alert" className="mt-2 text-[.75rem] font-semibold text-[var(--danger-strong)]">{error}</p>}
    </li>
  );
}


// Hem ilk programı girmek hem de var olanı taşımak için tek form. Taşıma sunucuda
// "eskisini kapat + yenisini aç" olarak işlenir (LessonSeriesFeatures.RescheduleAsync);
// geçmiş dersler eski programa bağlı kalır, bu yüzden "geçerlilik başlangıcı" alanı
// gerçek bir tarih seçimi - varsayılanı bugün, yani "bu haftadan itibaren".
const SCHEDULE_DAY_KEYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
const SCHEDULE_DAY_SHORT: Record<string, string> = { Monday: "Pzt", Tuesday: "Sal", Wednesday: "Çar", Thursday: "Per", Friday: "Cum", Saturday: "Cmt", Sunday: "Paz" };
const SCHEDULE_DURATIONS = [30, 45, 60];

function ScheduleForm({ studentId, enrollmentId, series, onClose }: { studentId: string; enrollmentId: string; series: StudentLessonSeries | null; onClose: () => void }) {
  const createSeries = useCreateLessonSeries();
  const rescheduleSeries = useRescheduleLessonSeries(studentId);
  const [dayOfWeek, setDayOfWeek] = useState(series?.dayOfWeek ?? "Monday");
  const [startTime, setStartTime] = useState(series ? series.startTime.slice(0, 5) : "18:00");
  const [durationMinutes, setDurationMinutes] = useState(series?.durationMinutes ?? 45);
  const [effectiveFrom, setEffectiveFrom] = useState(() => new Date().toISOString().slice(0, 10));
  // Mevcut program hazır seçeneklerden biri olmayan bir süre taşıyabilir (örn. 50 dk).
  // Listeye eklenmezse select eşleşme bulamayıp ilk seçeneği ("30 dakika") gösterir ve
  // kullanıcı hiç dokunmadığı bir alanda yanlış bilgi okur.
  const durationOptions = SCHEDULE_DURATIONS.includes(durationMinutes)
    ? SCHEDULE_DURATIONS
    : [...SCHEDULE_DURATIONS, durationMinutes].sort((a, b) => a - b);
  const [error, setError] = useState<string | null>(null);
  const [summary, setSummary] = useState<string | null>(null);
  const pending = createSeries.isPending || rescheduleSeries.isPending;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSummary(null);
    try {
      const body = { dayOfWeek, startTime: `${startTime}:00`, durationMinutes, effectiveFrom };
      const result = series
        ? await rescheduleSeries.mutateAsync({ seriesId: series.id, ...body })
        : await createSeries.mutateAsync({ enrollmentId, ...body });
      const skipped = result.generation.skippedHolidays.length + result.generation.skippedTeacherTimeOff.length;
      setSummary(`${result.generation.created} ders takvime yerleştirildi${skipped ? ` · ${skipped} uygun olmayan tarih atlandı` : ""}.`);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Ders programı kaydedilemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Ders süresi
          <select value={durationMinutes} onChange={(event) => setDurationMinutes(Number(event.target.value))} className="field text-sm">
            {durationOptions.map((minutes) => <option key={minutes} value={minutes}>{minutes} dakika</option>)}
          </select>
        </label>
        <label className="form-label">{series ? "Bu tarihten itibaren" : "Başlangıç tarihi"}
          <input type="date" required value={effectiveFrom} onChange={(event) => setEffectiveFrom(event.target.value)} className="field text-sm" />
        </label>
      </div>

      <div className="grid min-w-0 gap-3 rounded-2xl border border-[var(--line)] bg-[var(--surface-muted)] p-4 sm:grid-cols-[minmax(0,2fr)_minmax(10rem,1fr)]">
        <fieldset className="min-w-0">
          <legend className="form-label">Gün</legend>
          <div className="mt-1 grid grid-cols-4 gap-1.5 sm:grid-cols-7" role="radiogroup" aria-label="Ders günü">
            {SCHEDULE_DAY_KEYS.map((day) => {
              const active = dayOfWeek === day;
              return <button key={day} type="button" role="radio" aria-checked={active} onClick={() => setDayOfWeek(day)} className={`pressable grid min-h-11 min-w-0 place-items-center rounded-xl border text-xs font-bold ${active ? "border-[var(--brand)] bg-[var(--brand)] text-white shadow-sm" : "border-[var(--line)] bg-white text-[var(--foreground)] hover:border-[var(--brand)]"}`}>{SCHEDULE_DAY_SHORT[day]}</button>;
            })}
          </div>
        </fieldset>
        <label className="form-label min-w-0">Saat
          <input type="time" required value={startTime} onChange={(event) => setStartTime(event.target.value)} className="field min-w-0 text-base tabular-nums" />
        </label>
      </div>

      <p className="text-meta">
        {series
          ? `Şu anki program: ${formatWeeklySchedule(series)}. Seçilen tarihten önceki dersler olduğu gibi kalır, sonrasındakiler yeni saate taşınır.`
          : "Seçilen gün ve saat her hafta tekrarlanır; öğretmen ve öğrenci çakışmaları otomatik kontrol edilir."}
      </p>

      {error && <FormMessage tone="error">{error}</FormMessage>}
      {summary && <FormMessage tone="success">{summary}</FormMessage>}

      {summary ? (
        <div className="flex justify-end border-t border-[var(--line)] pt-4">
          <button type="button" onClick={onClose} className="btn btn-primary">Kapat</button>
        </div>
      ) : (
        <FormActions onCancel={onClose} submitLabel={series ? "Saati güncelle" : "Programı kaydet"} pending={pending} pendingLabel="Kaydediliyor…" />
      )}
    </form>
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
