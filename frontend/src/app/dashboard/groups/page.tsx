"use client";

// Grup dersleri (Resim gibi): yönetici sınıfı açar (öğretmen + kurs + haftalık gün/saat +
// süre + kapasite), öğrencileri altına ekler. Sunucu her üye için Grup türünde kurs kaydı ve
// gruba bağlı haftalık seri açar (LessonGroups.cs) - yoklama, telafi ve aidat öğrenci bazlı
// mevcut akışlarla yürür. Öğrenciyi gruptan çıkarmak kurs kaydını bitirmektir; ayrılma nedeni
// öğrenci künyesindeki "Kursu bitir" ile aynı listeden sorulur. Öğretmen kendi gruplarını görür.
import { useMemo, useState, type FormEvent } from "react";
import { AddButton, EmptyState, FormActions, FormMessage, Modal, Notice, PageHeader, Panel } from "@/components/ui";
import { Icon, InstrumentSilhouette } from "@/components/icons";
import { api } from "@/lib/api";
import { errorMessage } from "@/lib/library";
import { buildInstrumentColorMap, INSTRUMENT_TONES } from "@/lib/lesson-colors";
import { ENROLLMENT_END_REASON_LABELS, useInstruments, useStudents, useTeachers, type EnrollmentEndReason, isSelectable } from "@/lib/people";
import {
  DAY_NAMES_TR,
  LESSON_GROUP_DEFAULT_CAPACITY,
  LESSON_GROUP_DEFAULT_DURATION,
  useAddLessonGroupMember,
  useCreateLessonGroup,
  useEndLessonGroup,
  useLessonGroups,
  useUpdateLessonGroup,
  type LessonGroup,
  type LessonGroupMember,
} from "@/lib/scheduling";
import { useMe } from "@/lib/use-auth";
import { useMutation, useQueryClient } from "@tanstack/react-query";

const WEEK_DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
const DAY_SHORT: Record<string, string> = { Monday: "Pzt", Tuesday: "Sal", Wednesday: "Çar", Thursday: "Per", Friday: "Cum", Saturday: "Cmt", Sunday: "Paz" };

const todayLocal = () => {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
};

function timeRange(group: Pick<LessonGroup, "startTime" | "durationMinutes">) {
  const [h = "0", m = "0"] = group.startTime.split(":");
  const end = Number(h) * 60 + Number(m) + group.durationMinutes;
  return `${group.startTime.slice(0, 5)} – ${String(Math.floor(end / 60)).padStart(2, "0")}:${String(end % 60).padStart(2, "0")}`;
}

function formatDuration(minutes: number) {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours ? `${hours} sa${rest ? ` ${rest} dk` : ""}` : `${rest} dk`;
}

