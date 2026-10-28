"use client";

import { useEffect, useState, useSyncExternalStore, type ReactNode } from "react";
import { Icon } from "@/components/icons";

// Asistan ilk açıldığında üç adımlık kısa tanıtım: ne sorulur, öğrenci nasıl seçilir, değişiklik
// nasıl onaylanır. Her adımda kendi kendine dönen küçük bir canlandırma vardır; hareket azaltma
// tercihinde canlandırma son karede durur. Görüldü bilgisi tarayıcıda tutulur, başlıktaki
// "Nasıl kullanılır" düğmesi tanıtımı yeniden açar.
const SEEN_KEY = "abdera.assistant.intro.v1";
const TICK_MS = 950;

export function introSeen() {
  try {
    return window.localStorage.getItem(SEEN_KEY) === "1";
  } catch {
    return false;
  }
}

export function markIntroSeen() {
  try {
    window.localStorage.setItem(SEEN_KEY, "1");
  } catch {
    // Gizli pencerede depolama kapalı olabilir; tanıtım bir sonraki açılışta yeniden görünür.
  }
}

function subscribeReducedMotion(onChange: () => void) {
  const query = window.matchMedia("(prefers-reduced-motion: reduce)");
  query.addEventListener("change", onChange);
  return () => query.removeEventListener("change", onChange);
}

function useReducedMotion() {
  return useSyncExternalStore(
    subscribeReducedMotion,
    () => window.matchMedia("(prefers-reduced-motion: reduce)").matches,
    () => false,
  );
}

// Canlandırmanın karesi: 0..steps arası ilerler, son karede iki tık bekler ve baştan başlar.
// cycle her turda artar; anahtar olarak verilince tek seferlik CSS animasyonları yeniden oynar.
function useLoop(steps: number) {
  const reduced = useReducedMotion();
  // 1'den başlar: panel açıldığında ilk kare boş bir kutu olmasın.
  const [tick, setTick] = useState(1);
  useEffect(() => {
    if (reduced) return;
    const timer = window.setInterval(() => setTick((value) => value + 1), TICK_MS);
    return () => window.clearInterval(timer);
  }, [reduced]);
  const length = steps + 3;
  return reduced ? { step: steps, cycle: 0 } : { step: tick % length, cycle: Math.floor(tick / length) };
}

interface Slide {
  title: string;
  text: string;
  demo: () => ReactNode;
}

const SLIDES: Slide[] = [
  {
    title: "Günlük dilde sor",
    text: "Ders programı, devam, telafi hakkı ya da aidat durumu: okulun kayıtlarına bakıp cevaplarım, gerekirse ilgili kartı açarım.",
    demo: () => <AskDemo />,
  },
  {
    title: "Adı yaz, ben tamamlayayım",
    text: "Öğrencinin adını yazmaya başla ya da @ yaz; liste açılır, Tab ile seçersin. Aynı adlı iki öğrenci karışmaz.",
    demo: () => <MentionDemo />,
  },
  {
    title: "Önce kontrol, sonra senin onayın",
    text: "Ders saati ve telafide öğretmenin boş olduğuna, çakışma ve tatil olmadığına bakarım. Kayıt yalnızca sen \"Onayla\" deyince değişir.",
    demo: () => <ConfirmDemo />,
  },
];

export interface IntroExample {
  text: string;
  studentId?: string;
}

