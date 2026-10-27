"use client";

import { useMemo, useState, type FormEvent } from "react";
import { addDays, dayLabel, hasPassed, hhmm, localDateKey, type AssistantMove, type AssistantStudent } from "@/lib/assistant";
import { useMakeupCredits, useUseMakeupCredit, type MakeupCredit } from "@/lib/billing";
import { formatDay } from "@/lib/billing-format";
import { errorMessage } from "@/lib/library";
import { useCalendar, useLesson, useSchoolCalendarDays, useTeacherAvailability } from "@/lib/scheduling";
import { checkManualSlot, findOpenSlots } from "@/lib/smart-scheduling";
import { Actions, AssistantCard, Choice, ConfirmBox, DoneLine, ErrorLine, GoLink, Loading, StudentHeading, WarningLine } from "./shared";

// "Ece'ye telafi oluştur": süresi en erken dolan hak seçilir, hakkın geçerlilik süresi içindeki
// boş saatler önerilir, onaydan sonra telafi ekranının kullandığı uçla (POST
// /api/makeup-credits/{id}/use) ders açılır. Öğretmen, enstrüman ve süre hakkın doğduğu
// dersten gelir - telafi planlayıcısıyla (makeup-scheduler.tsx) aynı kural. Kullanıcı gün/saat
// söylediyse ("cuma 17:00'de telafi") kart önce o saati denetler: hakkın süresi, tatil, öğretmen
// müsaitliği, öğretmen/öğrenci çakışması. Uygunsa onay kutusu açılır, değilse nedeni ve boş
// saatler gösterilir. Sunucu çakışmayı ayrıca denetler; müsaitliği denetlemez.
const SEARCH_DAYS = 21;
// Elle seçilen gün hakkın son gününe kadar olabilir; çakışma için o aralığın dersleri çekilir.
const MAX_RANGE_DAYS = 90;

function isUsable(credit: MakeupCredit) {
  return credit.status === "Available" && new Date(credit.expiresAt).getTime() > Date.now();
}

export function MakeupFlow({ student, move = null }: { student: AssistantStudent; move?: AssistantMove | null }) {
  const credits = useMakeupCredits(student.studentId);
  const usable = useMemo(
    () => (credits.data ?? []).filter(isUsable).sort((a, b) => a.expiresAt.localeCompare(b.expiresAt)),
    [credits.data],
  );
  const [creditId, setCreditId] = useState<string | null>(null);
  // Hak kullanılınca liste tazelenir ve hak listeden düşer; sonuç mesajı burada tutulur.
  const [done, setDone] = useState<string | null>(null);
  const credit = usable.find((item) => item.id === creditId) ?? usable[0];

  if (done) {
    return (
      <AssistantCard>
        <StudentHeading student={student} />
        <DoneLine>{done}</DoneLine>
        <p className="text-meta text-xs">Birebir derste telafi, Geldi işaretlenince yoklama kartında bir satır doldurur.</p>
        <Actions><GoLink href="/dashboard/calendar">Takvimde gör</GoLink></Actions>
      </AssistantCard>
    );
  }

  if (credits.isLoading) return <AssistantCard><StudentHeading student={student} /><Loading>Telafi hakları getiriliyor…</Loading></AssistantCard>;
  if (credits.isError) return <AssistantCard><StudentHeading student={student} /><ErrorLine>Telafi hakları şu an getirilemedi.</ErrorLine></AssistantCard>;

  if (!credit) {
    return (
      <AssistantCard>
        <StudentHeading student={student} />
        <p className="text-sm">{student.fullName} için kullanılabilir telafi hakkı yok. Telafi hakkı Mazeretli yoklamayla ya da ders iptaliyle açılır.</p>
        <Actions><GoLink href={`/dashboard/students/analysis?studentId=${student.studentId}`}>Öğrenci analizi</GoLink></Actions>
      </AssistantCard>
    );
  }

  return (
    <AssistantCard>
      <StudentHeading student={student} />
      <ul className="divide-y divide-[var(--line)]">
        {usable.map((item, index) => (
          <li key={item.id} className="flex items-center justify-between gap-3 py-2">
            <span className="min-w-0 text-sm">
              <b>{formatDay(localDateKey(new Date(item.sourceLessonStartAt)))} dersi</b>
              <span className="text-meta block text-xs">Son gün {new Date(new Date(item.expiresAt).getTime() - 1).toLocaleDateString("tr-TR", { day: "numeric", month: "long" })}{index === 0 ? " · süresi en erken dolan" : ""}</span>
            </span>
            {usable.length > 1 && <Choice selected={item.id === credit.id} onClick={() => setCreditId(item.id)}>Bu hakkı kullan</Choice>}
          </li>
        ))}
      </ul>
      {/* Hak değişince öneriler ve onay baştan kurulsun. */}
      <CreditScheduler key={credit.id} student={student} credit={credit} move={move} onDone={setDone} />
    </AssistantCard>
  );
}

