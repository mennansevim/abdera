// Kârlılık sekmesi (docs/10-decisions.md V): sunucu her açılışta okulun güncel verisinden
// net kârı, birim ekonomisini, işleyiş eksiklerini ve fikirlerin etkisini hesaplar. Burada
// hesap yok - yalnızca tipler ve sorgular; tek hesap yeri sunucudaki GrowthIdeaEvaluator.

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";
import type { CourseKind } from "./billing";

export type IdeaKind = "NewTeacher" | "GroupClass" | "NewBranch" | "Incentive" | "PriceChange" | "Custom";
export type IdeaStatus = "Idea" | "Trying" | "Applied" | "Dropped";
export type IdeaVerdict = "Recommended" | "Uncertain" | "NotRecommended";
export type GapSeverity = "Warning" | "Info";

export interface ProfitMonth {
  period: string;
  revenue: number;
  teacherCost: number;
  fixedCost: number;
  net: number;
  isCurrent: boolean;
  teacherCostEstimated: boolean;
}

export interface ProfitUnit {
  individualPrice: number;
  groupPrice: number;
  lessonsPerMonth: number;
  averageDiscountRate: number;
  averageTeacherRate: number | null;
  individualMargin: number;
  groupMarginPerStudent: number;
  hourValueIndividual: number;
  hourValueGroup: number;
  groupSizeForComparison: number;
  averageTenureMonths: number;
  tenureIsDefault: boolean;
  lifetimeValue: number;
  activeEnrollments: number;
  endedLast3Months: number;
  earlyChurnLast3Months: number;
}

export interface ProfitInstrument {
  id: string;
  name: string;
  individual: number;
  group: number;
  teachers: number;
  teacherRate: number | null;
  bookedHours: number;
  availableHours: number | null;
  utilization: number | null;
  spareHours: number | null;
}

export interface OperationGap { key: string; severity: GapSeverity; count: number; message: string }

export interface IdeaEvaluation {
  monthlyNet: number;
  oneTimeCost: number;
  twelveMonthNet: number;
  paybackMonth: number | null;
  series: number[];
  verdict: IdeaVerdict;
  reasons: string[];
}

export interface IdeaInput {
  kind: IdeaKind;
  title: string;
  note?: string | null;
  instrumentId?: string | null;
  branchName?: string | null;
  courseKind?: CourseKind | null;
  students?: number | null;
  teacherRatePerLesson?: number | null;
  discountPercent?: number | null;
  discountMonths?: number | null;
  alreadyComingPercent?: number | null;
  priceChangePercent?: number | null;
  lostStudents?: number | null;
  monthlyAmount?: number | null;
  oneTimeCost?: number | null;
}

export interface GrowthIdea extends IdeaInput {
  id: string;
  status: IdeaStatus;
  createdAt: string;
  updatedAt: string;
  evaluation: IdeaEvaluation;
}

export interface ProfitabilityOverview {
  period: string;
  months: ProfitMonth[];
  unit: ProfitUnit;
  instruments: ProfitInstrument[];
  gaps: OperationGap[];
  insights: string[];
  ideas: GrowthIdea[];
}

export type CommentaryStatus = "Ready" | "Unavailable" | "Failed";

export interface ProfitCommentary {
  status: CommentaryStatus;
  period: string;
  text: string | null;
  generatedAt: string | null;
  refreshesLeft: number;
  nextAutomaticOn: string;
}

export const IDEA_KIND_LABEL: Record<IdeaKind, string> = {
  NewTeacher: "Yeni öğretmen",
  GroupClass: "Grup dersi",
  NewBranch: "Yeni branş",
  Incentive: "İndirim kampanyası",
  PriceChange: "Fiyat değişikliği",
  Custom: "Diğer",
};

export const IDEA_STATUS_LABEL: Record<IdeaStatus, string> = {
  Idea: "Fikir",
  Trying: "Deneniyor",
  Applied: "Uygulandı",
  Dropped: "Vazgeçildi",
};

export const VERDICT_LABEL: Record<IdeaVerdict, string> = {
  Recommended: "Mantıklı",
  Uncertain: "Emin değiliz",
  NotRecommended: "Kâr getirmez",
};

const OVERVIEW_KEY = ["profitability"];
const COMMENTARY_KEY = ["profitability", "commentary"];

export function useProfitability() {
  return useQuery({ queryKey: OVERVIEW_KEY, queryFn: () => api.get<ProfitabilityOverview>("/api/profitability") });
}

// Yorum ayda bir üretilir ve saklanır; ekran açıkken tekrar istemeye gerek yok.
export function useProfitCommentary() {
  return useQuery({
    queryKey: COMMENTARY_KEY,
    queryFn: () => api.get<ProfitCommentary>("/api/profitability/commentary"),
    staleTime: 60 * 60 * 1000,
    refetchOnWindowFocus: false,
  });
}

export function useRefreshProfitCommentary() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<ProfitCommentary>("/api/profitability/commentary/refresh"),
    onSuccess: (data) => queryClient.setQueryData(COMMENTARY_KEY, data),
  });
}

export function previewIdea(input: IdeaInput) {
  return api.post<IdeaEvaluation>("/api/profitability/ideas/preview", input);
}

export function useSaveIdea() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...input }: IdeaInput & { id?: string }) =>
      id ? api.put<GrowthIdea>(`/api/profitability/ideas/${id}`, input) : api.post<GrowthIdea>("/api/profitability/ideas", input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: OVERVIEW_KEY, exact: true }),
  });
}

export function useSetIdeaStatus() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: IdeaStatus }) => api.post<void>(`/api/profitability/ideas/${id}/status`, { status }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: OVERVIEW_KEY, exact: true }),
  });
}

export function useDeleteIdea() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.delete<void>(`/api/profitability/ideas/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: OVERVIEW_KEY, exact: true }),
  });
}
