// Benchmark modülü hook'ları - docs/13 Pillar F. GET /api/benchmark/{teachers,students} (Admin).
"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api } from "./api";

export interface TeacherBenchmarkRow {
  teacherId: string;
  teacherName: string;
  activeStudents: number;
  activeEnrollments: number;
  lessons: number;
  notes: number;
  approvedComments: number;
  present: number;
  absent: number;
  excused: number;
  attendanceRate: number; // 0..1
  score: number; // 0..100
  weekLessons: number; // seçilen hafta Pzt-Cmt, iptal/ertelenen hariç
  weekCompleted: number; // seçilen haftada tamamlanan
  weekAttendanceRate: number; // 0..1, seçilen haftanın yoklaması
}

export interface StudentBenchmarkRow {
  studentId: string;
  studentName: string;
  activeEnrollments: number;
  lessons: number;
  present: number;
  absent: number;
  attendanceRate: number;
  notesReceived: number;
  score: number;
}

// weekStart: seçilen haftanın Pazartesi'si (yyyy-MM-dd). Haftalık metrikler bu haftaya göre.
export function useTeacherBenchmark(weekStart: string, options?: { enabled?: boolean }) {
  return useQuery({
    enabled: options?.enabled ?? true,
    queryKey: ["benchmark", "teachers", weekStart],
    queryFn: () => api.get<TeacherBenchmarkRow[]>(`/api/benchmark/teachers?weekStart=${weekStart}`),
    staleTime: 60_000,
    // hafta değişirken liste (ve hafta seçici) iskelete düşmesin
    placeholderData: keepPreviousData,
  });
}

export function useStudentBenchmark() {
  return useQuery({
    queryKey: ["benchmark", "students"],
    queryFn: () => api.get<StudentBenchmarkRow[]>("/api/benchmark/students"),
    staleTime: 60_000,
  });
}
