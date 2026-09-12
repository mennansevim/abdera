// Benchmark modülü hook'ları - docs/13 Pillar F. GET /api/benchmark/{teachers,students} (Admin).
"use client";

import { useQuery } from "@tanstack/react-query";
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

export function useTeacherBenchmark() {
  return useQuery({
    queryKey: ["benchmark", "teachers"],
    queryFn: () => api.get<TeacherBenchmarkRow[]>("/api/benchmark/teachers"),
    staleTime: 60_000,
  });
}

export function useStudentBenchmark() {
  return useQuery({
    queryKey: ["benchmark", "students"],
    queryFn: () => api.get<StudentBenchmarkRow[]>("/api/benchmark/students"),
    staleTime: 60_000,
  });
}
