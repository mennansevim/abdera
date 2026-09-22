"use client";

import { useMemo, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { Icon } from "@/components/icons";
import { AddButton, FormActions, FormMessage, Modal, Notice, PageHeader, SearchInput } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/use-auth";
import { useSessionState } from "@/lib/use-session-state";
import { useCreateStudent, useCreateStudentForTeacher, useInstruments, useStudentOverviews, type Student, type StudentStatus } from "@/lib/people";
import { DeleteStudentDialog, RequestStudentDeletionDialog } from "@/components/delete-person-dialog";
import { StudentDetail } from "./student-detail";

// docs/04-permissions.md: öğrenci oluşturma/veli/kayıt yönetimi yalnızca Admin - Teacher
// yalnızca kendisine atanmış öğrencileri görür, formlar 403 vermesin diye gizlenir.
export default function StudentsPage() {
  const router = useRouter();
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  // Öğretmen artık kendi öğrencisini ekleyip düzenleyebiliyor (J1). Listede zaten
  // yalnızca kendine atanmış öğrenciler görünüyor, o yüzden görünen her satır "kendi".
  const isTeacher = me?.role === "Teacher";
  const canManage = isAdmin || isTeacher;
  // "İçine girmeden anlayabilelim" - liste her satırda kurs adlarını da taşıyan tek bir
  // toplu istekten (overview) besleniyor, N+1 sorgu açmadan.
  const { data: overviews, isLoading, isError, isFetching, refetch } = useStudentOverviews();
  const [deleting, setDeleting] = useState<{ id: string; name: string } | null>(null);
  const [expandedId, setExpandedId] = useSessionState<string | null>("abdera:students:expanded", null);
  const [showCreate, setShowCreate] = useState(false);
  const [search, setSearch] = useSessionState("abdera:students:search", "");
  const [instrumentId, setInstrumentId] = useSessionState("abdera:students:instrument", "");
  const [status, setStatus] = useSessionState<"all" | StudentStatus>("abdera:students:status", "Active");
  const [notice, setNotice] = useState<string | null>(null);

  function announce(text: string) {
    setNotice(text);
    window.setTimeout(() => setNotice((current) => (current === text ? null : current)), 4000);
  }

  const instrumentOptions = useMemo(() => {
    const byId = new Map<string, string>();
    for (const overview of overviews ?? []) {
      for (const instrument of overview.instruments) byId.set(instrument.instrumentId, instrument.instrumentName);
    }
    return [...byId].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name, "tr-TR"));
  }, [overviews]);

  // Arama ve enstrüman seçimi birlikte çalışır: örneğin "Ece" + "Piyano" yalnızca
  // iki koşulu da karşılayan öğrencileri gösterir. Filtre seçenekleri de listedeki toplu
  // overview yanıtından çıkarılır; bunun için ikinci bir API isteği gerekmez.
  const visibleRows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase("tr-TR");
    return (overviews ?? [])
      .filter(({ student, instruments }) => {
        if (status !== "all" && student.status !== status) return false;
        if (instrumentId && !instruments.some((item) => item.instrumentId === instrumentId)) return false;
        if (!query) return true;
        const haystack = [`${student.firstName} ${student.lastName}`, ...instruments.map((item) => item.instrumentName)]
          .join(" ").toLocaleLowerCase("tr-TR");
        return haystack.includes(query);
      })
      .sort((a, b) => `${a.student.firstName} ${a.student.lastName}`.localeCompare(`${b.student.firstName} ${b.student.lastName}`, "tr-TR"));
  }, [instrumentId, overviews, search, status]);

  const activeStudentCount = (overviews ?? []).filter(({ student }) => student.status === "Active").length;
  const hasFilters = Boolean(search || instrumentId || status !== "Active");

  function clearFilters() {
    setSearch("");
    setInstrumentId("");
    setStatus("Active");
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Öğrenciler"
        description={isLoading ? "Öğrenciler, dersleri ve kayıt bilgileri." : `${activeStudentCount} aktif öğrenci`}
        actions={<>
          <SearchInput value={search} onChange={setSearch} label="Öğrenci ara" placeholder="Ad veya enstrüman ara…" />
          {isTeacher && <AddButton label="Öğrenci ekle" onClick={() => setShowCreate(true)} />}
          {isAdmin && <AddButton label="Öğrenci ekle" onClick={() => router.push("/dashboard/students/new")} />}
        </>}
      />

      {notice && <Notice onDismiss={() => setNotice(null)}>{notice}</Notice>}

      <div className="app-card relative">
        <div className="flex flex-wrap items-center gap-2 rounded-t-[1.3rem] border-b border-[var(--line)] bg-[var(--surface-muted)]/30 px-3 py-2">
          <p className="text-meta mr-auto"><strong className="text-[var(--foreground)]">{visibleRows.length}</strong> öğrenci gösteriliyor</p>
          <label className="relative min-w-44 flex-1 sm:flex-none">
            <span className="sr-only">Enstrümana göre filtrele</span>
            <select value={instrumentId} onChange={(event) => setInstrumentId(event.target.value)} className="field min-h-10 appearance-none py-1.5 pr-8 text-xs font-semibold" aria-label="Enstrümana göre filtrele">
              <option value="">Tüm enstrümanlar</option>
              {instrumentOptions.map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}
            </select>
            <Icon name="chevron" className="pointer-events-none absolute right-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 rotate-90 text-[var(--muted)]" />
          </label>
          <label className="relative min-w-28 flex-1 sm:flex-none">
            <span className="sr-only">Duruma göre filtrele</span>
            <select value={status} onChange={(event) => setStatus(event.target.value as "all" | StudentStatus)} className="field min-h-10 appearance-none py-1.5 pr-8 text-xs font-semibold" aria-label="Duruma göre filtrele">
              <option value="Active">Aktif</option>
              <option value="Inactive">Pasif</option>
              <option value="all">Tümü</option>
            </select>
            <Icon name="chevron" className="pointer-events-none absolute right-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 rotate-90 text-[var(--muted)]" />
          </label>
          {isAdmin && <button type="button" onClick={() => setShowCreate(true)} className="btn btn-quiet min-h-10 h-10 text-xs">Hızlı ekle</button>}
        </div>
        {!isLoading && !isError && visibleRows.length > 0 && <div className="hidden grid-cols-[minmax(0,1.2fr)_9rem_minmax(10rem,.9fr)_6rem] items-center gap-3 border-b border-[var(--line)] px-3 py-2 text-micro text-[var(--muted)] md:grid"><span>Öğrenci</span><span>Doğum tarihi</span><span>Kurslar</span><span className="text-center">Durum</span></div>}
        {isLoading && <div className="space-y-2 p-3">{Array.from({ length: 5 }, (_, index) => <div key={index} className="skeleton h-13 rounded-lg" />)}</div>}
        {!isLoading && isError && <div className="grid min-h-48 place-items-center p-6 text-center"><div><p className="text-sm font-bold">Öğrenciler yüklenemedi</p><p className="text-meta mt-1">Bağlantıyı kontrol edip yeniden deneyebilirsin.</p><button type="button" onClick={() => void refetch()} disabled={isFetching} className="btn btn-quiet mt-3 disabled:opacity-50">{isFetching ? "Yükleniyor…" : "Tekrar dene"}</button></div></div>}
        {!isLoading && !isError && visibleRows.length === 0 && (
          <div className="p-6 text-center text-sm text-[var(--muted)]">
            <p>{hasFilters ? "Seçili filtrelerle eşleşen öğrenci yok." : "Henüz öğrenci yok."}</p>
            {hasFilters && <button type="button" onClick={clearFilters} className="pressable mt-2 text-xs font-bold text-[var(--brand)] hover:underline">Filtreleri temizle</button>}
          </div>
        )}
        {!isLoading && !isError && <ul className="divide-y divide-[var(--line)]">
          {visibleRows.map(({ student, instruments }) => (
            <li id={`student-${student.id}`} key={student.id} className="scroll-mt-24 target:bg-[var(--brand-soft)]">
              <div className="min-h-14 px-2">
              <button
                onClick={() => setExpandedId(expandedId === student.id ? null : student.id)}
                className="pressable grid min-h-13 w-full min-w-0 grid-cols-[minmax(0,1fr)] items-center gap-3 rounded-lg px-1.5 text-left hover:bg-[var(--surface-muted)] md:grid-cols-[minmax(0,1.2fr)_9rem_minmax(10rem,.9fr)_6rem]"
                aria-expanded={expandedId === student.id}
              >
                <span className="min-w-0">
                  <span className="flex min-w-0 items-center gap-1.5"><span className="truncate text-sm font-bold">{student.firstName} {student.lastName}</span><Icon name="chevron" className={`h-3.5 w-3.5 shrink-0 text-[var(--muted)] transition-transform ${expandedId === student.id ? "rotate-90" : ""}`} /></span>
                  <span className="text-meta mt-0.5 block truncate md:hidden">{student.birthDate} · {instruments.map((item) => item.instrumentName).join(", ") || "Kurs yok"}{student.status === "Inactive" ? " · Pasif" : ""}</span>
                </span>
                <span className="text-meta hidden tabular-nums md:block">{student.birthDate}</span>
                <span className="text-meta hidden truncate md:block">{instruments.map((item) => item.instrumentName).join(", ") || "Kurs yok"}</span>
                <span className={`hidden justify-self-center rounded-full px-2 py-1 text-[.75rem] font-bold md:block ${student.status === "Active" ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--surface-muted)] text-[var(--muted)]"}`}>{student.status === "Active" ? "Aktif" : "Pasif"}</span>
              </button>
              </div>
              {expandedId === student.id && <StudentDetail student={student} isAdmin={isAdmin} canManage={canManage} onDelete={() => setDeleting({ id: student.id, name: `${student.firstName} ${student.lastName}` })} />}
            </li>
          ))}
        </ul>}
      </div>

      {deleting && (isAdmin ? (
        <DeleteStudentDialog
          studentId={deleting.id}
          studentName={deleting.name}
          onClose={() => setDeleting(null)}
          onDeleted={() => setExpandedId(null)}
        />
      ) : (
        <RequestStudentDeletionDialog
          studentId={deleting.id}
          studentName={deleting.name}
          onClose={() => setDeleting(null)}
        />
      ))}

      {canManage && (
        <Modal open={showCreate} title="Öğrenci ekle" onClose={() => setShowCreate(false)} size="sm">
          {/* Yeni öğrenci eklenince satırı açık bırak: sıradaki iş neredeyse her zaman
              veli ve kurs eklemek, ikisi de bu satırın içindeki "+" eylemleri. */}
          {isTeacher && me?.teacherId
            ? <TeacherCreateStudentForm
                teacherId={me.teacherId}
                instrumentIds={me.instrumentIds}
                onClose={() => setShowCreate(false)}
                onCreated={(studentId, name) => {
                  setShowCreate(false);
                  setSearch("");
                  setExpandedId(studentId);
                  announce(`${name} eklendi - veli bilgisi aşağıda eklenebilir.`);
                  scrollToStudentWhenReady(studentId);
                }}
              />
            : <CreateStudentForm
            onClose={() => setShowCreate(false)}
            onCreated={(student) => {
              setShowCreate(false);
              setSearch("");
              setExpandedId(student.id);
              announce(`${student.firstName} ${student.lastName} eklendi - veli ve kurs bilgisi aşağıda eklenebilir.`);
              scrollToStudentWhenReady(student.id);
            }}
          />}
        </Modal>
      )}
    </div>
  );
}

