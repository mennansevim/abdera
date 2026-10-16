"use client";

import { useEffect } from "react";
import { DEFAULT_PROFILE, useSchoolProfile } from "@/lib/instance";

// Sekme başlığındaki "· Okul Paneli" son eki okulun kısa adıyla değiştirilir (docs/multi-tenant/ARCHITECTURE.md
// §5.1). Sayfa başlıkları sunucuda statik üretildiği için (layout.tsx metadata şablonu) değişim
// istemcide yapılır; Next başlığı her gezinmede yeniden yazdığından <title> izlenir.
const BUILD_TITLE = "Okul Paneli";
const BUILD_SUFFIX = ` · ${BUILD_TITLE}`;

export function DocumentTitleBrand() {
  const { shortName } = useSchoolProfile();

  useEffect(() => {
    if (shortName === DEFAULT_PROFILE.shortName) return;
    const apply = () => {
      const title = document.title;
      if (title.endsWith(BUILD_SUFFIX)) document.title = title.slice(0, -BUILD_SUFFIX.length) + ` · ${shortName}`;
      else if (title === BUILD_TITLE) document.title = shortName;
    };
    apply();
    const observer = new MutationObserver(apply);
    observer.observe(document.head, { subtree: true, childList: true, characterData: true });
    return () => observer.disconnect();
  }, [shortName]);

  return null;
}
