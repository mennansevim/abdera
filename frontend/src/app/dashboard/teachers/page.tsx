"use client";

import { useMemo, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { AddButton, AdminGate, FormActions, FormMessage, Modal, Notice, PageHeader, SearchInput } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useCreateTeacherAvailability, useDeleteTeacherAvailability, useTeacherAvailability, type TeacherAvailability } from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import {
  useCreateEnrollment,
  useCreateStudentForTeacher,
  useCreateTeacher,
  useInstruments,
  useStudents,
  useTeacherOverviews,
  useTeachers,
  useUpdateTeacher,
  type Student,
  type Teacher,
  type TeacherStatus,
  type TeacherStudentEnrollment,
} from "@/lib/people";

// Takvimin/telafi asistanının kullandığı gün sırası ve TR etiketleri (calendar/page.tsx ile
// aynı) - Pazartesi'den başlar, backend'in DayOfWeek string'leriyle (Sunday/Monday/...) eşleşir.
const AVAILABILITY_DAYS: Array<{ key: string; label: string }> = [
  { key: "Monday", label: "Pzt" },
  { key: "Tuesday", label: "Sal" },
  { key: "Wednesday", label: "Çar" },
  { key: "Thursday", label: "Per" },
  { key: "Friday", label: "Cum" },
  { key: "Saturday", label: "Cmt" },
  { key: "Sunday", label: "Paz" },
];
// Okulun varsayılan çalışma penceresi - takvim ızgarasının da varsayılanı (week-grid-layout.ts
// DEFAULT_START_HOUR/END_HOUR). Bir gün "açılırken" bu aralık kullanılır; ayrıntılı saat
// ayarı gerekiyorsa admin bunu Ders Programı'ndaki müsaitlik akışından güncelleyebilir.
const DEFAULT_AVAILABILITY_START = "09:00";
const DEFAULT_AVAILABILITY_END = "19:00";

export default function TeachersPage() {
  return <AdminGate><TeachersPageContent /></AdminGate>;
}

// Öğretmen dizini (isim + branş) tamamen Admin'e özel - bir öğretmenin okuldaki diğer
// öğretmenleri gezme ihtiyacı yok (kullanıcı isteği). Kenar çubuğundan zaten kaldırıldı
// (app-header.tsx); AdminGate doğrudan adres yazılmasına karşı ikinci katman.
function TeachersPageContent() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const { data: teachers, isLoading } = useTeachers();
  const { data: overviews, isLoading: overviewsLoading } = useTeacherOverviews(isAdmin);
  const { data: instruments } = useInstruments();
  const { data: students } = useStudents();
  const [showCreate, setShowCreate] = useState(false);
  const [search, setSearch] = useState("");
  // Geçici şifre yalnızca oluşturma yanıtında bir kez döner - pencere kapandıktan sonra da
  // görünmesi gerektiği için sayfa seviyesinde tutulur, admin kapatana kadar durur.
  const [temporaryPassword, setTemporaryPassword] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  function announce(text: string) {
    setNotice(text);
    window.setTimeout(() => setNotice((current) => (current === text ? null : current)), 4000);
  }
  // Liste ada göre sıralanır ve arama kutusuyla daraltılır: okul büyüdükçe (E2E/demo
  // kayıtlarıyla birlikte) sıralamasız bir listede kaydı gözle bulmak zorlaşıyordu.
  // Arama enstrümanı da kapsar - "keman öğretmeni kimdi" en sık sorulan soru.
  const teacherRows = useMemo(() => {
    const rows = isAdmin
      ? (overviews ?? []).map((overview) => ({ teacher: overview.teacher, teacherStudents: overview.students }))
      : (teachers ?? []).map((teacher) => ({ teacher, teacherStudents: [] as TeacherStudentEnrollment[] }));
    const query = search.trim().toLocaleLowerCase("tr-TR");
    const instrumentNames = new Map((instruments ?? []).map((instrument) => [instrument.id, instrument.name]));
    return rows
      .filter(({ teacher }) => {
        if (!query) return true;
        const haystack = [
          `${teacher.firstName} ${teacher.lastName}`,
          ...teacher.instrumentIds.map((id) => instrumentNames.get(id) ?? ""),
        ].join(" ").toLocaleLowerCase("tr-TR");
        return haystack.includes(query);
      })
      .sort((a, b) => `${a.teacher.firstName} ${a.teacher.lastName}`.localeCompare(`${b.teacher.firstName} ${b.teacher.lastName}`, "tr-TR"));
  }, [isAdmin, overviews, teachers, instruments, search]);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Öğretmenler"
        description="Öğretmenler, verdikleri dersler ve öğrencileri."
        actions={<>
          <SearchInput value={search} onChange={setSearch} label="Öğretmen ara" placeholder="Ad veya enstrüman ara…" />
          {isAdmin && <AddButton label="Öğretmen ekle" onClick={() => setShowCreate(true)} />}
        </>}
      />

      {notice && <Notice onDismiss={() => setNotice(null)}>{notice}</Notice>}

      {temporaryPassword && (
        <div className="app-card flex flex-wrap items-center justify-between gap-3 border-[var(--warning)]/40 bg-[var(--warning-soft)] p-3.5">
          <p className="text-xs font-semibold text-[var(--warning-strong)]">
            Geçici şifre: <code className="font-mono font-bold">{temporaryPassword}</code> — öğretmene ilet, bir daha gösterilmeyecek.
          </p>
          <button type="button" onClick={() => setTemporaryPassword(null)} className="btn btn-quiet">Anladım</button>
        </div>
      )}

      <div className="app-card overflow-hidden">
        {(isLoading || (isAdmin && overviewsLoading)) && <div className="space-y-3 p-4">{Array.from({ length: 4 }, (_, index) => <div key={index} className="skeleton h-16 rounded-xl" />)}</div>}
        {!isLoading && teacherRows.length === 0 && <p className="p-6 text-center text-sm text-[var(--muted)]">{search ? `"${search}" ile eşleşen öğretmen yok.` : "Henüz öğretmen yok."}</p>}
        <ul className="divide-y divide-[var(--line)]">
          {teacherRows.map(({ teacher, teacherStudents }) => <TeacherRow key={teacher.id} teacher={teacher} instruments={instruments ?? []} students={students ?? []} teacherStudents={teacherStudents} isAdmin={isAdmin} />)}
        </ul>
      </div>

      {isAdmin && (
        <Modal open={showCreate} title="Öğretmen ekle" onClose={() => setShowCreate(false)} size="sm">
          <CreateTeacherForm
            instruments={instruments ?? []}
            onClose={() => setShowCreate(false)}
            onCreated={(password, name) => { setTemporaryPassword(password); setShowCreate(false); announce(`${name} eklendi.`); }}
          />
        </Modal>
      )}
    </div>
  );
}