export default function LessonGroupsPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const { data: groups, isLoading, isError } = useLessonGroups();
  const [showCreate, setShowCreate] = useState(false);
  const [addingTo, setAddingTo] = useState<LessonGroup | null>(null);
  const [editing, setEditing] = useState<LessonGroup | null>(null);
  const [removing, setRemoving] = useState<{ group: LessonGroup; member: LessonGroupMember } | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const colors = useMemo(() => buildInstrumentColorMap((groups ?? []).map((group) => group.instrumentName)), [groups]);

  return (
    <div className="space-y-4">
      <PageHeader
        title="Grup dersleri"
        description={isAdmin ? "Resim gibi grup derslerinin sınıfları: öğretmen, haftalık saat ve öğrenciler." : "Verdiğin grup dersleri ve öğrencileri."}
        actions={isAdmin && <AddButton label="Grup aç" onClick={() => setShowCreate(true)} />}
      />

      {notice && <Notice onDismiss={() => setNotice(null)}>{notice}</Notice>}

      {isLoading ? (
        <div className="grid gap-3 lg:grid-cols-2">{Array.from({ length: 2 }, (_, index) => <div key={index} className="skeleton h-48 rounded-2xl" />)}</div>
      ) : isError ? (
        <FormMessage tone="error">Grup dersleri yüklenemedi.</FormMessage>
      ) : !groups?.length ? (
        <Panel>
          <EmptyState
            icon="students"
            title="Henüz grup dersi yok"
            description={isAdmin ? "Resim için bir grup açıp öğretmenini ve öğrencilerini ekleyebilirsin." : "Sana atanmış bir grup dersi yok."}
            action={isAdmin && <button type="button" onClick={() => setShowCreate(true)} className="btn btn-primary">Grup aç</button>}
          />
        </Panel>
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          {groups.map((group) => {
            const tone = colors.get(group.instrumentName) ?? INSTRUMENT_TONES[0];
            const full = group.members.length >= group.capacity;
            return (
              <Panel
                key={group.id}
                title={group.name}
                meta={`${group.instrumentName} · ${group.teacherName}`}
                actions={isAdmin && (
                  <button type="button" onClick={() => setEditing(group)} className="icon-btn icon-btn-quiet" aria-label={`${group.name} grubunu düzenle`} title="Düzenle">
                    <Icon name="pencil" className="h-4 w-4" />
                  </button>
                )}
                flush
              >
                <div className="relative overflow-hidden border-b border-[var(--line)] px-4 py-3" style={{ background: tone.bg, color: tone.text }}>
                  <InstrumentSilhouette instrumentName={group.instrumentName} className="pointer-events-none absolute -bottom-3 right-2 h-20 w-20 opacity-15" style={{ color: tone.border }} />
                  <p className="relative text-sm font-bold">Her hafta {DAY_NAMES_TR[group.dayOfWeek] ?? group.dayOfWeek} · <span className="tabular-nums">{timeRange(group)}</span></p>
                  <p className="relative mt-0.5 text-[.75rem] opacity-80">{formatDuration(group.durationMinutes)} · {group.members.length}/{group.capacity} öğrenci{full ? " · dolu" : ""}</p>
                  <div className="relative mt-2 flex gap-1" aria-hidden="true">
                    {Array.from({ length: group.capacity }, (_, index) => (
                      <span key={index} className="h-1.5 flex-1 rounded-full" style={{ background: index < group.members.length ? tone.border : "rgba(255,255,255,.7)" }} />
                    ))}
                  </div>
                </div>
                <ul className="divide-y divide-[var(--line)]">
                  {group.members.map((member) => (
                    <li key={member.enrollmentId} className="flex min-h-12 items-center gap-3 px-4 py-2">
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm font-semibold">{member.studentName}</span>
                        <span className="text-meta">{new Date(`${member.joinedOn}T12:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric" })} tarihinden beri</span>
                      </span>
                      {isAdmin && (
                        <button type="button" onClick={() => setRemoving({ group, member })} className="btn btn-quiet min-h-9 px-3 text-xs">Gruptan çıkar</button>
                      )}
                    </li>
                  ))}
                  {!group.members.length && <li className="text-meta px-4 py-3">Bu grupta henüz öğrenci yok.</li>}
                </ul>
                {isAdmin && (
                  <div className="border-t border-[var(--line)] px-4 py-2.5">
                    <button type="button" disabled={full} onClick={() => setAddingTo(group)} className="btn btn-quiet min-h-9 w-full text-xs">
                      {full ? `Grup dolu (${group.capacity}/${group.capacity})` : "Öğrenci ekle"}
                    </button>
                  </div>
                )}
              </Panel>
            );
          })}
        </div>
      )}

      {showCreate && <CreateGroupDialog onClose={() => setShowCreate(false)} onCreated={(text) => { setShowCreate(false); setNotice(text); }} />}
      {addingTo && <AddMemberDialog group={addingTo} onClose={() => setAddingTo(null)} onAdded={(text) => { setAddingTo(null); setNotice(text); }} />}
      {editing && <EditGroupDialog group={editing} onClose={() => setEditing(null)} onSaved={(text) => { setEditing(null); setNotice(text); }} />}
      {removing && <RemoveMemberDialog group={removing.group} member={removing.member} onClose={() => setRemoving(null)} onRemoved={(text) => { setRemoving(null); setNotice(text); }} />}
    </div>
  );
}

function CreateGroupDialog({ onClose, onCreated }: { onClose: () => void; onCreated: (text: string) => void }) {
  const { data: instruments } = useInstruments();
  const { data: teachers } = useTeachers();
  const create = useCreateLessonGroup();
  const defaultInstrument = instruments?.find((instrument) => instrument.code === "ART") ?? instruments?.[0];
  const [instrumentId, setInstrumentId] = useState("");
  const resolvedInstrumentId = instrumentId || defaultInstrument?.id || "";
  const [teacherId, setTeacherId] = useState("");
  const [name, setName] = useState("");
  const [day, setDay] = useState("Saturday");
  const [startTime, setStartTime] = useState("10:00");
  const [duration, setDuration] = useState(LESSON_GROUP_DEFAULT_DURATION);
  const [capacity, setCapacity] = useState(LESSON_GROUP_DEFAULT_CAPACITY);
  const [effectiveFrom, setEffectiveFrom] = useState(todayLocal);
  const [error, setError] = useState<string | null>(null);

  // Yalnızca bu kursu veren aktif öğretmenler; Resim öğretmeni yoksa Öğretmenler ekranından eklenir.
  const eligibleTeachers = (teachers ?? []).filter((teacher) => teacher.status === "Active" && teacher.instrumentIds.includes(resolvedInstrumentId));
  const instrumentName = instruments?.find((instrument) => instrument.id === resolvedInstrumentId)?.name ?? "";
  const suggestedName = `${instrumentName} · ${DAY_NAMES_TR[day] ?? day} ${startTime}`;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const group = await create.mutateAsync({
        name: name.trim() || suggestedName,
        teacherId,
        instrumentId: resolvedInstrumentId,
        dayOfWeek: day,
        startTime: `${startTime}:00`,
        effectiveFrom,
        durationMinutes: duration,
        capacity,
      });
      onCreated(`"${group.name}" grubu açıldı. Şimdi öğrencileri ekleyebilirsin.`);
    } catch (err) {
      setError(errorMessage(err, "Grup açılamadı."));
    }
  }

  return (
    <Modal open title="Grup aç" description="Grubun saati öğretmenin diğer dersleriyle çakışamaz; öğrencisi olmasa da takvimde bu saat ayrılır." onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="form-label">
            Kurs
            <select value={resolvedInstrumentId} onChange={(event) => { setInstrumentId(event.target.value); setTeacherId(""); }} required className="field text-sm">
              {instruments?.filter(isSelectable).map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}
            </select>
          </label>
          <label className="form-label">
            Öğretmen
            <select value={teacherId} onChange={(event) => setTeacherId(event.target.value)} required className="field text-sm">
              <option value="">{eligibleTeachers.length ? "Öğretmen seç" : "Bu kursu veren öğretmen yok"}</option>
              {eligibleTeachers.map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>)}
            </select>
          </label>
        </div>
        {!eligibleTeachers.length && instruments && teachers && (
          <p className="text-meta">Önce Öğretmenler ekranında öğretmeni ekleyip {instrumentName || "bu kursu"} branşını işaretle.</p>
        )}

        <fieldset>
          <legend className="form-label">Gün</legend>
          <div className="mt-1 grid grid-cols-4 gap-1.5 sm:grid-cols-7" role="radiogroup" aria-label="Grup dersi günü">
            {WEEK_DAYS.map((key) => {
              const active = day === key;
              return <button key={key} type="button" role="radio" aria-checked={active} onClick={() => setDay(key)} className={`pressable grid min-h-11 place-items-center rounded-xl border px-1 text-xs font-bold ${active ? "border-[var(--brand)] bg-[var(--brand)] text-white shadow-sm" : "border-[var(--line)] bg-white hover:border-[var(--brand)]"}`}>{DAY_SHORT[key]}</button>;
            })}
          </div>
        </fieldset>

        <div className="grid gap-3 sm:grid-cols-3">
          <label className="form-label">Başlangıç saati<input type="time" value={startTime} onChange={(event) => setStartTime(event.target.value)} required step={900} className="field text-base tabular-nums" /></label>
          <label className="form-label">
            Ders süresi
            <select value={duration} onChange={(event) => setDuration(Number(event.target.value))} className="field text-sm">
              {[60, 75, 90, 105, 120].map((minutes) => <option key={minutes} value={minutes}>{formatDuration(minutes)}</option>)}
            </select>
          </label>
          <label className="form-label">Kapasite<input type="number" min={2} max={12} value={capacity} onChange={(event) => setCapacity(Number(event.target.value))} required className="field text-base tabular-nums" /></label>
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          <label className="form-label">Grup adı<input value={name} onChange={(event) => setName(event.target.value)} maxLength={80} placeholder={suggestedName} className="field text-sm" /></label>
          <label className="form-label">İlk ders tarihi (en erken)<input type="date" value={effectiveFrom} onChange={(event) => setEffectiveFrom(event.target.value)} required className="field text-sm" /></label>
        </div>

        {error && <FormMessage tone="error">{error}</FormMessage>}
        <FormActions onCancel={onClose} submitLabel="Grubu aç" pending={create.isPending} pendingLabel="Açılıyor…" disabled={!teacherId} />
      </form>
    </Modal>
  );
}

function AddMemberDialog({ group, onClose, onAdded }: { group: LessonGroup; onClose: () => void; onAdded: (text: string) => void }) {
  const { data: students } = useStudents();
  const add = useAddLessonGroupMember();
  const [query, setQuery] = useState("");
  const [studentId, setStudentId] = useState("");
  const [startFrom, setStartFrom] = useState(() => (group.effectiveFrom > todayLocal() ? group.effectiveFrom : todayLocal()));
  const [error, setError] = useState<string | null>(null);
  const memberIds = new Set(group.members.map((member) => member.studentId));
  const normalized = query.trim().toLocaleLowerCase("tr-TR");
  const candidates = (students ?? [])
    .filter((student) => student.status === "Active" && !memberIds.has(student.id))
    .filter((student) => !normalized || `${student.firstName} ${student.lastName}`.toLocaleLowerCase("tr-TR").includes(normalized));

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      const result = await add.mutateAsync({ groupId: group.id, studentId, startFrom });
      const student = students?.find((item) => item.id === studentId);
      onAdded(`${student ? `${student.firstName} ${student.lastName}` : "Öğrenci"} gruba eklendi · ${result.generation.created} ders takvime yerleşti.`);
    } catch (err) {
      setError(errorMessage(err, "Öğrenci gruba eklenemedi."));
    }
  }

  return (
    <Modal open title="Öğrenci ekle" description={`${group.name} · ${group.members.length}/${group.capacity} öğrenci. Öğrenciye Grup tarifesinden aidatlı bir kurs kaydı açılır.`} onClose={onClose}>
      <form onSubmit={handleSubmit} className="space-y-3">
        <label className="form-label">
          Öğrenci ara
          <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Ad veya soyad" className="field text-sm" autoFocus />
        </label>
        <div role="radiogroup" aria-label="Öğrenci" className="max-h-64 space-y-1 overflow-y-auto rounded-xl border border-[var(--line)] p-1">
          {candidates.slice(0, 50).map((student) => {
            const active = studentId === student.id;
            return (
              <button key={student.id} type="button" role="radio" aria-checked={active} onClick={() => setStudentId(student.id)} className={`pressable flex min-h-11 w-full items-center justify-between rounded-lg px-3 text-left text-sm ${active ? "bg-[var(--brand-soft)] font-bold text-[var(--brand-strong)]" : "hover:bg-black/[.035]"}`}>
                {student.firstName} {student.lastName}
                {active && <Icon name="check" className="h-4 w-4" />}
              </button>
            );
          })}
          {!candidates.length && <p className="text-meta px-3 py-3">Eşleşen öğrenci yok. Yeni öğrenciyi Öğrenciler ekranından ekleyebilirsin.</p>}
        </div>
        <label className="form-label">İlk ders tarihi (en erken)<input type="date" value={startFrom} min={group.effectiveFrom} onChange={(event) => setStartFrom(event.target.value)} required className="field text-sm" /></label>
        {error && <FormMessage tone="error">{error}</FormMessage>}
        <FormActions onCancel={onClose} submitLabel="Gruba ekle" pending={add.isPending} pendingLabel="Ekleniyor…" disabled={!studentId} />
      </form>
    </Modal>
  );
}

function EditGroupDialog({ group, onClose, onSaved }: { group: LessonGroup; onClose: () => void; onSaved: (text: string) => void }) {
  const update = useUpdateLessonGroup();
  const end = useEndLessonGroup();
  const [name, setName] = useState(group.name);
  const [capacity, setCapacity] = useState(group.capacity);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await update.mutateAsync({ groupId: group.id, name, capacity });
      onSaved("Grup güncellendi.");
    } catch (err) {
      setError(errorMessage(err, "Grup güncellenemedi."));
    }
  }

  async function handleEnd() {
    setError(null);
    try {
      await end.mutateAsync(group.id);
      onSaved(`"${group.name}" grubu kapatıldı.`);
    } catch (err) {
      setError(errorMessage(err, "Grup kapatılamadı."));
    }
  }

  return (
    <Modal open title="Grubu düzenle" description={`Her hafta ${DAY_NAMES_TR[group.dayOfWeek]} ${timeRange(group)} · ${group.teacherName}`} onClose={onClose} size="sm">
      <form onSubmit={handleSubmit} className="space-y-3">
        <label className="form-label">Grup adı<input value={name} onChange={(event) => setName(event.target.value)} maxLength={80} required className="field text-sm" /></label>
        <label className="form-label">Kapasite<input type="number" min={Math.max(2, group.members.length)} max={12} value={capacity} onChange={(event) => setCapacity(Number(event.target.value))} required className="field text-base tabular-nums" /></label>
        <p className="text-meta">
          {group.members.length
            ? "Grubu kapatmak için önce öğrencileri gruptan çıkar."
            : <button type="button" onClick={handleEnd} disabled={end.isPending} className="font-bold text-[var(--danger-strong)] underline">Grubu kapat</button>}
        </p>
        {error && <FormMessage tone="error">{error}</FormMessage>}
        <FormActions onCancel={onClose} submitLabel="Kaydet" pending={update.isPending} />
      </form>
    </Modal>
  );
}

function RemoveMemberDialog({ group, member, onClose, onRemoved }: { group: LessonGroup; member: LessonGroupMember; onClose: () => void; onRemoved: (text: string) => void }) {
  const queryClient = useQueryClient();
  const [reason, setReason] = useState<EnrollmentEndReason | "">("");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  // Gruptan çıkarmak = kurs kaydını bitirmek (öğrenci künyesindeki "Kursu bitir" ile aynı uç).
  const remove = useMutation({
    mutationFn: () => api.post(`/api/students/${member.studentId}/enrollments/${member.enrollmentId}/end`, { reason, note: note || null }),
    onSuccess: () => {
      for (const key of ["lesson-groups", "calendar", "enrollments", "student-lesson-series", "student-overviews", "teacher-overviews", "billing-dues", "open-slots"]) {
        queryClient.invalidateQueries({ queryKey: [key] });
      }
    },
  });

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await remove.mutateAsync();
      onRemoved(`${member.studentName} ${group.name} grubundan çıkarıldı.`);
    } catch (err) {
      setError(errorMessage(err, "Öğrenci gruptan çıkarılamadı."));
    }
  }

  return (
    <Modal open title="Gruptan çıkar" description={`${member.studentName} · ${group.name}. Kurs kaydı bugün biter, gelecek dersleri takvimden kalkar.`} onClose={onClose} size="sm">
      <form onSubmit={handleSubmit} className="space-y-3">
        <label className="form-label">
          Ayrılma nedeni
          <select value={reason} onChange={(event) => setReason(event.target.value as EnrollmentEndReason)} required className="field text-sm">
            <option value="">Neden seç</option>
            {(Object.keys(ENROLLMENT_END_REASON_LABELS) as EnrollmentEndReason[]).map((key) => <option key={key} value={key}>{ENROLLMENT_END_REASON_LABELS[key]}</option>)}
          </select>
        </label>
        <label className="form-label">Not (isteğe bağlı)<input value={note} onChange={(event) => setNote(event.target.value)} maxLength={500} className="field text-sm" /></label>
        {error && <FormMessage tone="error">{error}</FormMessage>}
        <FormActions onCancel={onClose} submitLabel="Gruptan çıkar" pending={remove.isPending} pendingLabel="Çıkarılıyor…" disabled={!reason} />
      </form>
    </Modal>
  );
}
