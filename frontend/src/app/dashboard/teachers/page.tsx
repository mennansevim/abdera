"use client";

import { useMemo, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/use-auth";
import {
  useCreateEnrollment,
  useCreateStudentForTeacher,
  useCreateTeacher,
  useInstruments,
  useStudents,
  useTeacherOverviews,
  useTeachers,
  type Student,
  type Teacher,
  type TeacherStudentEnrollment,
} from "@/lib/people";

export default function TeachersPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const { data: teachers, isLoading } = useTeachers();
  const { data: overviews, isLoading: overviewsLoading } = useTeacherOverviews(isAdmin);
  const { data: instruments } = useInstruments();
  const { data: students } = useStudents();
  const teacherRows = isAdmin
    ? (overviews ?? []).map((overview) => ({ teacher: overview.teacher, teacherStudents: overview.students }))
    : (teachers ?? []).map((teacher) => ({ teacher, teacherStudents: [] as TeacherStudentEnrollment[] }));

  return (
    <div className="space-y-5">
      <header>
        <p className="text-micro text-[var(--brand-strong)]">Eğitim kadrosu</p>
        <h1 className="text-display mt-1 font-serif italic">Öğretmenler</h1>
        <p className="text-meta mt-2">Öğretmenlerin öğrencilerini gör ve yeni kayıt oluştur.</p>
      </header>

      {isAdmin && <CreateTeacherForm instruments={instruments ?? []} />}

      <div className="app-card overflow-hidden">
        {(isLoading || (isAdmin && overviewsLoading)) && <div className="space-y-3 p-4">{Array.from({ length: 4 }, (_, index) => <div key={index} className="skeleton h-16 rounded-xl" />)}</div>}
        {!isLoading && teacherRows.length === 0 && <p className="p-6 text-center text-sm text-[var(--muted)]">Henüz öğretmen yok.</p>}
        <ul className="divide-y divide-[var(--line)]">
          {teacherRows.map(({ teacher, teacherStudents }) => <TeacherRow key={teacher.id} teacher={teacher} instruments={instruments ?? []} students={students ?? []} teacherStudents={teacherStudents} isAdmin={isAdmin} />)}
        </ul>
      </div>
    </div>
  );
}

