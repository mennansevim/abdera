"use client";

import Link from "next/link";
import { useMemo, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { Modal } from "@/components/ui";
import type { MakeupCredit } from "@/lib/billing";
import { api } from "@/lib/api";
import { useStudents, type Student } from "@/lib/people";

type AssistantIntent = "makeup" | "remaining" | "move";
type AssistantMessage = {
  id: number;
  role: "assistant" | "user";
  text: string;
  links?: { href: string; label: string }[];
};
type PendingRequest = {
  intent: AssistantIntent;
  originalText: string;
};
type Approval = {
  summary: string;
  student: Student;
};
type MoveDraft = {
  request: PendingRequest;
  student: Student;
};

const WELCOME: AssistantMessage = {
  id: 1,
  role: "assistant",
  text: "Merhaba, ben Abdera Asistanı. Telafi haklarını sorabilir, öğrencinin kalan derslerine gidebilir veya bir ders değişikliği talebini beta olarak prova edebilirsin.",
};

function normalize(value: string) {
  return value
    .toLocaleLowerCase("tr-TR")
    .replaceAll("ı", "i")
    .replaceAll("ş", "s")
    .replaceAll("ğ", "g")
    .replaceAll("ü", "u")
    .replaceAll("ö", "o")
    .replaceAll("ç", "c")
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .replace(/[^a-z0-9]+/g, " ")
    .trim();
}

export function detectAssistantIntent(value: string): AssistantIntent | null {
  const text = normalize(value);
  if (/\b(tasi|degistir|ertele|al)\b/.test(text) && text.includes("ders")) return "move";
  if (text.includes("kalan ders") || text.includes("derslerine git") || text.includes("dersleri goster")) return "remaining";
  if (text.includes("telafi")) return "makeup";
  return null;
}

export function requestChangesData(intent: AssistantIntent, value: string) {
  const text = normalize(value);
  if (intent === "move") return true;
  return intent === "makeup" && /\b(uygula|planla|yerlestir|olustur|yap)\b/.test(text);
}

function matchingStudents(students: Student[], value: string) {
  const text = ` ${normalize(value)} `;
  return students.filter((student) => {
    const first = normalize(student.firstName);
    const last = normalize(student.lastName);
    const full = `${first} ${last}`;
    return text.includes(` ${full} `)
      || (first.length >= 3 && text.includes(` ${first} `))
      || (last.length >= 3 && text.includes(` ${last} `));
  });
}

function studentName(student: Student) {
  return `${student.firstName} ${student.lastName}`;
}

function nextId(messages: AssistantMessage[]) {
  return (messages.at(-1)?.id ?? 0) + 1;
}

export function AbderaAssistant() {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState("");
  const [messages, setMessages] = useState<AssistantMessage[]>([WELCOME]);
  const [pendingRequest, setPendingRequest] = useState<PendingRequest | null>(null);
  const [choices, setChoices] = useState<Student[]>([]);
  const [checkingCredits, setCheckingCredits] = useState(false);
  const [moveDraft, setMoveDraft] = useState<MoveDraft | null>(null);
  const [approval, setApproval] = useState<Approval | null>(null);
  const { data: students = [], isLoading: studentsLoading } = useStudents();
  const activeStudents = useMemo(() => students.filter((student) => student.status === "Active"), [students]);

  function addAssistant(text: string, links?: AssistantMessage["links"]) {
    setMessages((current) => [...current, { id: nextId(current), role: "assistant", text, links }]);
  }

  function addUser(text: string) {
    setMessages((current) => [...current, { id: nextId(current), role: "user", text }]);
  }

  function resetConversation() {
    setMessages([WELCOME]);
    setPendingRequest(null);
    setChoices([]);
    setMoveDraft(null);
    setApproval(null);
    setInput("");
  }

  function askForApproval(student: Student, summary: string) {
    setApproval({ student, summary });
    addAssistant(`${summary} Bu beta sürümünde işlem yapılmayacak. Talebi onaylıyor musun?`);
  }

  function resolveStudent(request: PendingRequest, candidates: Student[]) {
    if (candidates.length === 0) {
      setPendingRequest(request);
      setChoices([]);
      addAssistant("Öğrenciyi bulamadım. Lütfen öğrencinin adını ve soyadını yaz.");
      return;
    }
    if (candidates.length > 1) {
      setPendingRequest(request);
      setChoices(candidates.slice(0, 8));
      addAssistant("Birden fazla öğrenci eşleşti. Devam etmek istediğin öğrenciyi seç.");
      return;
    }
    setPendingRequest(null);
    setChoices([]);
    handleResolvedStudent(request, candidates[0]!);
  }

  async function handleResolvedStudent(request: PendingRequest, student: Student) {
    const name = studentName(student);
    if (request.intent === "remaining") {
      addAssistant(`${name} için ders geçmişi, kalan programı ve telafi özeti öğrenci analizinde görülebilir.`, [
        { href: `/dashboard/students/analysis?studentId=${student.id}`, label: "Kalan derslere git" },
        { href: `/dashboard/students#student-${student.id}`, label: "Öğrenci hesabını aç" },
      ]);
      return;
    }
    if (request.intent === "move") {
      setMoveDraft({ request, student });
      addAssistant(`${name} için ders değişikliği bir kerelik mi, yoksa sonraki tüm dersler için sürekli mi uygulanmalı?`);
      return;
    }
    addAssistant(`${name} için kullanılabilir telafi haklarını kontrol ediyorum.`);
    setCheckingCredits(true);
    try {
      const credits = await api.get<MakeupCredit[]>(`/api/students/${student.id}/makeup-credits`);
      const available = credits.filter((credit) => credit.status === "Available");
      if (!available.length) {
        addAssistant(`${name} için kullanılabilir telafi hakkı bulunmuyor.`, [
          { href: `/dashboard/students/analysis?studentId=${student.id}`, label: "Öğrenci analizine git" },
        ]);
        return;
      }
      const nextExpiry = available
        .map((credit) => new Date(credit.expiresAt))
        .sort((a, b) => a.getTime() - b.getTime())[0]!;
      const expiryText = nextExpiry.toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric" });
      if (requestChangesData(request.intent, request.originalText)) {
        askForApproval(student, `${name} için ${available.length} telafi hakkından en erken sona erecek olanı kullanma talebi hazırlanacak. Son kullanım tarihi ${expiryText}.`);
      } else {
        addAssistant(`${name} için ${available.length} kullanılabilir telafi hakkı var. En erken son kullanım tarihi ${expiryText}.`, [
          { href: `/dashboard/students/analysis?studentId=${student.id}`, label: "Kalan derslere git" },
          { href: "/dashboard/calendar", label: "Takvimi aç" },
        ]);
      }
    } catch {
      addAssistant("Telafi hakları şu anda görüntülenemedi. Bağlantıyı kontrol edip tekrar deneyebilirsin.");
    } finally {
      setCheckingCredits(false);
    }
  }

  function submitText(value: string) {
    const text = value.trim();
    if (!text || approval || studentsLoading || checkingCredits) return;
    addUser(text);
    setInput("");

    const intent = pendingRequest?.intent ?? detectAssistantIntent(text);
    if (!intent) {
      addAssistant("Şimdilik telafi hakkı, kalan derslere gitme ve ders değişikliği taleplerinde yardımcı olabiliyorum. Örneğin “Ahmet Yılmaz’ın telafisini uygula” yazabilirsin.");
      return;
    }
    const request = { intent, originalText: pendingRequest?.originalText ?? text };
    resolveStudent(request, matchingStudents(activeStudents, text));
  }

  function chooseStudent(student: Student) {
    const request = pendingRequest;
    if (!request) return;
    addUser(studentName(student));
    setChoices([]);
    setPendingRequest(null);
    handleResolvedStudent(request, student);
  }

  function chooseMoveScope(scope: "Bir kerelik" | "Sürekli") {
    if (!moveDraft) return;
    const { request, student } = moveDraft;
    setMoveDraft(null);
    addUser(scope);
    askForApproval(student, `${studentName(student)} için “${request.originalText}” ders değişikliği ${scope.toLocaleLowerCase("tr-TR")} talep olarak hazırlanacak.`);
  }

  function confirmRequest() {
    if (!approval) return;
    addUser("Onaylıyorum");
    setApproval(null);
    addAssistant("Onayınız alındı. İşleminiz onay alındıktan sonra gerçekleştirilecektir. Beta sürümünde takvimde veya telafi kayıtlarında herhangi bir değişiklik yapılmadı.");
  }

  function cancelRequest() {
    if (!approval) return;
    addUser("Vazgeç");
    setApproval(null);
    addAssistant("Talep iptal edildi. Herhangi bir değişiklik yapılmadı.");
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    submitText(input);
  }

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        aria-label="Abdera Asistanı beta"
        title="Abdera Asistanı beta"
        className="pressable fixed bottom-[5.15rem] right-3 z-30 inline-flex min-h-12 items-center gap-2 rounded-full bg-[var(--brand-strong)] px-4 text-sm font-bold text-white shadow-[0_12px_32px_rgba(84,43,28,.28)] ring-1 ring-white/50 lg:bottom-6 lg:right-6"
      >
        <Icon name="sparkles" className="h-4 w-4" />
        <span>Asistan</span>
        <span className="rounded-full bg-white/20 px-1.5 py-0.5 text-[.62rem] tracking-wide">BETA</span>
      </button>

      <Modal open={open} title="Abdera Asistanı" description="Beta · Bilgi verir ve onay alır; hiçbir işlemi uygulamaz." onClose={() => setOpen(false)} size="md">
        <div className="flex min-h-[28rem] flex-col">
          <div aria-live="polite" className="flex-1 space-y-3 overflow-y-auto pb-4">
            {messages.map((message) => (
              <div key={message.id} className={`flex ${message.role === "user" ? "justify-end" : "justify-start"}`}>
                <div className={`max-w-[88%] rounded-2xl px-3.5 py-2.5 text-sm leading-relaxed ${message.role === "user" ? "rounded-br-md bg-[var(--brand)] text-white" : "rounded-bl-md bg-[var(--surface-muted)] text-[var(--foreground)]"}`}>
                  <p>{message.text}</p>
                  {message.links && (
                    <div className="mt-2 flex flex-wrap gap-2">
                      {message.links.map((link) => (
                        <Link key={`${message.id}-${link.href}-${link.label}`} href={link.href} onClick={() => setOpen(false)} className="pressable inline-flex min-h-10 items-center gap-1.5 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand-strong)]">
                          {link.label}<Icon name="arrow-right" className="h-3.5 w-3.5" />
                        </Link>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            ))}

            {choices.length > 0 && (
              <div className="flex flex-wrap gap-2" role="group" aria-label="Öğrenci seç">
                {choices.map((student) => (
                  <button key={student.id} type="button" onClick={() => chooseStudent(student)} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand-strong)]">
                    {studentName(student)}
                  </button>
                ))}
              </div>
            )}

            {moveDraft && (
              <div className="flex flex-wrap gap-2" role="group" aria-label="Ders değişikliği kapsamı">
                <button type="button" onClick={() => chooseMoveScope("Bir kerelik")} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand-strong)]">
                  Bir kerelik
                </button>
                <button type="button" onClick={() => chooseMoveScope("Sürekli")} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand-strong)]">
                  Sürekli
                </button>
              </div>
            )}

            {approval && (
              <div className="rounded-xl border border-[var(--brand)]/30 bg-[var(--brand-soft)] p-3">
                <p className="text-xs font-bold text-[var(--brand-strong)]">Onay bekleniyor</p>
                <p className="text-meta mt-1">Bu adım yalnızca beta akışını doğrular; gerçek ders ve telafi kayıtları değişmez.</p>
                <div className="mt-3 flex flex-wrap justify-end gap-2">
                  <button type="button" onClick={cancelRequest} className="btn btn-quiet text-xs">Vazgeç</button>
                  <button type="button" onClick={confirmRequest} className="btn btn-primary text-xs">Talebi onayla</button>
                </div>
              </div>
            )}
          </div>

          {messages.length === 1 && (
            <div className="mb-3 flex flex-wrap gap-2" aria-label="Örnek sorular">
              {["Ahmet Yılmaz’ın telafi hakkı var mı?", "Ahmet Yılmaz’ın kalan derslerine git", "Ahmet Yılmaz’ın dersini perşembe 14:00’a taşı"].map((example) => (
                <button key={example} type="button" onClick={() => setInput(example)} className="pressable min-h-10 rounded-xl border border-[var(--line)] bg-white px-3 text-left text-xs font-semibold text-[var(--muted)] hover:border-[var(--brand)] hover:text-[var(--brand-strong)]">
                  {example}
                </button>
              ))}
            </div>
          )}

          <form onSubmit={handleSubmit} className="sticky bottom-[-1rem] -mx-4 mt-auto flex gap-2 border-t border-[var(--line)] bg-[var(--surface)] px-4 pb-[max(1rem,env(safe-area-inset-bottom))] pt-3 sm:-mx-5 sm:px-5">
            <label className="min-w-0 flex-1">
              <span className="sr-only">Asistana yaz</span>
              <input
                value={input}
                onChange={(event) => setInput(event.target.value)}
                disabled={Boolean(approval) || Boolean(moveDraft)}
                placeholder={studentsLoading ? "Öğrenciler yükleniyor…" : checkingCredits ? "Telafi hakları kontrol ediliyor…" : "Örn. Ece’nin telafisi var mı?"}
                autoFocus
                className="field text-sm"
              />
            </label>
            <button type="submit" disabled={!input.trim() || Boolean(approval) || Boolean(moveDraft) || studentsLoading || checkingCredits} aria-label="Mesajı gönder" className="icon-btn icon-btn-brand shrink-0 disabled:opacity-50">
              <Icon name="arrow-right" className="h-4 w-4" />
            </button>
            <button type="button" onClick={resetConversation} aria-label="Sohbeti temizle" title="Sohbeti temizle" className="icon-btn icon-btn-quiet shrink-0">
              <Icon name="x" className="h-4 w-4" />
            </button>
          </form>
        </div>
      </Modal>
    </>
  );
}
