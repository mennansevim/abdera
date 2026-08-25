// People modülü tipleri ve React Query hook'ları - docs/07-api.md.
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export type StudentStatus = "Active" | "Inactive";
export type TeacherStatus = "Active" | "Inactive";
export type EnrollmentStatus = "Active" | "Paused" | "Ended";

export interface Instrument {
  id: string;
  name: string;
  code: string;
}

export interface Student {
  id: string;
  firstName: string;
  lastName: string;
  birthDate: string;
  status: StudentStatus;
}

export interface StudentSearchResult {
  studentId: string;
  studentName: string;
  teacherId: string;
  teacherName: string;
  instrumentId: string;
  instrumentName: string;
  guardianPhoneMasked: string | null;
}

export interface AttentionNeededStudent {
  studentId: string;
  studentName: string;
  recentAbsenceCount: number;
  reasons: string[];
}

export function useAttentionNeededStudents() {
  return useQuery({
    queryKey: ["students", "attention-needed"],
    queryFn: () => api.get<AttentionNeededStudent[]>("/api/students/attention-needed"),
  });
}

export interface InstrumentMaintenanceSetting {
  id: string;
  instrumentId: string;
  instrumentName: string;
  maintenanceType: string;
  periodDays: number;
  isEnabled: boolean;
  notificationPreference: "None" | "WhatsApp";
  nextReminderAt: string;
  consentingGuardianCount: number;
}

export function useInstrumentMaintenanceSettings() {
  return useQuery({
    queryKey: ["instrument-maintenance-settings"],
    queryFn: () => api.get<InstrumentMaintenanceSetting[]>("/api/instrument-maintenance-settings"),
  });
}

export function useSaveInstrumentMaintenanceSetting() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ instrumentId, ...body }: { instrumentId: string; maintenanceType: string; periodDays: number; isEnabled: boolean; notificationPreference: "None" | "WhatsApp"; nextReminderAt: string }) =>
      api.put<InstrumentMaintenanceSetting>(`/api/instruments/${instrumentId}/maintenance-setting`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["instrument-maintenance-settings"] }),
  });
}

export function useRunDueMaintenanceReminders() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<{ dueSettingCount: number; scheduledCount: number }>("/api/instrument-maintenance-settings/run-due"),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["instrument-maintenance-settings"] }),
  });
}

export interface Guardian {
  id: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  notificationConsent: boolean;
}

export interface Teacher {
  id: string;
  firstName: string;
  lastName: string;
  status: TeacherStatus;
  instrumentIds: string[];
  hasLoginAccount: boolean;
}

export interface TeacherStudentEnrollment {
  studentId: string;
  firstName: string;
  lastName: string;
  enrollmentId: string;
  instrumentId: string;
  instrumentName: string;
  startedAt: string;
}

export interface TeacherOverview {
  teacher: Teacher;
  students: TeacherStudentEnrollment[];
}

export interface Enrollment {
  id: string;
  studentId: string;
  teacherId: string;
  instrumentId: string;
  status: EnrollmentStatus;
  startedAt: string;
  endedAt: string | null;
}

export function useInstruments() {
  return useQuery({ queryKey: ["instruments"], queryFn: () => api.get<Instrument[]>("/api/instruments") });
}

export function useStudents() {
  return useQuery({ queryKey: ["students"], queryFn: () => api.get<Student[]>("/api/students") });
}

export function useStudentAutocomplete(query: string) {
  const normalized = query.trim();
  return useQuery({
    queryKey: ["student-search", normalized],
    queryFn: () => api.get<StudentSearchResult[]>(`/api/students/search?query=${encodeURIComponent(normalized)}`),
    enabled: normalized.length >= 2,
    staleTime: 30_000,
  });
}

export function useCreateStudent() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { firstName: string; lastName: string; birthDate: string }) =>
      api.post<Student>("/api/students", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["students"] }),
  });
}