// Yeni eklenen satıra kaydır. Sabit bir gecikme yetmiyor: liste "student-overviews"
// sorgusu tazelendikten sonra yeniden kuruluyor ve satır DOM'a birkaç yüz ms sonra
// giriyor - eleman görünene kadar kısa aralıklarla denenir, en fazla 2 saniye.
function scrollToStudentWhenReady(studentId: string) {
  let attempts = 0;
  let scrolled = 0;
  const timer = window.setInterval(() => {
    const element = document.getElementById(`student-${studentId}`);
    if (element) {
      // Anında kaydırma: "smooth" animasyonu listenin yeniden kurulmasıyla yarışıp
      // iptal oluyordu, satır ekranda hiç görünmüyordu.
      element.scrollIntoView({ block: "center" });
      // Liste, sorgu tazelendikçe yeniden kuruluyor - satır yerine oturana kadar tekrarla.
      if (++scrolled >= 3) window.clearInterval(timer);
      return;
    }
    if (++attempts > 20) window.clearInterval(timer);
  }, 200);
}

function CreateStudentForm({ onClose, onCreated }: { onClose: () => void; onCreated: (student: Student) => void }) {
  const createStudent = useCreateStudent();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [birthDate, setBirthDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      onCreated(await createStudent.mutateAsync({ firstName, lastName, birthDate }));
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Ad<input value={firstName} onChange={(e) => setFirstName(e.target.value)} required className="field text-sm" /></label>
        <label className="form-label">Soyad<input value={lastName} onChange={(e) => setLastName(e.target.value)} required className="field text-sm" /></label>
      </div>
      <label className="form-label">Doğum tarihi<input type="date" value={birthDate} onChange={(e) => setBirthDate(e.target.value)} required className="field text-sm" /></label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Öğrenci ekle" pending={createStudent.isPending} pendingLabel="Ekleniyor…" />
    </form>
  );
}

