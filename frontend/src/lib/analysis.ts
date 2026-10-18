// Öğrenci ve öğretmen analizi ekranlarının API'si (GET /api/students/{id}/analysis,
// GET /api/teachers/{id}/analysis). Bütün sayılar sunucuda AnalysisTimeline kurallarıyla
// hesaplanır; ekran yalnızca seçili ayların toplamını alır ve çizer.
"use client";

import { useQuery } from "@tanstack/react-query";
import { api } from "./api";

export type AnalysisLessonState = "Present" | "Makeup" | "Excused" | "Absent" | "Cancelled" | "Unmarked" | "Upcoming";
export type AnalysisReceivableStatus = "Unpaid" | "Partial" | "Paid" | "Overdue" | "Cancelled";

export interface StudentAnalysisMonth {
  period: string;
  lessons: { startAt: string; state: AnalysisLessonState; teacherName: string }[];
  attended: number;
  rated: number;
  attendanceRate: number | null;
  cancelled: number;
  unmarked: number;
  averageDifficulty: number | null;
  assignmentCount: number;
  assignmentCompleted: number;
  assignmentRate: number | null;
  noteCount: number;
  billing: {
    amount: number;
    paid: number;
    currency: string;
    status: AnalysisReceivableStatus;
    dueDate: string;
    discountReason: string | null;
    lastPaymentDate: string | null;
  } | null;
  lastNote: { at: string; teacherName: string; text: string | null; nextGoal: string | null } | null;
}

export interface StudentAnalysis {
  studentId: string;
  fullName: string;
  birthDate: string;
  status: "Active" | "Inactive";
  siblingDiscount: boolean;
  enrolledSince: string | null;
  courses: { enrollmentId: string; instrumentName: string; teacherName: string; courseKind: "Individual" | "Group"; status: string; startedAt: string }[];
  guardians: { name: string; relationship: string | null; phoneNumber: string; notificationConsent: boolean }[];
  availableMakeupCredits: number;
  nextMakeupCreditExpiry: string | null;
  billingVisible: boolean;
  openBalance: number | null;
  months: StudentAnalysisMonth[];
  pieces: { title: string; composer: string | null; difficulty: number | null; firstPeriod: string; lastPeriod: string; finished: boolean }[];
  weekdays: { day: number; present: number; makeup: number; excused: number; absent: number }[];
}

export interface TeacherAnalysisMonth {
  period: string;
  present: number;
  makeup: number;
  excused: number;
  absent: number;
  cancelled: number;
  unmarked: number;
  upcoming: number;
  completed: number;
  attendanceRate: number | null;
  activeStudents: number;
  notedLessons: number;
  noteCoverage: number | null;
  averageDifficulty: number | null;
  paid: number;
}

export interface TeacherAnalysis {
  teacherId: string;
  fullName: string;
  status: "Active" | "Inactive";
  instruments: string[];
  activeStudentCount: number;
  currency: string;
  months: TeacherAnalysisMonth[];
  students: {
    studentId: string;
    name: string;
    instruments: string;
    lessons: number;
    attended: number;
    rated: number;
    attendanceRate: number | null;
    unmarked: number;
    lastLessonAt: string | null;
    lastNoteAt: string | null;
    averageDifficulty: number | null;
  }[];
  weekdays: { day: number; lessons: number; attended: number; rated: number }[];
}

export function useStudentAnalysis(studentId: string | null, months: number) {
  return useQuery({
    queryKey: ["student-analysis", studentId, months],
    queryFn: () => api.get<StudentAnalysis>(`/api/students/${studentId}/analysis?months=${months}`),
    enabled: Boolean(studentId),
  });
}

export function useTeacherAnalysis(teacherId: string | null, months: number) {
  return useQuery({
    queryKey: ["teacher-analysis", teacherId, months],
    queryFn: () => api.get<TeacherAnalysis>(`/api/teachers/${teacherId}/analysis?months=${months}`),
    enabled: Boolean(teacherId),
  });
}

export function rate(part: number, whole: number) {
  return whole === 0 ? null : Math.round((part / whole) * 100);
}

export function sum<T>(items: T[], pick: (item: T) => number) {
  return items.reduce((total, item) => total + pick(item), 0);
}

export function averageOf(values: (number | null)[]) {
  const present = values.filter((value): value is number => value !== null);
  return present.length ? present.reduce((a, b) => a + b, 0) / present.length : null;
}

// Zorluk 1–5 ölçeği (LessonNote.PieceDifficulty) - progress ekranındaki difficultyLabel ile aynı eşikler.
export function levelName(value: number | null) {
  if (value === null) return "—";
  return value <= 2 ? "Başlangıç" : value <= 3 ? "Orta" : value <= 4 ? "İleri" : "Ustalık";
}

export function formatDecimal(value: number | null) {
  return value === null ? "—" : value.toLocaleString("tr-TR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });
}

export function shortMonth(period: string) {
  return new Date(`${period}-01T00:00:00`).toLocaleDateString("tr-TR", { month: "short" });
}

export function longMonth(period: string) {
  return new Date(`${period}-01T00:00:00`).toLocaleDateString("tr-TR", { month: "long", year: "numeric" });
}

export const WEEKDAY_NAMES = ["", "Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"];

export const PERIOD_OPTIONS = [
  { value: "3", label: "Son 3 ay" },
  { value: "6", label: "Son 6 ay" },
  { value: "12", label: "Son 12 ay" },
] as const;
export type PeriodOption = (typeof PERIOD_OPTIONS)[number]["value"];
