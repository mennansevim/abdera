import Image from "next/image";
import type { CSSProperties } from "react";
import { Icon, type IconName } from "./icons";

// Tam sayfa yükleme ekranı: ortada "ritim tutan" logo, logodan yayılan ses halkaları,
// çevresinde dönen enstrüman rozetleri ve yükselen notalar. Yalnızca oturum/uygulama açılışı
// gibi TAM SAYFA beklemeler içindir - kart ve liste içi beklemelerde `.skeleton` kalır, yoksa
// ekranın her köşesinde logo dönmeye başlar. Animasyonlar globals.css'te `.app-loader*`
// altında; `prefers-reduced-motion` açıkken hepsi durur, geriye statik bir kompozisyon kalır.
const ORBIT: IconName[] = ["piano", "violin", "drums", "guitar", "music"];
// U+FE0E: Apple cihazlar bazı nota karakterlerini renkli emoji çizer; metin biçimini zorlar.
const NOTES = ["♪", "♫", "♩", "♬"].map((note) => `${note}\uFE0E`);

export function AppLoader({ message, background = "var(--background)" }: { message: string; background?: string }) {
  return (
    <main className="grid min-h-dvh place-items-center" style={{ background }}>
      <div role="status" aria-live="polite" className="flex flex-col items-center gap-6">
        <div className="app-loader" aria-hidden="true">
          <span className="app-loader-wave" />
          <span className="app-loader-wave" style={{ animationDelay: "1.2s" }} />
          <div className="app-loader-orbit">
            {ORBIT.map((name, index) => (
              <span key={name} className="app-loader-orbit-item" style={{ "--angle": `${(360 / ORBIT.length) * index}deg` } as CSSProperties}>
                <span className="app-loader-badge" style={{ "--beat-delay": `${index * 0.24}s` } as CSSProperties}>
                  <Icon name={name} className="h-[1.1rem] w-[1.1rem]" />
                </span>
              </span>
            ))}
          </div>
          {NOTES.map((note, index) => (
            <span
              key={note}
              className="app-loader-note"
              style={{ "--drift": `${index % 2 ? 1.6 : -1.6}rem`, left: `${38 + index * 8}%`, animationDelay: `${index * 0.7}s` } as CSSProperties}
            >
              {note}
            </span>
          ))}
          <Image src="/abdera-logo.webp" alt="" width={96} height={96} sizes="96px" loading="eager" unoptimized className="app-loader-logo" />
        </div>
        <p className="text-sm font-semibold text-[var(--muted)]">{message}</p>
      </div>
    </main>
  );
}
