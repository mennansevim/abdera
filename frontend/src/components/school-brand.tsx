"use client";

import { BrandMark } from "@/components/icons";
import { DEFAULT_PROFILE, schoolLogoSrc, useSchoolProfile, type SchoolProfile } from "@/lib/instance";

// Okulun kendi adı ve logosu (docs/multi-tenant/ARCHITECTURE.md §5.1). Profil yoksa ya da henüz
// yüklenmediyse bugünkü "Abdera / SANAT AKADEMİ" görünümü birebir korunur.
export function SchoolBrandMark({ compact = false }: { compact?: boolean }) {
  const profile = useSchoolProfile();
  const { title, subtitle } = brandLines(profile);
  const isDefaultIdentity = profile.name === DEFAULT_PROFILE.name && profile.shortName === DEFAULT_PROFILE.shortName;
  // Logosu yüklenmemiş başka bir okul Abdera logosunu taşımamalı: baş harfleri gösterilir.
  const logoSrc = profile.logoUrl || isDefaultIdentity ? schoolLogoSrc(profile) : null;

  return <BrandMark compact={compact} title={title} subtitle={subtitle} logoSrc={logoSrc} initials={initialsOf(profile.shortName)} />;
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
