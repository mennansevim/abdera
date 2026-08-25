"use client";

import { useEffect } from "react";
import { applyFontSizePreference, readFontSizePreference } from "@/lib/font-size";

export function FontSizeController() {
  useEffect(() => {
    applyFontSizePreference(readFontSizePreference());
  }, []);

  return null;
}
