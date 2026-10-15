"use client";

import { BrandMark } from "@/components/icons";
import { DEFAULT_PROFILE, schoolLogoSrc, useSchoolProfile, type SchoolProfile } from "@/lib/instance";

// Okulun kendi adı ve logosu (docs/multi-tenant/ARCHITECTURE.md §5.1). Profil yoksa ya da henüz
// yüklenmediyse bugünkü "Abdera / SANAT AKADEMİ" görünümü birebir korunur.
export function SchoolBrandMark({ compact = false }: { compact?: boolean }) {
  const profile = useSchoolProfile();
  const { title, subtitle } = brandLines(profile);

  return <BrandMark compact={compact} title={title} subtitle={subtitle} logoSrc={useSchoolLogo().src} initials={initialsOf(profile.shortName)} />;
}

// Logosu yüklenmemiş başka bir okul Abdera logosunu taşımamalı: src null döner, çağıran baş
// harfleri gösterir. Varsayılan kimlikte (bugünkü okul) Abdera logosu kalır.
export function useSchoolLogo(): { src: string | null; initials: string } {
  const profile = useSchoolProfile();
  const isDefaultIdentity = profile.name === DEFAULT_PROFILE.name && profile.shortName === DEFAULT_PROFILE.shortName;
  return {
    src: profile.logoUrl || isDefaultIdentity ? schoolLogoSrc(profile) : null,
    initials: initialsOf(profile.shortName),
  };
}

// "Abdera Sanat Akademi" + kısa ad "Abdera" → başlık "Abdera", alt satır "SANAT AKADEMİ".
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