function CreditScheduler({ student, credit, move, onDone }: { student: AssistantStudent; credit: MakeupCredit; move: AssistantMove | null; onDone: (message: string) => void }) {
  const source = useLesson(credit.sourceLessonId);
  const range = useMemo(() => {
    const now = new Date();
    const today = new Date(now);
    today.setHours(0, 0, 0, 0);
    const dayAfterSource = addDays(new Date(credit.sourceLessonStartAt), 1);
    dayAfterSource.setHours(0, 0, 0, 0);
    const start = dayAfterSource > today ? dayAfterSource : today;
    const expires = new Date(credit.expiresAt);
    const end = new Date(Math.min(addDays(expires, 1).getTime(), addDays(start, MAX_RANGE_DAYS).getTime()));
    end.setHours(0, 0, 0, 0);
    // Son gün: hak expiresAt anından önceki gün (expiresAt çoğunlukla gece yarısıdır).
    const lastDay = new Date(Math.min(expires.getTime() - 1, end.getTime() - 1));
    return { now, start, end, lastDay, from: start.toISOString(), to: end.toISOString(), fromKey: localDateKey(start), toKey: localDateKey(end), lastKey: localDateKey(lastDay) };
  }, [credit.sourceLessonStartAt, credit.expiresAt]);
  const lessons = useCalendar(range.from, range.to, { refetchInterval: false });
  const holidays = useSchoolCalendarDays(range.fromKey, range.toKey);
  const availability = useTeacherAvailability(source.data?.teacherId ?? "");
  const consume = useUseMakeupCredit(student.studentId);
  const [picked, setPicked] = useState<Date | null>(null);
  const [manualDate, setManualDate] = useState("");
  const [manualTime, setManualTime] = useState("");
  const [cancelled, setCancelled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (source.isError) return <ErrorLine>Hakkın doğduğu ders bulunamadı; telafiyi takvimden planlayabilirsin.</ErrorLine>;
  if (lessons.isError || availability.isError) return <ErrorLine>Takvim ya da öğretmenin müsaitliği getirilemedi; telafiyi takvimden planla.</ErrorLine>;
  // Müsaitlik ve dersler gelmeden saat denetlenmez (boş liste "her gün açık" sayılırdı).
  if (!source.data || !lessons.data || !availability.data || holidays.isLoading) return <Loading>Boş saatlere bakıyorum…</Loading>;

  const lesson = source.data;
  const durationMinutes = Math.round((new Date(lesson.endAt).getTime() - new Date(lesson.startAt).getTime()) / 60000);
  const holidayLabels = new Map((holidays.data ?? []).filter((day) => day.type === "Holiday").map((day) => [day.date, day.label]));
  const expiresAt = new Date(credit.expiresAt).getTime();
  const common = { durationMinutes, teacherId: lesson.teacherId, studentId: student.studentId, availability: availability.data, lessons: lessons.data };

  function problemOf(start: Date): string | null {
    if (Number.isNaN(start.getTime())) return "Geçerli bir gün ve saat seç.";
    if (start <= range.now) return "Geçmiş bir saate telafi açılamaz.";
    if (start < range.start) return "Telafi, hakkın doğduğu dersten sonraki bir gün olmalı.";
    if (start.getTime() >= expiresAt) return `Hakkın süresi ${range.lastDay.toLocaleDateString("tr-TR", { day: "numeric", month: "long" })} günü doluyor.`;
    if (start >= range.end) return "Bu tarih için takvim yüklenmedi; daha yakın bir gün seç.";
    const holiday = holidayLabels.get(localDateKey(start));
    if (holiday) return `Resmi tatil (${holiday}).`;
    return checkManualSlot({ start, ...common });
  }

  const requested = requestedFromMove(move);
  const target = picked ?? (requested?.time ? requested.time : null);
  const problem = target ? problemOf(target) : null;
  const slots = findOpenSlots({ from: requested && !requested.time && !picked ? requested.day : range.start, days: requested && !requested.time && !picked ? 1 : SEARCH_DAYS, ...common, limit: 12 })
    .filter((slot) => slot.start >= range.start && slot.start.getTime() < expiresAt && !holidayLabels.has(localDateKey(slot.start)))
    .slice(0, 6);
  const askedDayEmpty = requested && !requested.time && !picked && slots.length === 0;

  async function confirm(start: Date) {
    setError(null);
    if (hasPassed(start)) {
      setError("Bu saat geçti; başka bir saat seç.");
      return;
    }
    try {
      await consume.mutateAsync({ creditId: credit.id, teacherId: lesson.teacherId, instrumentId: lesson.instrumentId, startAt: start.toISOString(), durationMinutes });
      onDone(`${student.fullName} için telafi dersi açıldı: ${dayLabel(start)} ${hhmm(start)}, ${lesson.teacherName}.`);
    } catch (err) {
      setError(errorMessage(err, "Telafi dersi açılamadı."));
    }
  }

  function submitManual(event: FormEvent) {
    event.preventDefault();
    if (manualDate && manualTime) setPicked(new Date(`${manualDate}T${manualTime}:00`));
  }

  if (cancelled) return <p className="text-meta text-sm">Vazgeçildi; hak kullanılmadı.</p>;

  return (
    <div className="space-y-3">
      {target && problem && <WarningLine>{dayLabel(target)} {hhmm(target)}: {problem}</WarningLine>}
      {askedDayEmpty && <WarningLine>{dayLabel(requested.day)} günü {lesson.teacherName} için boş saat yok.</WarningLine>}
      <p className="text-sm">
        {slots.length
          ? requested && !requested.time && !picked
            ? `${dayLabel(requested.day)} için boş saatler (${durationMinutes} dk, ${lesson.instrumentName}, ${lesson.teacherName}):`
            : `${lesson.teacherName} için hakkın süresi içindeki boş saatler (${durationMinutes} dk, ${lesson.instrumentName}):`
          : "Önerecek boş saat bulamadım; günü ve saati kendin yazabilirsin."}
      </p>
      {slots.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {slots.map((slot) => (
            <Choice key={slot.start.toISOString()} selected={target?.getTime() === slot.start.getTime()} onClick={() => setPicked(slot.start)}>
              {dayLabel(slot.start)} {hhmm(slot.start)}
            </Choice>
          ))}
        </div>
      )}
      <form onSubmit={submitManual} className="flex flex-wrap items-end gap-2">
        <label className="text-xs font-semibold">Gün<input type="date" className="field mt-1 text-sm" value={manualDate} min={range.fromKey} max={range.lastKey} onChange={(event) => setManualDate(event.target.value)} /></label>
        <label className="text-xs font-semibold">Saat<input type="time" step={900} className="field mt-1 text-sm" value={manualTime} onChange={(event) => setManualTime(event.target.value)} /></label>
        <button type="submit" disabled={!manualDate || !manualTime} className="btn btn-quiet text-xs">Bu saati dene</button>
      </form>
      {target && !problem && (
        <ConfirmBox
          title="Telafi dersi açılacak"
          rows={[
            { label: "Öğrenci", value: `${student.fullName} · ${lesson.instrumentName}` },
            { label: "Kullanılan hak", value: `${formatDay(localDateKey(new Date(credit.sourceLessonStartAt)))} dersi` },
            { label: "Telafi", value: `${dayLabel(target)} ${hhmm(target)}–${hhmm(new Date(target.getTime() + durationMinutes * 60000))}` },
            { label: "Öğretmen", value: `${lesson.teacherName} · ${durationMinutes} dk` },
          ]}
          checks={[
            `Hakkın süresi içinde (son gün ${range.lastDay.toLocaleDateString("tr-TR", { day: "numeric", month: "long" })})`,
            "Öğretmenin müsait olduğu saat",
            "Öğretmenin ve öğrencinin o saatte başka dersi yok",
            "Resmi tatil değil",
          ]}
          note="Telafi planlayıcısıyla aynı işlemdir; veli ve öğretmen bildirimi de aynı şekilde gider."
          confirmLabel="Onayla, telafiyi aç"
          pending={consume.isPending}
          onConfirm={() => void confirm(target)}
          onCancel={() => setCancelled(true)}
        />
      )}
      {error && <ErrorLine>{error}</ErrorLine>}
      {slots.length === 0 && <Actions><GoLink href="/dashboard/calendar">Takvim</GoLink></Actions>}
    </div>
  );
}

// Cümledeki telafi günü/saati: gün yoksa null; saat yoksa yalnız gün (kart o günün boş saatlerini
// gösterir). Sunucu yalnız gün adı verildiyse ("cuma") targetDate'i bugünden sonraki ilk o gün yapar.
function requestedFromMove(move: AssistantMove | null): { day: Date; time: Date | null } | null {
  if (!move?.targetDate) return null;
  const day = new Date(`${move.targetDate}T00:00:00`);
  if (Number.isNaN(day.getTime())) return null;
  if (!move.targetTime) return { day, time: null };
  const [hour, minute] = move.targetTime.split(":").map(Number);
  const time = new Date(day);
  time.setHours(hour ?? 0, minute ?? 0, 0, 0);
  return { day, time };
}
