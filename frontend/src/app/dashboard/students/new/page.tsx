"use client";

// docs/13 Pillar C — tek-ekran öğrenci kaydı. Öğrenci + öğretmen/enstrüman + (opsiyonel) ders
// günü + veli tek sade ekranda; tek submit'te zincirlenir (useRegisterStudent). Başarıda
// veliye üretilen şifre gösterilir (WhatsApp'tan da gönderilir).
import { useMemo, useState, type FormEvent } from "react";
import Link from "next/link";
import { AdminGate, FormMessage, onInvalidTurkish, PageHeader, resetValidity } from "@/components/ui";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import { useInstruments, useRegisterStudent, useTeachers, type RegisterStudentResult } from "@/lib/people";
import { DAY_NAMES_TR } from "@/lib/scheduling";

const TODAY = new Date().toISOString().slice(0, 10);
const DAY_KEYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

export default function NewStudentPage() {
  return (
    <AdminGate>
      <NewStudentForm />
    </AdminGate>
  );
}

function NewStudentForm() {
  const teachersQuery = useTeachers();
  const instrumentsQuery = useInstruments();
  const register = useRegisterStudent();

  const activeTeachers = useMemo(
    () => (teachersQuery.data ?? []).filter((t) => t.status === "Active"),
    [teachersQuery.data],
  );
  const instrumentName = useMemo(() => {
    const map = new Map<string, string>();
    for (const i of instrumentsQuery.data ?? []) map.set(i.id, i.name);
    return map;
  }, [instrumentsQuery.data]);

  // öğrenci
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [birthDate, setBirthDate] = useState("");
  // eğitim
  const [teacherId, setTeacherId] = useState("");
  const [instrumentId, setInstrumentId] = useState("");
  const [startedAt, setStartedAt] = useState(TODAY);
  // ders günü
  const [scheduleNow, setScheduleNow] = useState(false);
  const [dayOfWeek, setDayOfWeek] = useState("Monday");
  const [startTime, setStartTime] = useState("16:00");
  // veli
  const [gFirstName, setGFirstName] = useState("");
  const [gLastName, setGLastName] = useState("");
  const [gPhone, setGPhone] = useState("");
  const [relationship, setRelationship] = useState("Anne/Baba");

  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<RegisterStudentResult | null>(null);

  const selectedTeacher = activeTeachers.find((t) => t.id === teacherId);
  const teacherInstruments = selectedTeacher?.instrumentIds ?? [];

  function onTeacherChange(id: string) {
    setTeacherId(id);
    const t = activeTeachers.find((x) => x.id === id);
    // öğretmenin ilk enstrümanını otomatik seç
    setInstrumentId(t?.instrumentIds[0] ?? "");
  }

  function resetForm() {
    setFirstName(""); setLastName(""); setBirthDate("");
    setTeacherId(""); setInstrumentId(""); setStartedAt(TODAY);
    setScheduleNow(false); setDayOfWeek("Monday"); setStartTime("16:00");
    setGFirstName(""); setGLastName(""); setGPhone(""); setRelationship("Anne/Baba");
    setError(null); setResult(null);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (!teacherId || !instrumentId) { setError("Lütfen öğretmen ve enstrüman seç."); return; }
    try {
      const res = await register.mutateAsync({
        student: { firstName, lastName, birthDate },
        teacherId,
        instrumentId,
        startedAt,
        guardian: { firstName: gFirstName, lastName: gLastName || lastName, phoneNumber: gPhone, relationship },
        lesson: scheduleNow ? { dayOfWeek, startTime: `${startTime}:00`, durationMinutes: 45 } : undefined,
      });
      setResult(res);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Kayıt oluşturulamadı. Lütfen tekrar dene.");
    }
  }

  // --- başarı ekranı ---
  if (result) {
    return (
      <div className="mx-auto max-w-[640px] space-y-4">
        <PageHeader title="Öğrenci kaydedildi" />
        <div className="app-card p-5">
          <div className="mb-4 flex items-center gap-2 text-[var(--brand-strong)]">
            <Icon name="check" className="h-5 w-5" />
            <span className="text-title font-bold">{firstName} {lastName} sisteme eklendi.</span>
          </div>
          <div className="rounded-xl bg-[var(--brand-soft)] p-4">
            <p className="text-meta mb-2 font-semibold text-[var(--brand-strong)]">{"Veli giriş bilgileri (WhatsApp'tan gönderildi)"}</p>
            <dl className="space-y-1 text-sm">
              <div className="flex justify-between"><dt className="text-[var(--muted)]">Telefon (kullanıcı adı)</dt><dd className="font-mono font-semibold">{result.guardian.phoneNumber}</dd></div>
              <div className="flex justify-between"><dt className="text-[var(--muted)]">Şifre</dt><dd className="font-mono font-semibold">{result.guardian.password}</dd></div>
            </dl>
          </div>
          {result.lessonWarning && (
            <div className="mt-3"><FormMessage tone="error">{result.lessonWarning}</FormMessage></div>
          )}
          {scheduleNow && result.lessonScheduled && (
            <p className="text-meta mt-3 text-[var(--muted)]">Ders günü: {DAY_NAMES_TR[dayOfWeek]} {startTime} — takvime eklendi.</p>
          )}
          <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row">
            <button type="button" onClick={resetForm} className="btn btn-primary w-full sm:w-auto">Yeni öğrenci ekle</button>
            <Link href="/dashboard/students" className="btn btn-quiet w-full sm:w-auto">Öğrenci listesine dön</Link>
          </div>
        </div>
      </div>
    );
  }

  const pending = register.isPending;

  return (
    <div className="mx-auto max-w-[640px] space-y-4">
      <PageHeader title="Yeni öğrenci kaydı" description="Öğrenci, eğitim, ders günü ve veli bilgilerini tek ekranda gir." />
      <form onSubmit={handleSubmit} className="space-y-4">
        {/* 1) Öğrenci */}
        <fieldset className="app-card p-5">
          <legend className="text-meta mb-3 font-bold uppercase tracking-wide text-[var(--muted)]">1 · Öğrenci</legend>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="form-label">Ad<input className="field" required value={firstName} onChange={resetValidity((e) => setFirstName(e.target.value))} onInvalid={onInvalidTurkish} /></label>
            <label className="form-label">Soyad<input className="field" required value={lastName} onChange={resetValidity((e) => setLastName(e.target.value))} onInvalid={onInvalidTurkish} /></label>
            <label className="form-label">Doğum tarihi<input type="date" className="field" required max={TODAY} value={birthDate} onChange={resetValidity((e) => setBirthDate(e.target.value))} onInvalid={onInvalidTurkish} /></label>
          </div>
        </fieldset>

        {/* 2) Eğitim */}
        <fieldset className="app-card p-5">
          <legend className="text-meta mb-3 font-bold uppercase tracking-wide text-[var(--muted)]">2 · Eğitim</legend>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="form-label">Öğretmen
              <select className="field" required value={teacherId} onChange={resetValidity((e) => onTeacherChange(e.target.value))} onInvalid={onInvalidTurkish}>
                <option value="" disabled>Öğretmen seç…</option>
                {activeTeachers.map((t) => <option key={t.id} value={t.id}>{t.firstName} {t.lastName}</option>)}
              </select>
            </label>
            <label className="form-label">Enstrüman
              <select className="field" required value={instrumentId} onChange={resetValidity((e) => setInstrumentId(e.target.value))} onInvalid={onInvalidTurkish} disabled={!teacherId}>
                <option value="" disabled>{teacherId ? "Enstrüman seç…" : "Önce öğretmen seç"}</option>
                {teacherInstruments.map((id) => <option key={id} value={id}>{instrumentName.get(id) ?? id}</option>)}
              </select>
            </label>
            <label className="form-label">Başlangıç tarihi<input type="date" className="field" required value={startedAt} onChange={resetValidity((e) => setStartedAt(e.target.value))} onInvalid={onInvalidTurkish} /></label>
          </div>
        </fieldset>

        {/* 3) Ders günü */}
        <fieldset className="app-card p-5">
          <legend className="text-meta mb-3 font-bold uppercase tracking-wide text-[var(--muted)]">3 · Ders günü</legend>
          <div className="flex flex-wrap gap-x-4 text-sm">
            <label className="flex min-h-11 items-center gap-2"><input type="radio" name="sched" className="h-5 w-5 accent-[var(--brand)]" checked={!scheduleNow} onChange={() => setScheduleNow(false)} /> Sonra belirle</label>
            <label className="flex min-h-11 items-center gap-2"><input type="radio" name="sched" className="h-5 w-5 accent-[var(--brand)]" checked={scheduleNow} onChange={() => setScheduleNow(true)} /> Şimdi belirle</label>
          </div>
          {scheduleNow && (
            <div className="mt-3 grid gap-3 sm:grid-cols-2">
              <label className="form-label">Gün
                <select className="field" value={dayOfWeek} onChange={(e) => setDayOfWeek(e.target.value)}>
                  {DAY_KEYS.map((d) => <option key={d} value={d}>{DAY_NAMES_TR[d]}</option>)}
                </select>
              </label>
              <label className="form-label">Saat<input type="time" className="field" value={startTime} onChange={(e) => setStartTime(e.target.value)} /></label>
              <p className="text-meta sm:col-span-2 text-[var(--muted)]">Ders süresi 45 dk. Öğretmenin o saati doluysa uyarı verilir; öğrenci yine kaydedilir.</p>
            </div>
          )}
        </fieldset>

        {/* 4) Veli */}
        <fieldset className="app-card p-5">
          <legend className="text-meta mb-3 font-bold uppercase tracking-wide text-[var(--muted)]">4 · Veli</legend>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="form-label">Ad<input className="field" required value={gFirstName} onChange={resetValidity((e) => setGFirstName(e.target.value))} onInvalid={onInvalidTurkish} /></label>
            <label className="form-label">Soyad<input className="field" placeholder={lastName || "Öğrenciyle aynı"} value={gLastName} onChange={(e) => setGLastName(e.target.value)} /></label>
            <label className="form-label">Telefon<input type="tel" inputMode="tel" autoComplete="tel" className="field" required placeholder="0555 123 45 67" value={gPhone} onChange={resetValidity((e) => setGPhone(e.target.value))} onInvalid={onInvalidTurkish} /></label>
            <label className="form-label">Yakınlık<input className="field" value={relationship} onChange={(e) => setRelationship(e.target.value)} /></label>
          </div>
          <p className="text-meta mt-2 text-[var(--muted)]">{"Veliye giriş şifresi otomatik üretilip WhatsApp'tan gönderilir; kayıttan sonra ekranda da gösterilir."}</p>
        </fieldset>

        {error && <FormMessage tone="error">{error}</FormMessage>}

        <div className="flex gap-3">
          <button type="submit" disabled={pending} className="btn btn-primary">
            {pending ? "Kaydediliyor…" : "Öğrenciyi kaydet"}
          </button>
          <Link href="/dashboard/students" className="btn btn-quiet">Vazgeç</Link>
        </div>
      </form>
    </div>
  );
}
