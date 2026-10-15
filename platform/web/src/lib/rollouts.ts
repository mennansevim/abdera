"use client";

// Filo güncellemesi: dalga dalga dağıtım (platform/api Features/Rollouts.cs, §7).
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export type RolloutStatus = "Running" | "Paused" | "Completed" | "Halted";

export interface Wave {
  wave: number;
  schools: number;
  done: number;
}

export interface Rollout {
  id: string;
  version: string;
  wave: number;
  status: RolloutStatus;
  haltReason: string | null;
  startedAt: string;
  waveCompletedAt: string | null;
  finishedAt: string | null;
  waves: Wave[];
}

export const ROLLOUT_STATUS_LABEL: Record<string, string> = {
  Running: "Sürüyor",
  Paused: "Bekliyor",
  Completed: "Tamamlandı",
  Halted: "Durduruldu",
};

// Dalgalar Domain/Rollout.cs ile aynı: 0 kanarya okullar, 1 kalanların ~%10'u, 2 geri kalanı.
export const WAVE_LABEL = ["Kanarya", "İlk %10", "Kalanlar"];

export function useRollouts() {
  return useQuery({
    queryKey: ["rollouts"],
    queryFn: () => api.get<Rollout[]>("/api/rollouts"),
    refetchInterval: (query) => (query.state.data?.some((r) => r.status === "Running" || r.status === "Paused") ? 5_000 : false),
  });
}

export function useStartRollout() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (version: string) => api.post<{ id: string }>("/api/rollouts", { version }),
    onSettled: () => client.invalidateQueries({ queryKey: ["rollouts"] }),
  });
}

export function useHaltRollout() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/api/rollouts/${id}/halt`),
    onSettled: () => client.invalidateQueries({ queryKey: ["rollouts"] }),
  });
}
