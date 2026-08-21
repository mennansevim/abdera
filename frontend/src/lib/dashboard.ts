// Dashboard özet API'si - docs/07-api.md GET /api/dashboard/today (denetim ARC-6/E2,
// docs/13-audit-fix-prompt.md madde 13). Admin okul geneli, Teacher yalnızca kendi
// derslerini görür - backend zaten role göre scope'luyor, burada tek bir hook yeterli.
"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "./api";

export interface DashboardToday {
  todayLessons: number;
  attending: number;
  notAttending: number;
  noResponse: number;
  pendingChangeRequests: number;
  overduePayments: number;
  upcomingBirthdays: number;
  upcomingSchoolEvents: number;
}

export function useDashboardToday() {
  return useQuery({
    queryKey: ["dashboard-today"],
    queryFn: () => api.get<DashboardToday>("/api/dashboard/today"),
  });
}