export function useGuardians() {
  return useQuery({ queryKey: ["guardians"], queryFn: () => api.get<Guardian[]>("/api/guardians") });
}

export function useCreateGuardian() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { firstName: string; lastName: string; phoneNumber: string }) =>
      api.post<Guardian>("/api/guardians", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["guardians"] }),
  });
}

export interface StudentGuardianLink {
  id: string;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  relationship: string | null;
  isPrimary: boolean;
}

export function useStudentGuardians(studentId: string) {
  return useQuery({
    queryKey: ["student-guardians", studentId],
    queryFn: () => api.get<StudentGuardianLink[]>(`/api/students/${studentId}/guardians`),
    enabled: !!studentId,
  });
}

export function useLinkGuardian(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { guardianId: string; relationship?: string; isPrimary: boolean }) =>
      api.post(`/api/students/${studentId}/guardians`, body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["student-guardians", studentId] }),
  });
}

// Formda çoğunlukla "yeni veli ekle" akışı olduğu için oluşturma + ilişkilendirmeyi
// tek mutation'da birleştirir - iki ayrı API çağrısını UI'da tekrar tekrar yazmamak için.
export function useCreateAndLinkGuardian(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: {
      firstName: string;
      lastName: string;
      phoneNumber: string;
      relationship?: string;
      isPrimary: boolean;
    }) => {
      const guardian = await api.post<Guardian>("/api/guardians", {
        firstName: body.firstName,
        lastName: body.lastName,
        phoneNumber: body.phoneNumber,
      });
      return api.post(`/api/students/${studentId}/guardians`, {
        guardianId: guardian.id,
        relationship: body.relationship,
        isPrimary: body.isPrimary,
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["student-guardians", studentId] });
      queryClient.invalidateQueries({ queryKey: ["guardians"] });
    },
  });
}

export function useTeachers() {
  return useQuery({ queryKey: ["teachers"], queryFn: () => api.get<Teacher[]>("/api/teachers") });
}

export function useTeacherOverviews(enabled = true) {
  return useQuery({
    queryKey: ["teacher-overviews"],
    queryFn: () => api.get<TeacherOverview[]>("/api/teachers/overview"),
    enabled,
  });
}

export interface CreateTeacherResponse {
  teacher: Teacher;
  temporaryPassword: string | null;
}

export function useCreateTeacher() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { firstName: string; lastName: string; instrumentIds: string[]; email?: string }) =>
      api.post<CreateTeacherResponse>("/api/teachers", body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["teachers"] });
      queryClient.invalidateQueries({ queryKey: ["teacher-overviews"] });
    },
  });
}

export function useCreateStudentForTeacher(teacherId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { firstName: string; lastName: string; birthDate: string; instrumentId: string; startedAt: string }) =>
      api.post<TeacherStudentEnrollment>(`/api/teachers/${teacherId}/students`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["students"] });
      queryClient.invalidateQueries({ queryKey: ["teacher-overviews"] });
    },
  });
}

export function useEnrollments(studentId: string) {
  return useQuery({
    queryKey: ["enrollments", studentId],
    queryFn: () => api.get<Enrollment[]>(`/api/students/${studentId}/enrollments`),
    enabled: !!studentId,
  });
}

export function useCreateEnrollment(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { teacherId: string; instrumentId: string; startedAt: string }) =>
      api.post<Enrollment>(`/api/students/${studentId}/enrollments`, body),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["enrollments", studentId] });
      queryClient.invalidateQueries({ queryKey: ["teacher-overviews"] });
    },
  });
}

export function useEndEnrollment(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (enrollmentId: string) =>
      api.delete(`/api/students/${studentId}/enrollments/${enrollmentId}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["enrollments", studentId] });
      queryClient.invalidateQueries({ queryKey: ["calendar"] });
      queryClient.invalidateQueries({ queryKey: ["student-billing", studentId] });
    },
  });
}
