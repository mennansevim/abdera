import type { Metadata } from "next";
import Link from "next/link";
import { SchoolBrandMark } from "@/components/school-brand";
import { MusicLibrary } from "@/components/music-library/music-library";
import archive from "@/data/sheet-music.json";
import { buildLibrary } from "@/lib/sheet-music";

export const metadata: Metadata = {
  title: "Nota Kütüphanesi",
  description: "Nota kütüphanesi. Piyano, keman, eğitim çalışmaları ve dünya repertuvarından kaynak ve lisans bilgili açık notalar.",
};

export default function PublicMusicLibraryPage() {
  return <div className="min-h-dvh">
    <header className="flex min-h-16 items-center justify-between gap-3 border-b border-[var(--line)] bg-[var(--surface)] px-3 py-2 sm:px-5">
      <Link href="/" aria-label="Ana sayfa" className="text-[var(--brand-strong)]"><SchoolBrandMark /></Link>
      <Link href="/dashboard/library" className="pressable flex min-h-11 items-center rounded-lg border border-[var(--line)] px-3 text-xs font-bold text-[var(--brand-strong)]">Okul paneli</Link>
    </header>
    <main className="mx-auto max-w-7xl p-3 sm:p-4"><MusicLibrary catalogue={buildLibrary(null, archive)} checkedAt={archive.checkedAt} /></main>
  </div>;
}