export function AssistantIntro({ examples, onDone, onExample }: {
  examples: IntroExample[];
  onDone: () => void;
  onExample: (example: IntroExample) => void;
}) {
  const [index, setIndex] = useState(0);
  const slide = SLIDES[index] ?? SLIDES[0];
  const last = index === SLIDES.length - 1;
  if (!slide) return null;

  return (
    <section aria-labelledby="assistant-intro-title" className="assistant-rise mx-auto flex max-w-md flex-col gap-4">
      <div className="flex items-center justify-between">
        <p className="text-meta text-xs font-semibold">Kısa tanıtım · {index + 1}/{SLIDES.length}</p>
        <button type="button" onClick={onDone} className="text-meta rounded-lg px-2 py-1 text-xs font-semibold hover:bg-[var(--surface-muted)] hover:text-[var(--brand-strong)]">Geç</button>
      </div>

      <div key={index} className="assistant-rise flex flex-col gap-4">
        <div aria-hidden className="intro-stage relative h-52 overflow-hidden rounded-3xl border border-[var(--line)] bg-[var(--surface-muted)] p-4">
          {slide.demo()}
        </div>
        <div>
          <h3 id="assistant-intro-title" className="text-lg font-extrabold tracking-tight">{slide.title}</h3>
          <p className="text-meta mt-1 text-sm leading-relaxed">{slide.text}</p>
        </div>
      </div>

      <div className="flex items-center gap-1.5" role="tablist" aria-label="Tanıtım adımları">
        {SLIDES.map((item, dot) => (
          <button
            key={item.title}
            type="button"
            role="tab"
            aria-selected={dot === index}
            aria-label={`${dot + 1}. adım: ${item.title}`}
            onClick={() => setIndex(dot)}
            className={`h-1.5 rounded-full transition-all ${dot === index ? "w-6 bg-[var(--brand-strong)]" : "w-1.5 bg-[var(--line)] hover:bg-[var(--muted)]"}`}
          />
        ))}
        <span className="flex-1" />
        {index > 0 && (
          <button type="button" onClick={() => setIndex(index - 1)} className="btn btn-quiet text-sm">Geri</button>
        )}
        <button type="button" onClick={() => (last ? onDone() : setIndex(index + 1))} className="btn btn-primary text-sm">
          {last ? "Başla" : "İleri"}
        </button>
      </div>

      {last && (
        <div className="space-y-1.5 border-t border-[var(--line)] pt-3">
          <p className="text-meta px-1 text-xs font-semibold">Hemen dene</p>
          {examples.map((example) => (
            <button key={example.text} type="button" onClick={() => onExample(example)} className="pressable flex w-full items-center justify-between gap-2 rounded-xl px-3 py-2 text-left text-sm hover:bg-[var(--surface-muted)]">
              {example.text}<Icon name="arrow-right" className="h-3.5 w-3.5 shrink-0 text-[var(--muted)]" />
            </button>
          ))}
        </div>
      )}
    </section>
  );
}

// 1. adım: soru yazılır, gönderilir, cevap ve kart gelir.
function AskDemo() {
  const { step, cycle } = useLoop(4);
  return (
    <div className="flex h-full flex-col justify-end gap-2.5">
      {step >= 1 && (
        <div key={`q${cycle}`} className="assistant-rise flex justify-end">
          <p className="rounded-2xl rounded-br-md bg-[var(--brand-strong)] px-3 py-1.5 text-[.8rem] text-white">
            <span className="intro-type">Deniz bu ay derslere geldi mi?</span>
          </p>
        </div>
      )}
      {step === 2 && <DemoTurn><TypingDots /></DemoTurn>}
      {step >= 3 && (
        <DemoTurn key={`a${cycle}`}>
          <p className="text-[.8rem] leading-snug">{"Ekimdeki 4 dersin 4'üne geldi, 1 telafi hakkı var."}</p>
          {step >= 4 && (
            <div className="assistant-rise mt-1.5 flex items-center gap-2 rounded-xl border border-[var(--line)] bg-white px-2.5 py-1.5">
              <span className="grid h-6 w-6 place-items-center rounded-lg bg-[var(--brand-soft)] text-[var(--brand-strong)]"><Icon name="activity" className="h-3.5 w-3.5" /></span>
              <span className="text-[.75rem] font-bold">Gelişim kartı</span>
              <span className="text-meta ml-auto text-[.7rem]">Devam %100</span>
            </div>
          )}
        </DemoTurn>
      )}
    </div>
  );
}