function TeacherRow({ teacher, instruments, students, teacherStudents, isAdmin }: { teacher: Teacher; instruments: { id: string; name: string }[]; students: Student[]; teacherStudents: TeacherStudentEnrollment[]; isAdmin: boolean }) {
  const [showStudents, setShowStudents] = useState(false);
  const [showAddForm, setShowAddForm] = useState(false);
  const [showEditForm, setShowEditForm] = useState(false);
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

  return <li id={`teacher-${teacher.id}`} className="scroll-mt-24 target:bg-[var(--brand-soft)]">
    <div className="flex min-h-16 items-center gap-3 px-4 py-3">
      <button type="button" onClick={() => setShowStudents((visible) => !visible)} aria-expanded={isAdmin ? showStudents : undefined} disabled={!isAdmin} className="pressable flex min-w-0 flex-1 items-center gap-3 rounded-xl text-left disabled:cursor-default disabled:active:transform-none">
        <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-[var(--brand-soft)] text-xs font-bold text-[var(--brand-strong)]">{teacher.firstName[0]}{teacher.lastName[0]}</span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-bold">{teacher.firstName} {teacher.lastName}</span>
          <span className="text-meta mt-0.5 block truncate">
            {teacherInstruments.map((instrument) => instrument.name).join(", ") || "Enstrüman atanmadı"}
            {isAdmin && ` · ${groupedStudents.length} öğrenci`}
            {teacher.status === "Active" ? "" : " · pasif"}
          </span>
        </span>
        {isAdmin && <Icon name="chevron" className={`h-4 w-4 shrink-0 text-[var(--muted)] transition-transform ${showStudents ? "rotate-90" : ""}`} />}
      </button>
      {isAdmin && (
        <button
          type="button"
          onClick={() => setShowEditForm(true)}
          aria-label={`${teacher.firstName} ${teacher.lastName} bilgilerini düzenle`}
          title="Düzenle"
          className="icon-btn icon-btn-quiet shrink-0"
        >
          <Icon name="pencil" className="h-4 w-4" />
        </button>
      )}
      {isAdmin && teacher.status === "Active" && (
        <AddButton label={`${teacher.firstName} ${teacher.lastName} öğretmenine öğrenci ekle`} tone="quiet" onClick={() => setShowAddForm(true)} />
      )}
    </div>

    {showStudents && isAdmin && <div className="border-t border-[var(--line)] bg-white px-4 py-3">
      {groupedStudents.length > 0 ? <ul className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{groupedStudents.map((student) => <li key={student.id} className="flex items-center gap-2.5 rounded-xl border border-[var(--line)] px-3 py-2.5"><span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-[var(--surface-muted)] text-[.62rem] font-bold text-[var(--brand-strong)]">{student.name.split(" ").map((part) => part[0]).slice(0, 2).join("")}</span><span className="min-w-0"><span className="block truncate text-xs font-bold">{student.name}</span><span className="text-meta mt-0.5 block truncate">{student.courses.join(", ")}</span></span></li>)}</ul> : <p className="text-meta py-2">Bu öğretmene bağlı aktif öğrenci yok.</p>}
      <TeacherAvailabilityDays teacherId={teacher.id} enabled={showStudents} />
    </div>}

    <Modal
      open={showAddForm}
      title="Öğrenci ekle"
      description={`${teacher.firstName} ${teacher.lastName} · ${teacherInstruments.map((instrument) => instrument.name).join(", ")}`}
      onClose={() => setShowAddForm(false)}
      size="sm"
    >
      <AddStudentToTeacherForm
        teacher={teacher}
        teacherInstruments={teacherInstruments}
        students={students}
        onClose={() => setShowAddForm(false)}
        onAdded={() => { setShowAddForm(false); setShowStudents(true); }}
      />
    </Modal>

    <Modal open={showEditForm} title="Öğretmeni düzenle" onClose={() => setShowEditForm(false)} size="sm">
      <EditTeacherForm teacher={teacher} instruments={instruments} onClose={() => setShowEditForm(false)} />
    </Modal>
  </li>;
}

// "Öğretmen ayarlarında kaç enstrüman çalabileceği seçilmeli" - oluşturma sırasında zaten
// seçilebiliyordu ama sonradan DEĞİŞTİRİLEMİYORDU (backend UpdateAsync zaten vardı,
// arayüzde hiç kullanılmıyordu). Ad/soyad ve aktif/pasif durumu da aynı formda - üçü de
// aynı PATCH isteğine gidiyor.
function EditTeacherForm({ teacher, instruments, onClose }: { teacher: Teacher; instruments: { id: string; name: string }[]; onClose: () => void }) {
  const updateTeacher = useUpdateTeacher(teacher.id);
  const [firstName, setFirstName] = useState(teacher.firstName);
  const [lastName, setLastName] = useState(teacher.lastName);
  const [status, setStatus] = useState<TeacherStatus>(teacher.status);
  const [selectedInstruments, setSelectedInstruments] = useState<string[]>(teacher.instrumentIds);
  const [error, setError] = useState<string | null>(null);

  function toggleInstrument(id: string) {
    setSelectedInstruments((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await updateTeacher.mutateAsync({ firstName, lastName, status, instrumentIds: selectedInstruments });
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğretmen güncellenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Ad<input value={firstName} onChange={(e) => setFirstName(e.target.value)} required className="field text-sm" /></label>
        <label className="form-label">Soyad<input value={lastName} onChange={(e) => setLastName(e.target.value)} required className="field text-sm" /></label>
      </div>

      <div className="inline-flex rounded-xl border border-[var(--line)] p-1" role="group" aria-label="Durum">
        {([["Active", "Aktif"], ["Inactive", "Pasif"]] as const).map(([value, label]) => (
          <button key={value} type="button" onClick={() => setStatus(value)} aria-pressed={status === value} className={`pressable min-h-8 rounded-lg px-3 text-xs font-bold ${status === value ? "bg-[var(--brand)] text-white" : "text-[var(--muted)]"}`}>{label}</button>
        ))}
      </div>

      <div>
        <p className="form-label">Enstrümanlar</p>
        <div className="mt-1.5 flex flex-wrap gap-2">
          {instruments.map((instrument) => {
            const checked = selectedInstruments.includes(instrument.id);
            return (
              <button
                key={instrument.id}
                type="button"
                onClick={() => toggleInstrument(instrument.id)}
                aria-pressed={checked}
                className={`pressable min-h-9 rounded-full border px-3 text-xs font-semibold ${checked ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "border-[var(--line)] bg-white text-[var(--muted)] hover:border-[var(--brand)]"}`}
              >
                {instrument.name}
              </button>
            );
          })}
        </div>
      </div>

      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Kaydet" pending={updateTeacher.isPending} pendingLabel="Kaydediliyor…" disabled={selectedInstruments.length === 0} />
    </form>
  );
}

// Öğretmene öğrenci bağlama: ya yeni bir öğrenci kaydı açılır ya da kayıtlı bir öğrenci
// seçilir. İki yol tek bir formda, üstteki iki sekmeyle ayrılır.
function AddStudentToTeacherForm({
  teacher,
  teacherInstruments,
  students,
  onClose,
  onAdded,
}: {
  teacher: Teacher;
  teacherInstruments: { id: string; name: string }[];
  students: Student[];
  onClose: () => void;
  onAdded: () => void;
}) {
  const [mode, setMode] = useState<"new" | "existing">("new");
  const [studentId, setStudentId] = useState("");
  const [instrumentId, setInstrumentId] = useState(teacherInstruments[0]?.id ?? "");
  const [startedAt, setStartedAt] = useState(() => new Date().toISOString().slice(0, 10));
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [birthDate, setBirthDate] = useState("");
  const [error, setError] = useState<string | null>(null);
  const createEnrollment = useCreateEnrollment(studentId);
  const createStudent = useCreateStudentForTeacher(teacher.id);
  const pending = createEnrollment.isPending || createStudent.isPending;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      if (mode === "new") {
        await createStudent.mutateAsync({ firstName, lastName, birthDate, instrumentId, startedAt });
      } else {
        await createEnrollment.mutateAsync({ teacherId: teacher.id, instrumentId, startedAt });
      }
      onAdded();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="inline-flex rounded-xl border border-[var(--line)] p-1" role="group" aria-label="Öğrenci ekleme yöntemi">
        {([["new", "Yeni öğrenci"], ["existing", "Kayıtlı öğrenci"]] as const).map(([value, label]) => (
          <button key={value} type="button" onClick={() => { setMode(value); setError(null); }} aria-pressed={mode === value} className={`pressable min-h-8 rounded-lg px-3 text-xs font-bold ${mode === value ? "bg-[var(--brand)] text-white" : "text-[var(--muted)]"}`}>{label}</button>
        ))}
      </div>

      {mode === "new" ? (
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="form-label">Ad<input value={firstName} onChange={(event) => setFirstName(event.target.value)} required className="field text-sm" /></label>
          <label className="form-label">Soyad<input value={lastName} onChange={(event) => setLastName(event.target.value)} required className="field text-sm" /></label>
          <label className="form-label sm:col-span-2">Doğum tarihi<input type="date" value={birthDate} onChange={(event) => setBirthDate(event.target.value)} required className="field text-sm" /></label>
        </div>
      ) : (
        <label className="form-label">Öğrenci
          <select value={studentId} onChange={(event) => setStudentId(event.target.value)} required className="field text-sm">
            <option value="">Öğrenci seç</option>
            {students.filter((student) => student.status === "Active").map((student) => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}
          </select>
        </label>
      )}

      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Enstrüman
          <select value={instrumentId} onChange={(event) => setInstrumentId(event.target.value)} required className="field text-sm">
            {teacherInstruments.map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}
          </select>
        </label>
        <label className="form-label">Başlangıç<input type="date" value={startedAt} onChange={(event) => setStartedAt(event.target.value)} required className="field text-sm" /></label>
      </div>

      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions
        onCancel={onClose}
        submitLabel={mode === "new" ? "Oluştur ve ekle" : "Öğretmene ekle"}
        pending={pending}
        pendingLabel="Ekleniyor…"
        disabled={!instrumentId || (mode === "existing" && !studentId)}
      />
    </form>
  );
}

// "Öğretmeni tıklayınca açılan sekme içinde uygun günler yeşil olsun, tek tıkla seçilebilsin"
// - telafi/akıllı zamanlama önerileri (lib/smart-scheduling.ts) bu günleri kullanır: bir gün
// için hiç kayıt yoksa öğretmen o gün açık sayılır (varsayılan), ama en az bir gün
// işaretlenince yalnızca işaretli günler açık kalır - bu yüzden burada seçilen günler kadar
// önemli olan, HİÇBİR gün seçilmemiş olma durumunun ne anlama geldiğini de göstermek.
function TeacherAvailabilityDays({ teacherId, enabled }: { teacherId: string; enabled: boolean }) {
  const { data: availability, isLoading } = useTeacherAvailability(teacherId, { enabled });
  const createAvailability = useCreateTeacherAvailability(teacherId);
  const deleteAvailability = useDeleteTeacherAvailability(teacherId);
  const [pendingDay, setPendingDay] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const rowsByDay = new Map<string, TeacherAvailability[]>();
  for (const row of availability ?? []) {
    rowsByDay.set(row.dayOfWeek, [...(rowsByDay.get(row.dayOfWeek) ?? []), row]);
  }

  async function toggleDay(day: string) {
    setError(null);
    setPendingDay(day);
    try {
      const existing = rowsByDay.get(day) ?? [];
      if (existing.length) {
        // Normalde günde tek bir pencere olur; birden fazlaysa (elle/eski veriden) hepsini
        // kaldır - arayüz bir günü "açık/kapalı" olarak modelliyor, birden fazla aralık değil.
        await Promise.all(existing.map((row) => deleteAvailability.mutateAsync(row.id)));
      } else {
        await createAvailability.mutateAsync({ dayOfWeek: day, startTime: DEFAULT_AVAILABILITY_START, endTime: DEFAULT_AVAILABILITY_END });
      }
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Uygunluk güncellenemedi.");
    } finally {
      setPendingDay(null);
    }
  }

  return (
    <div className="mt-3 border-t border-[var(--line)] pt-3">
      <p className="text-meta font-bold">Uygun günler</p>
      {isLoading ? (
        <div className="mt-2 flex gap-1.5">{AVAILABILITY_DAYS.map((day) => <div key={day.key} className="skeleton h-9 w-14 rounded-lg" />)}</div>
      ) : (
        <div className="mt-2 flex flex-wrap gap-1.5" role="group" aria-label="Uygun günler">
          {AVAILABILITY_DAYS.map((day) => {
            const active = rowsByDay.has(day.key);
            const busy = pendingDay === day.key;
            return (
              <button
                key={day.key}
                type="button"
                onClick={() => void toggleDay(day.key)}
                disabled={busy}
                aria-pressed={active}
                title={active ? `${day.label}: uygun (${DEFAULT_AVAILABILITY_START}–${DEFAULT_AVAILABILITY_END}) - kapatmak için tıkla` : `${day.label}: uygun değil - açmak için tıkla`}
                className={`pressable min-h-9 w-14 rounded-lg border text-xs font-bold disabled:opacity-50 ${active ? "border-[var(--success-strong)] bg-[var(--success-soft)] text-[var(--success-strong)]" : "border-[var(--line)] bg-white text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand)]"}`}
              >
                {day.label}
              </button>
            );
          })}
        </div>
      )}
      <p className="text-meta mt-2">
        {!isLoading && !rowsByDay.size
          ? "Hiçbir gün seçilmedi - öğretmen şu an her gün uygun sayılıyor."
          : "Telafi ve uygun slot önerileri bu günleri kullanır."}
      </p>
      {error && <p role="alert" className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
    </div>
  );
}

function CreateTeacherForm({ instruments, onClose, onCreated }: { instruments: { id: string; name: string }[]; onClose: () => void; onCreated: (temporaryPassword: string | null, name: string) => void }) {
  const createTeacher = useCreateTeacher();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [selectedInstruments, setSelectedInstruments] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);

  function toggleInstrument(id: string) {
    setSelectedInstruments((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const result = await createTeacher.mutateAsync({
        firstName,
        lastName,
        instrumentIds: selectedInstruments,
        email: email || undefined,
      });
      onCreated(result.temporaryPassword ?? null, `${firstName} ${lastName}`);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğretmen eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Ad<input value={firstName} onChange={(e) => setFirstName(e.target.value)} required className="field text-sm" /></label>
        <label className="form-label">Soyad<input value={lastName} onChange={(e) => setLastName(e.target.value)} required className="field text-sm" /></label>
      </div>
      <label className="form-label">E-posta <span className="font-medium">· giriş hesabı için, opsiyonel</span>
        <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className="field text-sm" />
      </label>

      <div>
        <p className="form-label">Enstrümanlar</p>
        <div className="mt-1.5 flex flex-wrap gap-2">
          {instruments.map((instrument) => {
            const checked = selectedInstruments.includes(instrument.id);
            return (
              <button
                key={instrument.id}
                type="button"
                onClick={() => toggleInstrument(instrument.id)}
                aria-pressed={checked}
                className={`pressable min-h-9 rounded-full border px-3 text-xs font-semibold ${checked ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "border-[var(--line)] bg-white text-[var(--muted)] hover:border-[var(--brand)]"}`}
              >
                {instrument.name}
              </button>
            );
          })}
        </div>
      </div>

      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Öğretmen ekle" pending={createTeacher.isPending} pendingLabel="Ekleniyor…" />
    </form>
  );
}
