"use client";

// Okul sunucuları (platform/api Features/Hosts.cs, §4.4).
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export type HostStatus = "Active" | "Draining" | "Offline";

export interface ServerHost {
  id: string;
  name: string;
  address: string;
  sshPort: number;
  sshUser: string;
  hostKeyFingerprint: string;
  publicIp: string;
  totalMemoryMb: number;
  reservedMemoryMb: number;
  capacityMb: number;
  status: HostStatus;
  schoolCount: number;
  agentVersion: string | null;
  lastCheckedAt: string | null;
}

export interface HostInput {
  name: string;
  address: string;
  sshPort: number;
  sshUser: string;
  hostKeyFingerprint: string;
  publicIp: string;
  totalMemoryMb: number;
}

export const HOST_STATUS_LABEL: Record<HostStatus, string> = {
  Active: "Etkin",
  Draining: "Boşaltılıyor",
  Offline: "Kapalı",
};

export function useHosts() {
  return useQuery({ queryKey: ["hosts"], queryFn: () => api.get<ServerHost[]>("/api/hosts") });
}

export function useCreateHost() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: HostInput) => api.post<ServerHost>("/api/hosts", input),
    onSuccess: () => client.invalidateQueries({ queryKey: ["hosts"] }),
  });
}

// Ajanı yoklar; cevap ajanın `host-info` çıktısıdır (serbest JSON).
export function useCheckHost() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post<Record<string, unknown>>(`/api/hosts/${id}/check`),
    onSettled: () => client.invalidateQueries({ queryKey: ["hosts"] }),
  });
}

export function useSetHostStatus() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: HostStatus }) => api.put<void>(`/api/hosts/${id}/status`, { status }),
    onSettled: () => client.invalidateQueries({ queryKey: ["hosts"] }),
  });
}