// 2. adım: kutuya "@De" yazılır, liste açılır, ikinci satır seçilir, ad kutuya yerleşir.
function MentionDemo() {
  const { step, cycle } = useLoop(4);
  const picked = step >= 4;
  const people = [
    { name: "Defne Kaya", meta: "Gitar" },
    { name: "Deniz Arslan", meta: "Piyano" },
    { name: "Derin Yıldız", meta: "Keman" },
  ];
  return (
    <div className="flex h-full flex-col justify-end gap-2">
      {step >= 2 && !picked && (
        <div key={`l${cycle}`} className="assistant-rise overflow-hidden rounded-2xl border border-[var(--line)] bg-white shadow-[0_12px_28px_rgba(84,43,28,.12)]">
          {people.map((person, row) => (
            <div key={person.name} className={`flex items-center gap-2 px-2.5 py-1.5 transition-colors ${step >= 3 && row === 1 ? "bg-[var(--brand-soft)]" : ""}`}>
              <span className="grid h-6 w-6 place-items-center rounded-full bg-[var(--surface-muted)] text-[.6rem] font-extrabold text-[var(--brand-strong)]">{person.name.split(" ").map((part) => part[0]).join("")}</span>
              <span className="text-[.75rem] font-bold">{person.name}</span>
              <span className="text-meta text-[.7rem]">{person.meta}</span>
              {step >= 3 && row === 1 && <kbd className="text-meta ml-auto rounded border border-[var(--line)] px-1 font-sans text-[.6rem]">Tab</kbd>}
            </div>
          ))}
        </div>
      )}
      <div className="flex items-center gap-2 rounded-2xl border border-[var(--brand)] bg-white px-3 py-2 shadow-[0_0_0_3px_rgba(217,102,42,.14)]">
        <p className="min-w-0 flex-1 truncate text-[.8rem]">
          Telafi aç:{" "}
          {picked ? (
            <span key={`p${cycle}`} className="assistant-rise font-bold text-[var(--brand-strong)]">Deniz Arslan </span>
          ) : step >= 1 ? (
            <span key={`t${cycle}`} className="intro-type">@De</span>
          ) : null}
          <span className="intro-caret" />
        </p>
        <span className="grid h-6 w-6 place-items-center rounded-lg bg-[var(--brand-strong)] text-white"><Icon name="arrow-right" className="h-3 w-3" /></span>
      </div>
    </div>
  );
}

// 3. adım: taşıma kartı açılır, denetimler tek tek işaretlenir, onay düğmesine basılır.
function ConfirmDemo() {
  const { step, cycle } = useLoop(5);
  const checks = ["Öğretmen o saatte müsait", "Başka dersle çakışma yok", "Tatil günü değil"];
  return (
    <div className="flex h-full items-center">
      <div className="w-full rounded-2xl border border-[var(--line)] bg-white p-3 shadow-[0_12px_28px_rgba(84,43,28,.1)]">
        <p className="flex items-center gap-2 text-[.8rem] font-extrabold">
          <Icon name="clock" className="h-3.5 w-3.5 text-[var(--brand-strong)]" />Ders saatini değiştir
        </p>
        <p className="mt-1 text-[.75rem]">
          <span className="text-meta line-through">Salı 17:00</span>
          <Icon name="arrow-right" className="mx-1 inline h-3 w-3 text-[var(--muted)]" />
          <span className="font-bold">Perşembe 18:00</span>
        </p>
        <ul className="mt-2 space-y-1">
          {checks.map((check, row) => (
            <li key={check} className="flex items-center gap-1.5 text-[.72rem]">
              <span className={`grid h-4 w-4 place-items-center rounded-full transition-colors duration-300 ${step > row ? "bg-[var(--success)] text-white" : "bg-[var(--surface-muted)] text-transparent"}`}>
                <Icon name="check" className="h-2.5 w-2.5" />
              </span>
              <span className={step > row ? "" : "text-meta"}>{check}</span>
            </li>
          ))}
        </ul>
        <div className="mt-2.5 flex justify-end">
          <span key={`b${cycle}`} className={`rounded-xl px-3 py-1.5 text-[.75rem] font-bold transition-all duration-300 ${step >= 4 ? "bg-[var(--brand-strong)] text-white" : "bg-[var(--line)] text-[var(--muted)]"} ${step === 4 ? "intro-press" : ""}`}>
            {step >= 5 ? <span className="inline-flex items-center gap-1"><Icon name="check" className="h-3 w-3" />Taşındı</span> : "Onayla ve taşı"}
          </span>
        </div>
      </div>
    </div>
  );
}

function DemoTurn({ children }: { children: ReactNode }) {
  return (
    <div className="assistant-rise flex items-start gap-2">
      <span className="grid h-6 w-6 shrink-0 place-items-center rounded-lg bg-gradient-to-br from-[var(--brand)] to-[var(--brand-strong)] text-white">
        <Icon name="sparkles" className="h-3.5 w-3.5" />
      </span>
      <div className="min-w-0 flex-1">{children}</div>
    </div>
  );
}

function TypingDots() {
  return (
    <span className="inline-flex h-5 items-center gap-1 text-[var(--muted)]">
      <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current [animation-delay:-240ms]" />
      <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current [animation-delay:-120ms]" />
      <span className="h-1.5 w-1.5 animate-bounce rounded-full bg-current" />
    </span>
  );
}