function TeacherRow({ teacher, instruments, students, teacherStudents, isAdmin }: { teacher: Teacher; instruments: { id: string; name: string }[]; students: Student[]; teacherStudents: TeacherStudentEnrollment[]; isAdmin: boolean }) {
  const [showStudents, setShowStudents] = useState(false);
  const [showAddForm, setShowAddForm] = useState(false);
  const [formMode, setFormMode] = useState<"new" | "existing">("new");
  const [studentId, setStudentId] = useState("");
  const [instrumentId, setInstrumentId] = useState(teacher.instrumentIds[0] ?? "");
  const [startedAt, setStartedAt] = useState(() => new Date().toISOString().slice(0, 10));
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [birthDate, setBirthDate] = useState("");
  const [message, setMessage] = useState<{ tone: "success" | "error"; text: string } | null>(null);
  const createEnrollment = useCreateEnrollment(studentId);
  const createStudent = useCreateStudentForTeacher(teacher.id);
  const teacherInstruments = instruments.filter((instrument) => teacher.instrumentIds.includes(instrument.id));
  const groupedStudents = useMemo(() => {
    const grouped = new Map<string, { id: string; name: string; courses: string[] }>();
    for (const enrollment of teacherStudents) {
      const existing = grouped.get(enrollment.studentId);
      if (existing) {
        if (!existing.courses.includes(enrollment.instrumentName)) existing.courses.push(enrollment.instrumentName);
      } else {
        grouped.set(enrollment.studentId, {
          id: enrollment.studentId,
          name: `${enrollment.firstName} ${enrollment.lastName}`,
          courses: [enrollment.instrumentName],
        });
      }
    }
    return [...grouped.values()];
  }, [teacherStudents]);

  async function addCourse(event: FormEvent) {
    event.preventDefault();
    setMessage(null);
    try {
      if (formMode === "new") {
        await createStudent.mutateAsync({ firstName, lastName, birthDate, instrumentId, startedAt });
        setFirstName("");
        setLastName("");
        setBirthDate("");
        setMessage({ tone: "success", text: "Yeni öğrenci oluşturuldu ve öğretmene eklendi." });
      } else {
        await createEnrollment.mutateAsync({ teacherId: teacher.id, instrumentId, startedAt });
        setStudentId("");
        setMessage({ tone: "success", text: "Kayıtlı öğrenci öğretmene eklendi." });
      }
      setShowStudents(true);
      // Kayıt başarılı olduğunda ekleme alanı açık kalmasın; öğrenci sayısı ve liste
      // zaten sorgu yenilenince güncellenir. Kullanıcı isterse + Öğrenci ekle ile tekrar açar.
      setShowAddForm(false);
      setMessage(null);
    } catch (err) {
      setMessage({ tone: "error", text: err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci eklenemedi." });
    }
  }

  return <li id={`teacher-${teacher.id}`} className="scroll-mt-24 target:bg-[var(--brand-soft)]">
    <div className="flex min-h-16 flex-wrap items-center justify-between gap-3 px-4 py-3.5">
      <button type="button" onClick={() => setShowStudents((visible) => !visible)} aria-expanded={isAdmin ? showStudents : undefined} disabled={!isAdmin} className="pressable flex min-w-0 flex-1 items-center gap-3 rounded-xl text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand)] disabled:cursor-default disabled:active:transform-none">
        <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-xs font-bold text-[var(--brand-strong)]">{teacher.firstName[0]}{teacher.lastName[0]}</span>
        <span className="min-w-0 flex-1">
          <span className="block text-sm font-bold">{teacher.firstName} {teacher.lastName}</span>
          <span className="text-meta mt-0.5 block truncate">{teacherInstruments.map((instrument) => instrument.name).join(", ")}</span>
        </span>
        {isAdmin && <span className="inline-flex shrink-0 items-center gap-1.5 rounded-full bg-[var(--surface-muted)] px-2.5 py-1 text-[.66rem] font-bold text-[var(--foreground)]"><Icon name="students" className="h-3.5 w-3.5 text-[var(--brand)]" />{groupedStudents.length} öğrenci<Icon name="chevron" className={`h-3 w-3 text-[var(--muted)] transition-transform ${showStudents ? "rotate-90" : ""}`} /></span>}
      </button>
      {isAdmin && teacher.status === "Active" && <button type="button" onClick={() => { setShowAddForm((visible) => !visible); setMessage(null); }} aria-expanded={showAddForm} className={`pressable min-h-9 rounded-lg border px-3 text-xs font-bold ${showAddForm ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "border-[var(--line)] bg-white text-[var(--brand)] hover:border-[var(--brand)]"}`}>{showAddForm ? "Kapat" : "+ Öğrenci ekle"}</button>}
      <span className="shrink-0 text-right">
        <span className={`inline-flex rounded-full px-2 py-0.5 text-[.62rem] font-bold ${teacher.status === "Active" ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{teacher.status === "Active" ? "Aktif" : "Pasif"}</span>
        <span className="text-meta mt-0.5 block">{teacher.hasLoginAccount ? "Giriş hesabı var" : "Giriş hesabı yok"}</span>
      </span>
    </div>
    {showStudents && isAdmin && <div className="border-t border-[var(--line)] bg-white px-4 py-3">
      {groupedStudents.length > 0 ? <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{groupedStudents.map((student) => <li key={student.id} className="flex items-center gap-2.5 rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/45 px-3 py-2.5"><span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-white text-[.62rem] font-bold text-[var(--brand-strong)]">{student.name.split(" ").map((part) => part[0]).slice(0, 2).join("")}</span><span className="min-w-0"><span className="block truncate text-xs font-bold">{student.name}</span><span className="text-meta mt-0.5 block truncate">{student.courses.join(", ")}</span></span></li>)}</ul> : <p className="py-2 text-xs text-[var(--muted)]">Bu öğretmene bağlı aktif öğrenci yok.</p>}
    </div>}
    {showAddForm && <form onSubmit={addCourse} className="space-y-3 border-t border-[var(--line)] bg-[var(--surface-muted)] p-3 sm:p-4">
      <div className="inline-flex rounded-xl border border-[var(--line)] bg-white p-1" aria-label="Öğrenci ekleme yöntemi"><button type="button" onClick={() => { setFormMode("new"); setMessage(null); }} aria-pressed={formMode === "new"} className={`pressable min-h-8 rounded-lg px-3 text-[.66rem] font-bold ${formMode === "new" ? "bg-[var(--brand)] text-white" : "text-[var(--muted)]"}`}>Yeni öğrenci</button><button type="button" onClick={() => { setFormMode("existing"); setMessage(null); }} aria-pressed={formMode === "existing"} className={`pressable min-h-8 rounded-lg px-3 text-[.66rem] font-bold ${formMode === "existing" ? "bg-[var(--brand)] text-white" : "text-[var(--muted)]"}`}>Kayıtlı öğrenci</button></div>
      <div className={`grid gap-2 sm:items-end ${formMode === "new" ? "sm:grid-cols-[1fr_1fr_10rem_1fr_10rem_auto]" : "sm:grid-cols-[minmax(12rem,1.5fr)_1fr_10rem_auto]"}`}>
        {formMode === "new" ? <><label className="space-y-1 text-[.66rem] font-bold text-[var(--muted)]">Ad<input value={firstName} onChange={(event) => setFirstName(event.target.value)} required className="field min-h-10 text-xs" /></label><label className="space-y-1 text-[.66rem] font-bold text-[var(--muted)]">Soyad<input value={lastName} onChange={(event) => setLastName(event.target.value)} required className="field min-h-10 text-xs" /></label><label className="space-y-1 text-[.66rem] font-bold text-[var(--muted)]">Doğum tarihi<input type="date" value={birthDate} onChange={(event) => setBirthDate(event.target.value)} required className="field min-h-10 text-xs" /></label></> : <label className="space-y-1 text-[.66rem] font-bold text-[var(--muted)]">Öğrenci<select value={studentId} onChange={(event) => setStudentId(event.target.value)} required className="field min-h-10 text-xs"><option value="">Öğrenci seç</option>{students.filter((student) => student.status === "Active").map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}</select></label>}
        <label className="space-y-1 text-[.66rem] font-bold text-[var(--muted)]">Enstrüman<select value={instrumentId} onChange={(event) => setInstrumentId(event.target.value)} required className="field min-h-10 text-xs">{teacherInstruments.map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}</select></label>
        <label className="space-y-1 text-[.66rem] font-bold text-[var(--muted)]">Başlangıç<input type="date" value={startedAt} onChange={(event) => setStartedAt(event.target.value)} required className="field min-h-10 text-xs" /></label>
        <button type="submit" disabled={createEnrollment.isPending || createStudent.isPending || !instrumentId || (formMode === "existing" && !studentId)} className="pressable min-h-10 rounded-xl bg-[var(--brand)] px-4 text-xs font-bold text-white disabled:opacity-50">{createEnrollment.isPending || createStudent.isPending ? "Ekleniyor…" : formMode === "new" ? "Oluştur ve ekle" : "Öğretmene ekle"}</button>
      </div>
      {message && <p role="status" className={`text-xs font-semibold ${message.tone === "success" ? "text-[var(--success-strong)]" : "text-[var(--danger-strong)]"}`}>{message.text}</p>}
    </form>}
  </li>;
}

function CreateTeacherForm({ instruments }: { instruments: { id: string; name: string }[] }) {
  const createTeacher = useCreateTeacher();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [selectedInstruments, setSelectedInstruments] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [temporaryPassword, setTemporaryPassword] = useState<string | null>(null);

  function toggleInstrument(id: string) {
    setSelectedInstruments((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setTemporaryPassword(null);
    try {
      const result = await createTeacher.mutateAsync({
        firstName,
        lastName,
        instrumentIds: selectedInstruments,
        email: email || undefined,
      });
      setFirstName("");
      setLastName("");
      setEmail("");
      setSelectedInstruments([]);
      if (result.temporaryPassword) {
        setTemporaryPassword(result.temporaryPassword);
      }
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğretmen eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="app-card space-y-3 p-4">
      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1.5">
          <label className="text-[.7rem] font-semibold text-[var(--muted)]">Ad</label>
          <input value={firstName} onChange={(e) => setFirstName(e.target.value)} required className="field min-h-11 w-32 text-sm" />
        </div>
        <div className="space-y-1.5">
          <label className="text-[.7rem] font-semibold text-[var(--muted)]">Soyad</label>
          <input value={lastName} onChange={(e) => setLastName(e.target.value)} required className="field min-h-11 w-32 text-sm" />
        </div>
        <div className="space-y-1.5">
          <label className="text-[.7rem] font-semibold text-[var(--muted)]">E-posta (giriş hesabı için, opsiyonel)</label>
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className="field min-h-11 w-56 text-sm" />
        </div>
      </div>

      <div className="space-y-1.5">
        <label className="text-[.7rem] font-semibold text-[var(--muted)]">Enstrümanlar</label>
        <div className="flex flex-wrap gap-2">
          {instruments.map((i) => {
            const checked = selectedInstruments.includes(i.id);
            return (
              <button
                key={i.id}
                type="button"
                onClick={() => toggleInstrument(i.id)}
                aria-pressed={checked}
                className={`pressable min-h-9 rounded-full border px-3 text-xs font-semibold ${checked ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand)]" : "border-[var(--line)] bg-white text-[var(--muted)] hover:border-[#e0c39d]"}`}
              >
                {i.name}
              </button>
            );
          })}
        </div>
      </div>

      <button type="submit" disabled={createTeacher.isPending} className="pressable min-h-11 rounded-xl bg-[var(--brand)] px-4 text-sm font-bold text-white shadow-[0_6px_14px_rgba(217,102,42,.2)] hover:bg-[var(--brand-strong)] disabled:opacity-50">
        {createTeacher.isPending ? "Ekleniyor…" : "Öğretmen ekle"}
      </button>

      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] px-3 py-2.5 text-xs font-medium text-[var(--danger-strong)]">{error}</p>}

      {temporaryPassword && (
        <p className="rounded-xl border border-[var(--warning)]/40 bg-[var(--warning-soft)] p-3 text-xs font-medium text-[var(--warning-strong)]">
          Geçici şifre: <code className="font-mono font-bold">{temporaryPassword}</code> — bunu öğretmene sözlü/WhatsApp ile
          ilet, bir daha gösterilmeyecek.
        </p>
      )}
    </form>
  );
}
