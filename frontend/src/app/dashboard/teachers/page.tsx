"use client";

import { useMemo, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { AddButton, AdminGate, FormActions, FormMessage, Modal, Notice, PageHeader, RowMenu, RowMenuItem, SearchInput } from "@/components/ui";
import { DeleteTeacherDialog } from "@/components/delete-person-dialog";
import { TeacherAvailabilityDays } from "@/components/teacher-availability-days";
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
  useResetTeacherPassword,
  useUpdateTeacher,
  type Student,
  type Teacher,
  type TeacherStatus,
  type TeacherStudentEnrollment,
} from "@/lib/people";

export default function TeachersPage() {
  return <AdminGate><TeachersPageContent /></AdminGate>;
}

// Öğretmen dizini (isim + branş) tamamen Admin'e özel - bir öğretmenin okuldaki diğer
// öğretmenleri gezme ihtiyacı yok (kullanıcı isteği). Kenar çubuğundan zaten kaldırıldı
// (app-header.tsx); AdminGate doğrudan adres yazılmasına karşı ikinci katman.
function TeachersPageContent() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const { data: teachers, isLoading, isError: teachersError, refetch: refetchTeachers, isFetching: teachersFetching } = useTeachers();
  const { data: overviews, isLoading: overviewsLoading, isError: overviewsError, refetch: refetchOverviews, isFetching: overviewsFetching } = useTeacherOverviews(isAdmin);
  const { data: instruments } = useInstruments();
  const { data: students } = useStudents();
  const [showCreate, setShowCreate] = useState(false);
  const [search, setSearch] = useState("");
  const [instrumentId, setInstrumentId] = useState("");
  const [status, setStatus] = useState<"all" | TeacherStatus>("Active");
  const [expandedTeacherId, setExpandedTeacherId] = useState<string | null>(null);
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
  const allTeacherRows = useMemo(() => {
    const rows = isAdmin
      ? (overviews ?? []).map((overview) => ({ teacher: overview.teacher, teacherStudents: overview.students }))
      : (teachers ?? []).map((teacher) => ({ teacher, teacherStudents: [] as TeacherStudentEnrollment[] }));
    return rows.sort((a, b) => `${a.teacher.firstName} ${a.teacher.lastName}`.localeCompare(`${b.teacher.firstName} ${b.teacher.lastName}`, "tr-TR"));
  }, [isAdmin, overviews, teachers]);

  const teacherRows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase("tr-TR");
    const instrumentNames = new Map((instruments ?? []).map((instrument) => [instrument.id, instrument.name]));
    return allTeacherRows
      .filter(({ teacher }) => {
        if (status !== "all" && teacher.status !== status) return false;
        if (instrumentId && !teacher.instrumentIds.includes(instrumentId)) return false;
        if (!query) return true;
        const haystack = [
          `${teacher.firstName} ${teacher.lastName}`,
          ...teacher.instrumentIds.map((id) => instrumentNames.get(id) ?? ""),
        ].join(" ").toLocaleLowerCase("tr-TR");
        return haystack.includes(query);
      });
  }, [allTeacherRows, instrumentId, instruments, search, status]);

  const activeTeacherCount = allTeacherRows.filter(({ teacher }) => teacher.status === "Active").length;
  const studentCount = new Set(allTeacherRows.flatMap(({ teacherStudents }) => teacherStudents.map((student) => student.studentId))).size;
  const loading = isLoading || (isAdmin && overviewsLoading);
  const isError = isAdmin ? overviewsError : teachersError;
  const isFetching = teachersFetching || (isAdmin && overviewsFetching);

  function retry() {
    void refetchTeachers();
    if (isAdmin) void refetchOverviews();
  }
  const hasFilters = Boolean(search || instrumentId || status !== "Active");

  function clearFilters() {
    setSearch("");
    setInstrumentId("");
    setStatus("Active");
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Öğretmenler"
        description={loading ? "Öğretmenler ve öğrenci dağılımları." : `${activeTeacherCount} aktif öğretmen · ${studentCount} öğrenci`}
        actions={<>
          <SearchInput value={search} onChange={setSearch} label="Öğretmen ara" placeholder="Ad veya enstrüman ara…" />
          {isAdmin && <AddButton label="Öğretmen ekle" onClick={() => setShowCreate(true)} />}
        </>}
      />

      {notice && <Notice onDismiss={() => setNotice(null)}>{notice}</Notice>}

      {temporaryPassword && (
        <div className="app-card flex flex-wrap items-center justify-between gap-3 border-[var(--warning)]/40 bg-[var(--warning-soft)] p-4">
          <p className="text-xs font-semibold text-[var(--warning-strong)]">
            Geçici şifre: <code className="font-mono font-bold">{temporaryPassword}</code> — öğretmene ilet, bir daha gösterilmeyecek.
          </p>
          <button type="button" onClick={() => setTemporaryPassword(null)} className="btn btn-quiet">Anladım</button>
        </div>
      )}

      <div className="app-card relative">
        <div className="flex flex-wrap items-center gap-2 rounded-t-[1.3rem] border-b border-[var(--line)] bg-[var(--surface-muted)]/30 px-3 py-2">
          <p className="text-meta mr-auto"><strong className="text-[var(--foreground)]">{teacherRows.length}</strong> öğretmen gösteriliyor</p>
          <label className="relative min-w-44 flex-1 sm:flex-none">
            <span className="sr-only">Enstrümana göre filtrele</span>
            <select value={instrumentId} onChange={(event) => setInstrumentId(event.target.value)} className="field min-h-11 appearance-none py-1.5 pr-8 text-xs font-semibold" aria-label="Enstrümana göre filtrele">
              <option value="">Tüm enstrümanlar</option>
              {(instruments ?? []).map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}
            </select>
            <Icon name="chevron" className="pointer-events-none absolute right-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 rotate-90 text-[var(--muted)]" />
          </label>
          <label className="relative min-w-28 flex-1 sm:flex-none">
            <span className="sr-only">Duruma göre filtrele</span>
            <select value={status} onChange={(event) => setStatus(event.target.value as "all" | TeacherStatus)} className="field min-h-11 appearance-none py-1.5 pr-8 text-xs font-semibold" aria-label="Duruma göre filtrele">
              <option value="Active">Aktif</option>
              <option value="Inactive">Pasif</option>
              <option value="all">Tümü</option>
            </select>
            <Icon name="chevron" className="pointer-events-none absolute right-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 rotate-90 text-[var(--muted)]" />
          </label>
        </div>
        {!loading && !isError && teacherRows.length > 0 && <div className="hidden grid-cols-[minmax(0,1.25fr)_minmax(0,1fr)_7rem_6rem_2.75rem] items-center gap-3 border-b border-[var(--line)] px-3 py-2 text-micro text-[var(--muted)] md:grid"><span>Öğretmen</span><span>Branş</span><span className="text-right">Öğrenci</span><span className="text-center">Durum</span><span /></div>}
        {loading && <div className="space-y-2 p-3">{Array.from({ length: 5 }, (_, index) => <div key={index} className="skeleton h-13 rounded-lg" />)}</div>}
        {!loading && isError && <div className="grid min-h-48 place-items-center p-6 text-center"><div><p className="text-sm font-bold">Öğretmenler yüklenemedi</p><p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p><button type="button" onClick={retry} disabled={isFetching} className="btn btn-quiet mt-3 disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button></div></div>}
        {!loading && !isError && teacherRows.length === 0 && <div className="p-6 text-center text-sm text-[var(--muted)]"><p>{hasFilters ? "Seçili filtrelerle eşleşen öğretmen yok." : "Henüz öğretmen yok."}</p>{hasFilters && <button type="button" onClick={clearFilters} className="pressable mt-2 text-xs font-bold text-[var(--brand)] hover:underline">Filtreleri temizle</button>}</div>}
        {!loading && !isError && <ul className="divide-y divide-[var(--line)]">
          {teacherRows.map(({ teacher, teacherStudents }) => <TeacherRow key={teacher.id} teacher={teacher} instruments={instruments ?? []} students={students ?? []} teacherStudents={teacherStudents} isAdmin={isAdmin} expanded={expandedTeacherId === teacher.id} onToggle={() => setExpandedTeacherId((current) => current === teacher.id ? null : teacher.id)} />)}
        </ul>}
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

function TeacherRow({ teacher, instruments, students, teacherStudents, isAdmin, expanded, onToggle }: { teacher: Teacher; instruments: { id: string; name: string }[]; students: Student[]; teacherStudents: TeacherStudentEnrollment[]; isAdmin: boolean; expanded: boolean; onToggle: () => void }) {
  const [studentSearch, setStudentSearch] = useState("");
  const [detailTab, setDetailTab] = useState<"students" | "availability">("students");
  const [showAddForm, setShowAddForm] = useState(false);
  const [showEditForm, setShowEditForm] = useState(false);
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
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
    return [...grouped.values()].sort((a, b) => a.name.localeCompare(b.name, "tr-TR"));
  }, [teacherStudents]);
  const visibleStudents = useMemo(() => {
    const query = studentSearch.trim().toLocaleLowerCase("tr-TR");
    if (!query) return groupedStudents;
    return groupedStudents.filter((student) => `${student.name} ${student.courses.join(" ")}`.toLocaleLowerCase("tr-TR").includes(query));
  }, [groupedStudents, studentSearch]);

  return <li id={`teacher-${teacher.id}`} className="scroll-mt-24 target:bg-[var(--brand-soft)]">
    <div className="grid min-h-14 grid-cols-[minmax(0,1fr)_2.75rem] items-center gap-1 px-2 md:grid-cols-[minmax(0,1fr)_2.75rem]">
      <button type="button" onClick={onToggle} aria-expanded={isAdmin ? expanded : undefined} disabled={!isAdmin} className="pressable grid min-h-13 min-w-0 grid-cols-[minmax(0,1fr)] items-center gap-3 rounded-lg px-1.5 text-left hover:bg-[var(--surface-muted)] disabled:cursor-default disabled:hover:bg-transparent disabled:active:transform-none md:grid-cols-[minmax(0,1.25fr)_minmax(0,1fr)_7rem_6rem]">
        <span className="min-w-0">
          <span className="flex min-w-0 items-center gap-1.5"><span className="truncate text-sm font-bold">{teacher.firstName} {teacher.lastName}</span>{isAdmin && <Icon name="chevron" className={`h-3.5 w-3.5 shrink-0 text-[var(--muted)] transition-transform ${expanded ? "rotate-90" : ""}`} />}</span>
          <span className="text-meta mt-0.5 block truncate md:hidden">{teacherInstruments.map((instrument) => instrument.name).join(", ") || "Enstrüman atanmadı"} · {groupedStudents.length} öğrenci{teacher.status === "Inactive" ? " · Pasif" : ""}</span>
        </span>
        <span className="text-meta hidden truncate md:block">{teacherInstruments.map((instrument) => instrument.name).join(", ") || "Enstrüman atanmadı"}</span>
        <span className="text-meta hidden text-right tabular-nums md:block"><strong className="text-sm text-[var(--foreground)]">{groupedStudents.length}</strong></span>
        <span className={`hidden justify-self-center rounded-full px-2 py-1 text-[.75rem] font-bold md:block ${teacher.status === "Active" ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{teacher.status === "Active" ? "Aktif" : "Pasif"}</span>
      </button>
      {isAdmin && <RowMenu label={`${teacher.firstName} ${teacher.lastName} işlemleri`}>{(close) => <>
        {teacher.status === "Active" && <RowMenuItem icon="plus" onClick={() => { close(); setShowAddForm(true); }}>Öğrenci ekle</RowMenuItem>}
        <RowMenuItem icon="pencil" onClick={() => { close(); setShowEditForm(true); }}>Bilgileri düzenle</RowMenuItem>
        <RowMenuItem icon="x" tone="danger" onClick={() => { close(); setShowDeleteDialog(true); }}>Kalıcı olarak sil</RowMenuItem>
      </>}</RowMenu>}
    </div>

    {showDeleteDialog && (
      <DeleteTeacherDialog
        teacherId={teacher.id}
        teacherName={`${teacher.firstName} ${teacher.lastName}`}
        onClose={() => setShowDeleteDialog(false)}
      />
    )}

    {expanded && isAdmin && <div className="border-t border-[var(--line)] bg-[var(--surface-muted)]/30 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <div className="inline-flex rounded-xl border border-[var(--line)] bg-white p-1" role="group" aria-label="Öğretmen ayrıntıları">
          <button type="button" aria-pressed={detailTab === "students"} onClick={() => setDetailTab("students")} className={`pressable min-h-11 rounded-lg px-3 text-xs font-bold ${detailTab === "students" ? "bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "text-[var(--muted)]"}`}>Öğrenciler <span className="ml-1 tabular-nums opacity-70">{groupedStudents.length}</span></button>
          <button type="button" aria-pressed={detailTab === "availability"} onClick={() => setDetailTab("availability")} className={`pressable min-h-11 rounded-lg px-3 text-xs font-bold ${detailTab === "availability" ? "bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "text-[var(--muted)]"}`}>Uygunluk</button>
        </div>
        {detailTab === "students" && groupedStudents.length > 6 && <label className="relative ml-auto w-full sm:w-56"><span className="sr-only">Bu öğretmenin öğrencilerinde ara</span><Icon name="search" className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[var(--muted)]" /><input value={studentSearch} onChange={(event) => setStudentSearch(event.target.value)} placeholder="Öğrenci veya branş ara…" className="field min-h-11 pl-9 py-1.5 text-xs" /></label>}
      </div>
      {detailTab === "students" && <div className="mt-3 overflow-hidden rounded-xl border border-[var(--line)] bg-white">
        {visibleStudents.length > 0 ? <><div className="grid grid-cols-[2rem_minmax(0,1fr)_minmax(7rem,.7fr)] items-center gap-2 border-b border-[var(--line)] bg-[var(--surface-muted)]/45 px-3 py-2 text-micro text-[var(--muted)]"><span>#</span><span>Öğrenci</span><span>Branş</span></div><ol className="max-h-80 divide-y divide-[var(--line)] overflow-y-auto">{visibleStudents.map((student, index) => <li key={student.id} className="grid min-h-10 grid-cols-[2rem_minmax(0,1fr)_minmax(7rem,.7fr)] items-center gap-2 px-3 py-1.5 text-xs hover:bg-[var(--surface-muted)]/35"><span className="text-[.75rem] tabular-nums text-[var(--muted)]">{index + 1}</span><span className="truncate font-semibold">{student.name}</span><span className="flex min-w-0 flex-wrap gap-1">{student.courses.map((course) => <span key={course} className="rounded-full bg-[var(--surface-muted)] px-2 py-0.5 text-[.75rem] font-semibold text-[var(--muted)]">{course}</span>)}</span></li>)}</ol></> : groupedStudents.length ? <p className="p-4 text-center text-xs text-[var(--muted)]">“{studentSearch}” ile eşleşen öğrenci yok.</p> : <p className="p-4 text-center text-xs text-[var(--muted)]">Bu öğretmene bağlı aktif öğrenci yok.</p>}
      </div>}
      {detailTab === "availability" && <div className="mt-3 rounded-xl border border-[var(--line)] bg-white p-3"><TeacherAvailabilityDays teacherId={teacher.id} enabled={expanded && detailTab === "availability"} embedded /></div>}
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
        onAdded={() => { setShowAddForm(false); setDetailTab("students"); if (!expanded) onToggle(); }}
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
  const resetPassword = useResetTeacherPassword(teacher.id);
  const [firstName, setFirstName] = useState(teacher.firstName);
  const [lastName, setLastName] = useState(teacher.lastName);
  const [status, setStatus] = useState<TeacherStatus>(teacher.status);
  const [selectedInstruments, setSelectedInstruments] = useState<string[]>(teacher.instrumentIds);
  const [error, setError] = useState<string | null>(null);
  const [temporaryPassword, setTemporaryPassword] = useState<string | null>(null);

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

  async function handlePasswordReset() {
    setError(null);
    setTemporaryPassword(null);
    try {
      const result = await resetPassword.mutateAsync();
      setTemporaryPassword(result.temporaryPassword);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Şifre sıfırlanamadı.");
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
          <button key={value} type="button" onClick={() => setStatus(value)} aria-pressed={status === value} className={`pressable min-h-11 rounded-lg px-3 text-xs font-bold ${status === value ? "bg-[var(--brand)] text-white" : "text-[var(--muted)]"}`}>{label}</button>
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
                className={`pressable min-h-11 rounded-full border px-3 text-xs font-semibold ${checked ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "border-[var(--line)] bg-white text-[var(--muted)] hover:border-[var(--brand)]"}`}
              >
                {instrument.name}
              </button>
            );
          })}
        </div>
      </div>

      {teacher.hasLoginAccount && (
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-[var(--line)] pt-3.5">
          {temporaryPassword ? (
            <div className="w-full rounded-xl border border-[var(--warning)]/40 bg-[var(--warning-soft)] p-3" role="status" aria-live="polite">
              <p className="text-xs font-semibold text-[var(--warning-strong)]">
                Yeni geçici şifre: <code className="font-mono font-bold">{temporaryPassword}</code>
              </p>
              <p className="mt-1 text-[.75rem] text-[var(--warning-strong)]">Öğretmene şimdi ilet; pencere kapandıktan sonra tekrar gösterilmez.</p>
            </div>
          ) : (
            <>
              <div>
                <p className="text-xs font-bold">Giriş şifresi</p>
                <p className="text-meta mt-0.5">Mevcut şifre ve açık oturumlar geçersiz olur.</p>
              </div>
              <button
                type="button"
                onClick={handlePasswordReset}
                disabled={resetPassword.isPending}
                className="btn btn-quiet"
              >
                {resetPassword.isPending ? "Sıfırlanıyor…" : "Şifreyi sıfırla"}
              </button>
            </>
          )}
        </div>
      )}

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
          <button key={value} type="button" onClick={() => { setMode(value); setError(null); }} aria-pressed={mode === value} className={`pressable min-h-11 rounded-lg px-3 text-xs font-bold ${mode === value ? "bg-[var(--brand)] text-white" : "text-[var(--muted)]"}`}>{label}</button>
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
                className={`pressable min-h-11 rounded-full border px-3 text-xs font-semibold ${checked ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "border-[var(--line)] bg-white text-[var(--muted)] hover:border-[var(--brand)]"}`}
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
