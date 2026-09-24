import type { Metadata } from "next";
import { MusicLibrary } from "@/components/music-library/music-library";
import archive from "@/data/sheet-music.json";
import schoolBooks from "@/data/school-books.json";
import { buildLibrary } from "@/lib/sheet-music";

export const metadata: Metadata = { title: "Nota Kütüphanesi" };

export default function MusicLibraryPage() {
  return <MusicLibrary catalogue={buildLibrary(schoolBooks, archive)} checkedAt={archive.checkedAt} staff />;
}