// Öğretmenin öğrenci ekleme formu. Yöneticininkinden iki farkı var ve ikisi de kasıtlı:
//   1. Enstrüman ZORUNLU - öğretmen için öğrenci ile kurs kaydı aynı işlem; kayıtsız bir
//      öğrenci ona görünmez bile (liste kendi öğrencileriyle sınırlı).
//   2. Enstrüman listesi öğretmenin kendi branşlarıyla sınırlı - sunucu zaten "bu öğretmen
//      bu enstrümanı öğretmiyor" diye reddediyor, form da aynı sınırı gösterir.
function TeacherCreateStudentForm({
  teacherId,
  instrumentIds,
  onClose,
  onCreated,
}: {
  teacherId: string;
  instrumentIds: string[];
  onClose: () => void;
  onCreated: (studentId: string, name: string) => void;
}) {
  const createStudent = useCreateStudentForTeacher(teacherId);
  const { data: instruments } = useInstruments();
  const own = (instruments ?? []).filter((instrument) => instrumentIds.includes(instrument.id));

  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [birthDate, setBirthDate] = useState("");
  const [instrumentId, setInstrumentId] = useState("");
  const [courseKind, setCourseKind] = useState<"Individual" | "Group">("Individual");
  const [error, setError] = useState<string | null>(null);

  const selectedInstrumentId = own.some((instrument) => instrument.id === instrumentId)
    ? instrumentId
    : (own.length === 1 ? own[0].id : "");

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const created = await createStudent.mutateAsync({
        firstName,
        lastName,
        birthDate,
        instrumentId: selectedInstrumentId,
        startedAt: new Date().toISOString().slice(0, 10),
        courseKind,
      });
      onCreated(created.studentId, `${created.firstName} ${created.lastName}`);
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci eklenemedi.");
    }
  }

  if (own.length === 0) {
    return (
      <p className="rounded-xl bg-[var(--warning-soft)]/60 p-4 text-sm text-[var(--warning-strong)]">
        Sana atanmış bir enstrüman yok, bu yüzden öğrenci ekleyemezsin. Yöneticiden branşını tanımlamasını iste.
      </p>
    );
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Ad
          <input value={firstName} onChange={(event) => setFirstName(event.target.value)} required autoFocus maxLength={100} className="field text-sm" />
        </label>
        <label className="form-label">Soyad
          <input value={lastName} onChange={(event) => setLastName(event.target.value)} required maxLength={100} className="field text-sm" />
        </label>
      </div>

      <label className="form-label">Doğum tarihi
        <input type="date" value={birthDate} onChange={(event) => setBirthDate(event.target.value)} required className="field text-sm" />
      </label>

      <div className="grid gap-3 sm:grid-cols-2">
        <label className="form-label">Kurs
          <select value={selectedInstrumentId} onChange={(event) => setInstrumentId(event.target.value)} required className="field text-sm">
            {own.length > 1 && <option value="">Seç…</option>}
            {own.map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}
          </select>
        </label>
        <label className="form-label">Ders türü
          <select value={courseKind} onChange={(event) => setCourseKind(event.target.value as "Individual" | "Group")} className="field text-sm">
            <option value="Individual">Birebir</option>
            <option value="Group">Grup</option>
          </select>
        </label>
      </div>

      <p className="text-meta">Öğrenci sana atanmış olarak eklenir. Veli bilgisini eklendikten sonra satırın içinden girebilirsin.</p>

      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Öğrenciyi ekle" pending={createStudent.isPending} pendingLabel="Ekleniyor…" />
    </form>
  );
}
