"use client";

import { useMemo, useState } from "react";
import { addDays, dayLabel, hhmm, localDateKey, type AssistantStudent } from "@/lib/assistant";
import { useMakeupCredits, useUseMakeupCredit, type MakeupCredit } from "@/lib/billing";
import { formatDay } from "@/lib/billing-format";
import { errorMessage } from "@/lib/library";
import { useCalendar, useLesson, useSchoolCalendarDays, useTeacherAvailability } from "@/lib/scheduling";
import { findOpenSlots, type SuggestedSlot } from "@/lib/smart-scheduling";
import { Actions, AssistantCard, Choice, ConfirmBox, DoneLine, ErrorLine, GoLink, Loading, StudentHeading } from "./shared";

// "Ece'ye telafi oluştur": süresi en erken dolan hak seçilir, hakkın geçerlilik süresi içindeki
// boş saatler önerilir, onaydan sonra telafi ekranının kullandığı uçla (POST
// /api/makeup-credits/{id}/use) ders açılır. Öğretmen, enstrüman ve süre hakkın doğduğu
// dersten gelir - telafi planlayıcısıyla (makeup-scheduler.tsx) aynı kural.
const SEARCH_DAYS = 21;

function isUsable(credit: MakeupCredit) {
  return credit.status === "Available" && new Date(credit.expiresAt).getTime() > Date.now();
}

export function MakeupFlow({ student }: { student: AssistantStudent }) {
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
      <CreditScheduler key={credit.id} student={student} credit={credit} onDone={setDone} />
    </AssistantCard>
  );
}

function CreditScheduler({ student, credit, onDone }: { student: AssistantStudent; credit: MakeupCredit; onDone: (message: string) => void }) {
  const source = useLesson(credit.sourceLessonId);
  const range = useMemo(() => {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const dayAfterSource = addDays(new Date(credit.sourceLessonStartAt), 1);
    dayAfterSource.setHours(0, 0, 0, 0);
    const start = dayAfterSource > today ? dayAfterSource : today;
    return { start, from: start.toISOString(), to: addDays(start, SEARCH_DAYS).toISOString(), fromKey: localDateKey(start), toKey: localDateKey(addDays(start, SEARCH_DAYS)) };
  }, [credit.sourceLessonStartAt]);
  const lessons = useCalendar(range.from, range.to, { refetchInterval: false });
  const holidays = useSchoolCalendarDays(range.fromKey, range.toKey);
  const availability = useTeacherAvailability(source.data?.teacherId ?? "");
  const consume = useUseMakeupCredit(student.studentId);
  const [picked, setPicked] = useState<SuggestedSlot | null>(null);
  const [cancelled, setCancelled] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (source.isError) return <ErrorLine>Hakkın doğduğu ders bulunamadı; telafiyi takvimden planlayabilirsin.</ErrorLine>;
  if (source.isLoading || lessons.isLoading || availability.isLoading || !source.data) return <Loading>Boş saatlere bakıyorum…</Loading>;

  const lesson = source.data;
  const durationMinutes = Math.round((new Date(lesson.endAt).getTime() - new Date(lesson.startAt).getTime()) / 60000);
  const holidayDates = new Set((holidays.data ?? []).filter((day) => day.type === "Holiday").map((day) => day.date));
  const expiresAt = new Date(credit.expiresAt).getTime();
  const slots = findOpenSlots({
    from: range.start,
    days: SEARCH_DAYS,
    durationMinutes,
    teacherId: lesson.teacherId,
    studentId: student.studentId,
    availability: availability.data ?? [],
    lessons: lessons.data ?? [],
    limit: 12,
  })
    .filter((slot) => slot.start.getTime() < expiresAt && !holidayDates.has(localDateKey(slot.start)))
    .slice(0, 6);

  async function confirm(slot: SuggestedSlot) {
    setError(null);
    try {
      await consume.mutateAsync({ creditId: credit.id, teacherId: lesson.teacherId, instrumentId: lesson.instrumentId, startAt: slot.start.toISOString(), durationMinutes });
      onDone(`${student.fullName} için telafi dersi açıldı: ${dayLabel(slot.start)} ${hhmm(slot.start)}, ${lesson.teacherName}.`);
    } catch (err) {
      setError(errorMessage(err, "Telafi dersi açılamadı."));
    }
  }

  if (cancelled) return <p className="text-meta text-sm">Vazgeçildi; hak kullanılmadı.</p>;

  return (
    <div className="space-y-3">
      <p className="text-sm">
        {slots.length
          ? `${lesson.teacherName} için hakkın süresi içindeki boş saatler (${durationMinutes} dk, ${lesson.instrumentName}):`
          : "Hakkın süresi içinde öğretmenin boş saati görünmüyor. Telafiyi takvimden elle planlayabilirsin."}
      </p>
      {slots.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {slots.map((slot) => (
            <Choice key={slot.start.toISOString()} selected={picked?.start.getTime() === slot.start.getTime()} onClick={() => setPicked(slot)}>
              {dayLabel(slot.start)} {hhmm(slot.start)}
            </Choice>
          ))}
        </div>
      )}
      {picked && (
        <ConfirmBox
          title="Telafi dersi açılacak"
          rows={[
            { label: "Öğrenci", value: `${student.fullName} · ${lesson.instrumentName}` },
            { label: "Kullanılan hak", value: `${formatDay(localDateKey(new Date(credit.sourceLessonStartAt)))} dersi` },
            { label: "Ders", value: `${dayLabel(picked.start)} ${hhmm(picked.start)}–${hhmm(picked.end)} · ${lesson.teacherName}` },
          ]}
          note="Telafi planlayıcısıyla aynı işlemdir; veli ve öğretmen bildirimi de aynı şekilde gider."
          confirmLabel="Telafiyi aç"
          pending={consume.isPending}
          onConfirm={() => void confirm(picked)}
          onCancel={() => setCancelled(true)}
        />
      )}
      {error && <ErrorLine>{error}</ErrorLine>}
      {slots.length === 0 && <Actions><GoLink href="/dashboard/calendar">Takvim</GoLink></Actions>}
    </div>
  );
}
