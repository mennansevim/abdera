"use client";

import { BrandMark } from "@/components/icons";
import { schoolLogoSrc, useSchoolProfile, type SchoolProfile } from "@/lib/instance";

// Okulun kendi adı ve logosu (docs/multi-tenant/ARCHITECTURE.md §5.1). Logo yoksa baş harfler.
export function SchoolBrandMark({ compact = false }: { compact?: boolean }) {
  const profile = useSchoolProfile();
  const { title, subtitle } = brandLines(profile);

  return <BrandMark compact={compact} title={title} subtitle={subtitle} logoSrc={useSchoolLogo().src} initials={initialsOf(profile.shortName)} />;
}

// Logosu yüklenmemiş okulda src null döner, çağıran baş harfleri gösterir.
export function useSchoolLogo(): { src: string | null; initials: string } {
  const profile = useSchoolProfile();
  return {
    src: schoolLogoSrc(profile),
    initials: initialsOf(profile.shortName),
  };
}

// "Deniz Müzik Akademisi" + kısa ad "Deniz" → başlık "Deniz", alt satır "MÜZİK AKADEMİSİ".
// Kısa ad tam adın başında değilse alt satır tam ad olur.
export function brandLines(profile: SchoolProfile): { title: string; subtitle: string } {
  const rest = profile.name.startsWith(profile.shortName) ? profile.name.slice(profile.shortName.length).trim() : profile.name;
  return { title: profile.shortName, subtitle: rest === profile.shortName ? "" : rest.toLocaleUpperCase("tr-TR") };
}

function initialsOf(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => word[0]!.toLocaleUpperCase("tr-TR"))
    .join("");
}
