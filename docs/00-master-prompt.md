# Music School Management System — Master Prompt

## Role

You are a Senior Software Architect, Senior Backend Engineer, and Product-minded Technical Lead.

We are designing a small but production-quality Music School Management System. Do not over-engineer the solution. Before writing application code, inspect the requirements, propose the architecture and domain model, identify assumptions, and wait for approval of the implementation plan.

---

## Product Context

The initial customer is a small music school with:

- 1 administrator
- 6 teachers: 4 piano, 1 guitar, 1 drums
- mostly child students
- parents/guardians who already communicate heavily through WhatsApp

The school currently uses WhatsApp, Excel, paper/calendar notes, manual payment tracking, and teacher-to-manager messages.

The product should centralize:

- lesson scheduling and teacher schedules
- attendance and parent RSVP responses
- lesson changes and make-up lessons
- dues and manual payment tracking
- lesson notes and student progress
- WhatsApp notifications
- birthdays and operational reminders

The product principle is:

> Build a small-school operating system, not an ERP, accounting application, generic CRM, full LMS, workflow engine, or microservice platform.

Use this interface model:

~~~text
Administrator -> Web application
Teacher       -> Responsive web application / PWA
Guardian      -> WhatsApp
~~~

Do not force parents to install another application in the MVP.

---

## Architecture Decision

Build the first version as a Modular Monolith: one backend deployment with clear business-module boundaries.

Do not use microservices or introduce Kafka, RabbitMQ, Redis, Kubernetes, Keycloak, Elasticsearch, Camunda, Temporal, or event-streaming infrastructure unless a concrete requirement cannot reasonably be solved without them.

PostgreSQL is sufficient for application state and persistent scheduled jobs at this scale. The architecture may allow future module extraction, but future extraction is not a reason to add distributed-system complexity now.

---

## Preferred Technical Stack

### Backend

- Java 21
- Spring Boot 3.x
- Spring Web
- Spring Security
- Spring Data JPA and Hibernate
- Jakarta Bean Validation
- PostgreSQL
- Flyway
- Spring Scheduler
- Spring Boot Actuator
- OpenAPI / Swagger
- JUnit 5
- Testcontainers

### Frontend

- React or Next.js
- TypeScript
- responsive design
- PWA behavior where useful

### Deployment

- Docker
- Docker Compose for local development
- one backend deployment
- one frontend deployment
- managed PostgreSQL where possible
- HTTPS
- environment-based configuration

### External integration

- Meta WhatsApp Business Platform / Cloud API

Future integrations may include online payments, object storage, or an AI provider, but they are not required for the MVP.

---

## Users and Authorization

### Administrator

Can manage students, guardians, teachers, instruments, enrollments, schedules, lessons, change requests, make-up lessons, payments, attendance, WhatsApp responses, progress, notifications, and settings.

### Teacher

Can see their own schedule and assigned students; mark attendance; add lesson notes, homework, and skill assessments; request lesson changes; and see RSVP status for upcoming lessons.

A teacher must not access unrelated students, other teachers’ private data, or the full school financial data.

### Guardian / Parent

There is no guardian account and no parent application in the MVP. Guardians interact through WhatsApp and are resolved through the normalized phone number and the student-guardian relationship.

---

## Backend Organization

Organize the backend by business domain, not globally by technical layer:

~~~text
com.example.musicschool

shared/

auth/
    api/
    application/
    domain/
    infrastructure/

people/
    api/
    application/
    domain/
    infrastructure/

scheduling/
    api/
    application/
    domain/
    infrastructure/

attendance/
    api/
    application/
    domain/
    infrastructure/

billing/
    api/
    application/
    domain/
    infrastructure/

progress/
    api/
    application/
    domain/
    infrastructure/

messaging/
    api/
    application/
    domain/
    infrastructure/

dashboard/
    api/
    application/
    infrastructure/
~~~

Avoid a global controller/service/repository/entity/dto structure because it mixes business domains.

### API layer

Contains REST controllers, request/response DTOs, boundary validation, and mapping. It must not contain business rules.

### Application layer

Contains use cases, orchestration, transaction boundaries, authorization checks, domain loading, domain invocation, persistence, and integration ports.

Examples:

~~~text
CreateStudentUseCase
ScheduleLessonUseCase
RescheduleLessonUseCase
RequestLessonChangeUseCase
ApproveLessonChangeUseCase
RespondToLessonRsvpUseCase
MarkAttendanceUseCase
RecordPaymentUseCase
SendLessonReminderUseCase
~~~

Avoid giant service classes with unrelated methods. Prefer use-case-oriented application services.

### Domain layer

Contains entities, value objects, enums, invariants, and domain rules. It must not depend on Spring MVC, the Meta API, OpenAI, HTTP clients, or database implementations.

### Infrastructure layer

Contains JPA repositories, persistence mappings, database adapters, external API clients, the WhatsApp adapter, scheduler implementation, configuration, and observability.

---

## Domain Model

### Auth

Initial concepts:

~~~text
User
Role
Authentication
Authorization
Password management
~~~

Initial roles:

~~~text
ADMIN
TEACHER
~~~

Use secure password hashing, protected sessions/tokens, server-side authorization, and audit logging for sensitive changes.

### People

Core concepts:

~~~text
Student
Guardian
StudentGuardian
Teacher
Instrument
Enrollment
~~~

A student may have multiple guardians; a guardian may have multiple students. Normalize phone numbers to international form where possible.

Suggested student fields:

~~~text
id, firstName, lastName, birthDate, status, createdAt, updatedAt
~~~

Suggested guardian fields:

~~~text
id, firstName, lastName, phoneNumber, whatsappEnabled,
notificationConsent, createdAt, updatedAt
~~~

Suggested teacher fields:

~~~text
id, userId, firstName, lastName, status
~~~

### Scheduling

Scheduling is the center of the system. Use:

~~~text
LessonSeries
Lesson
LessonChangeRequest
TeacherAvailability
~~~

LessonSeries represents a recurring schedule such as “every Tuesday at 18:00”.

Suggested LessonSeries fields:

~~~text
id
studentId
teacherId
instrumentId
dayOfWeek
startTime
durationMinutes
effectiveFrom
effectiveUntil
status
~~~

Lesson represents a concrete occurrence.

Suggested Lesson fields:

~~~text
id
lessonSeriesId
studentId
teacherId
instrumentId
startAt
endAt
status
originalLessonId
createdAt
updatedAt
~~~

Suggested lesson statuses:

~~~text
NORMAL
RESCHEDULED
CANCELLED
COMPLETED
MAKEUP
~~~

Generate concrete occurrences for a rolling window such as the next 8–12 weeks rather than recalculating recurrence rules on every screen. Generation must be idempotent and must not create duplicates when run twice.

Validate teacher availability, student conflicts, teacher conflicts, valid duration, and valid time ranges. Preserve completed lesson history. Keep the original lesson reference when creating a rescheduled or make-up lesson.

A lesson-change request should contain:

~~~text
lessonId
requestedBy
reason
proposedStartAt
proposedEndAt
status
createdAt
resolvedAt
~~~

Suggested request statuses:

~~~text
PENDING
APPROVED
REJECTED
ALTERNATIVE_PROPOSED
PARENT_CONFIRMATION_PENDING
PARENT_ACCEPTED
PARENT_REJECTED
~~~

The administrator is the authority for schedule changes in the MVP.

### RSVP and actual attendance

Do not combine a parent’s intention with actual attendance.

Lesson RSVP:

~~~text
LessonRsvp
  id
  lessonId
  guardianId
  response
  respondedAt
  source
~~~

Responses:

~~~text
UNKNOWN
ATTENDING
NOT_ATTENDING
~~~

ATTENDING means the guardian expects the student to attend; it does not mean the student was present.

Actual attendance:

~~~text
LessonAttendance
  id
  lessonId
  status
  markedByTeacherId
  markedAt
  note
~~~

Statuses:

~~~text
PRESENT
ABSENT
EXCUSED
~~~

Flow:

~~~text
WhatsApp reminder
    -> Guardian selects Attending / Not attending
    -> LessonRsvp is updated
    -> Lesson occurs
    -> Teacher marks actual attendance
    -> LessonAttendance is stored
~~~

### Billing

Do not turn the MVP into a full accounting system.

Core concepts:

~~~text
FeePlan
Receivable
Payment
~~~

Fee-plan types:

~~~text
MONTHLY
PACKAGE
~~~

Suggested fee-plan fields:

~~~text
id, enrollmentId, billingType, amount, currency, dueDay,
packageLessonCount, activeFrom, activeUntil
~~~

Suggested receivable fields:

~~~text
id, enrollmentId, period, amount, dueDate, status
~~~

Statuses:

~~~text
UNPAID
PARTIAL
PAID
OVERDUE
CANCELLED
~~~

Suggested payment fields:

~~~text
id, receivableId, amount, paymentDate, method, reference, note, createdBy
~~~

Payment methods:

~~~text
CASH
TRANSFER
CARD
OTHER
~~~

The MVP supports manual recording and a “send WhatsApp reminder” action. Do not add online payment, bank reconciliation, e-invoicing, tax accounting, or accounting integration.

### Progress

Core concepts:

~~~text
LessonNote
SkillDefinition
SkillAssessment
PracticeAssignment
~~~

Common skills:

~~~text
RHYTHM
TEMPO_CONTROL
SIGHT_READING
MUSICAL_EXPRESSION
TECHNIQUE
PRACTICE_DISCIPLINE
~~~

Use a simple 1–5 scale and a short free-text note.

Instrument-specific examples:

~~~text
PIANO
  HAND_COORDINATION
  PEDAL_USE
  SIGHT_READING

GUITAR
  CHORD_TRANSITION
  PICKING
  FINGER_POSITION

DRUMS
  TIMING
  LIMB_INDEPENDENCE
  GROOVE_CONSISTENCY
~~~

A lesson may contain what was practiced, teacher note, homework, and next goal. The student profile should show a timeline of notes, missed lessons, goals, and skill changes.

### AI layer

Do not make AI part of the core domain. Create an adapter boundary:

~~~text
ProgressSummaryGenerator
~~~

A later implementation could be:

~~~text
OpenAIProgressSummaryGenerator
~~~

The first useful AI feature is a progress summary based on teacher-entered facts:

~~~text
Teacher data -> AI summary -> guardian-facing report
~~~

The AI must summarize supplied data, not invent progress, diagnose a student, or write directly to the database.

Do not implement real-time audio analysis in the MVP. Recording and analyzing piano, guitar, and drums is a separate product problem.

---

## WhatsApp Messaging

Keep messaging as its own module. Do not embed provider logic inside scheduling or billing.

Core concepts:

~~~text
NotificationJob
WhatsAppMessage
WhatsAppWebhookEvent
MessageTemplate
~~~

### NotificationJob

Use PostgreSQL as a simple persistent queue.

Suggested fields:

~~~text
id
type
recipientPhoneNumber
referenceType
referenceId
scheduledAt
status
attemptCount
lastError
sentAt
createdAt
updatedAt
~~~

Types:

~~~text
LESSON_REMINDER
LESSON_RESCHEDULED
MAKEUP_APPROVED
PAYMENT_REMINDER
BIRTHDAY
PACKAGE_ENDING
~~~

Statuses:

~~~text
PENDING
PROCESSING
SENT
FAILED
CANCELLED
~~~

Jobs must be idempotent. Concurrent workers must not process one job twice.

### One-hour lesson reminder

For a lesson at 18:00, create a job with scheduledAt 17:00, status PENDING, and type LESSON_REMINDER.

A scheduler runs approximately once per minute and claims due jobs safely. Use a transaction and a locking strategy such as FOR UPDATE SKIP LOCKED, or an equivalent safe approach.

### WhatsApp template

Use an approved Meta template for business-initiated messages outside the applicable customer-service window.

Example template name:

~~~text
lesson_reminder_rsvp
~~~

Example content:

~~~text
🎹 Ders Hatırlatması

Merhaba {{guardian_name}},

{{student_name}} öğrencimizin {{instrument}} dersi bugün
{{lesson_time}} saatinde.

Öğretmen: {{teacher_name}}

Katılım durumunuzu bildirir misiniz?
~~~

Quick replies:

~~~text
✅ Geliyorum
❌ Gelemiyorum
~~~

The wording may be localized to Turkish. Do not automate WhatsApp through a browser; use the official WhatsApp Business Platform / Cloud API.

### Webhook

Expose:

~~~text
GET  /api/webhooks/whatsapp
POST /api/webhooks/whatsapp
~~~

Processing flow:

~~~text
Receive webhook
    -> Verify provider signature
    -> Check provider-event idempotency
    -> Persist raw event safely
    -> Parse button payload or inbound text
    -> Resolve guardian, student, and lesson
    -> Execute the relevant use case
    -> Return a fast 2xx response
~~~

Validate the provider signature, such as X-Hub-Signature-256, before trusting the payload. Never log access tokens or unnecessary personal data.

Store events with:

~~~text
WhatsAppWebhookEvent
  id
  providerEventId
  eventType
  payloadJson
  receivedAt
  processedAt
  status
  processingError
~~~

Add a unique constraint on providerEventId.

Do not put predictable internal IDs in public button payloads. Use a random, opaque, or signed reference such as:

~~~text
rsvp_attending:e0b5c3...
~~~

Validate it server-side.

### Deterministic inbound questions

The first version should support a small set of deterministic intents:

~~~text
ders
aidat
telafi
okula yaz
~~~

Examples:

~~~text
Guardian: ders
System: Ece'nin sonraki piyano dersi 22 Ağustos Cumartesi 14:00.
        Öğretmen: Ayşe Hanım.

Guardian: aidat
System: Ağustos aidatı ödendi. Sonraki ödeme 5 Eylül — 2.000 TL.

Guardian: telafi
System: Kullanılabilir 1 telafi dersiniz bulunuyor.
~~~

Later, AI may classify natural-language requests into safe application commands such as getNextLesson(studentId). AI must not invent facts.

### No-response policy

Send one reminder approximately one hour before the lesson. Do not send a second message 15 minutes before the lesson in the initial version. Show “No response” on the dashboard. Make a second reminder an optional future setting.

---

## Dashboard

The dashboard is a read/query model, not a separate business aggregate. It must answer:

1. What is happening today?
2. What needs my attention?
3. What is coming soon?

Example:

~~~text
Today:
  24 lessons, 1 cancellation, 2 make-up lessons, 6 active teachers

Needs attention:
  8 overdue payments, 3 lesson-change requests,
  2 packages ending soon

Coming soon:
  3 birthdays, teacher leave on Thursday, upcoming recital
~~~

Example endpoint:

~~~text
GET /api/dashboard/today
~~~

Example response:

~~~json
{
  "todayLessons": 22,
  "attending": 15,
  "notAttending": 2,
  "noResponse": 5,
  "pendingChangeRequests": 3,
  "overduePayments": 8,
  "upcomingBirthdays": 2
}
~~~

Do not turn the dashboard into a BI project.

---

## Frontend UX

### Administrator

Provide a today dashboard, weekly calendar, student/guardian directory, teacher directory, lesson-change queue, payment list, notification status list, and student progress timeline.

### Teacher

The first screen should be My Lessons Today. For every lesson, keep the action flow short:

~~~text
Open lesson
    -> Present / Absent / Excused
    -> Short lesson note
    -> Homework / next goal
    -> Optional skill scores
    -> Save
~~~

Do not require teachers to fill twenty fields after every lesson.

### Calendar

Provide filters for All, Piano, Guitar, Drums, and individual teachers. Provide a normal availability query for finding alternative slots; AI is not required.

---

## Key Workflows

### Standard lesson

~~~text
Create recurring lesson series
    -> Generate concrete lesson occurrences
    -> Create one-hour reminder job
    -> Send WhatsApp template
    -> Guardian selects Attending / Not attending
    -> Store LessonRsvp
    -> Teacher marks actual attendance
    -> Teacher writes note and homework
    -> Optional skill assessment
~~~

### Lesson change

~~~text
Teacher or administrator requests change
    -> Validate alternative slot
    -> Administrator approves or rejects
    -> Update lesson and preserve history
    -> Send WhatsApp notification
    -> Optionally ask guardian for confirmation
~~~

If the guardian rejects a change, create or update a manager-facing request; do not silently change the schedule again.

### Payment

~~~text
Create fee plan
    -> Generate receivable
    -> Show unpaid / overdue status
    -> Optionally send WhatsApp reminder
    -> Administrator records payment
    -> Recalculate receivable status
~~~

### Progress

~~~text
Lesson occurs
    -> Teacher writes note
    -> Teacher updates skills
    -> Progress timeline updates
    -> Optional periodic summary is generated
~~~

---

## Suggested REST API

The exact names may be refined after the domain model is approved.

~~~text
POST /api/auth/login
POST /api/auth/logout
GET  /api/auth/me

GET    /api/students
POST   /api/students
GET    /api/students/{studentId}
PATCH  /api/students/{studentId}
GET    /api/students/{studentId}/timeline

GET    /api/guardians
POST   /api/guardians
PATCH  /api/guardians/{guardianId}

GET    /api/teachers
POST   /api/teachers
PATCH  /api/teachers/{teacherId}

GET    /api/instruments
POST   /api/instruments

GET    /api/calendar
GET    /api/lessons
POST   /api/lesson-series
PATCH  /api/lesson-series/{seriesId}
POST   /api/lessons/{lessonId}/change-requests
POST   /api/change-requests/{requestId}/approve
POST   /api/change-requests/{requestId}/reject
GET    /api/teachers/{teacherId}/availability

POST   /api/lessons/{lessonId}/attendance
POST   /api/lessons/{lessonId}/notes
POST   /api/students/{studentId}/skill-assessments
GET    /api/students/{studentId}/progress

GET    /api/receivables
POST   /api/receivables
POST   /api/receivables/{receivableId}/payments
GET    /api/students/{studentId}/billing
POST   /api/receivables/{receivableId}/send-reminder

GET    /api/notifications
POST   /api/notifications/{notificationId}/retry
GET    /api/webhooks/whatsapp
POST   /api/webhooks/whatsapp
~~~

---

## Persistence, Security, and Privacy

Use Flyway from the first commit.

Use UUIDs or another non-guessable public identifier strategy for externally exposed resources. Add timestamps to mutable records. Use optimistic locking or an equivalent safe strategy for concurrent edits.

Preserve historical records for completed lessons, attendance changes, payments, webhook events, and lesson-change decisions. Prefer inactive status or soft deletion for people/configuration where appropriate; do not delete financial or audit history.

Add constraints for unique provider event IDs, valid lesson ranges, valid payment amounts, relationship integrity, and no duplicate lesson occurrence for the same series and time.

Use timezone-aware timestamps and configure the school's timezone.

Implement secure password hashing, role-based authorization, request validation, HTTPS in deployed environments, webhook signature verification, environment-based secret management, safe logging, login/webhook abuse protection, and audit records for sensitive administrator operations.

Guardian phone numbers, children’s names, birth dates, attendance, and payment information are personal data. Minimize collection, restrict access, and do not expose student information merely because someone knows a lesson ID or message payload.

---

## Observability and Reliability

Use Spring Boot Actuator and structured logs.

Track notification job counts, notification failures and retry counts, webhook failures, scheduler failures, API error rates, and database connectivity.

Every external message should have an internal correlation/reference ID.

Retries must use bounded attempts and backoff. Failed jobs must remain visible to the administrator.

Return a fast success response from webhooks after safely recording the event. Long-running processing must not block the provider request.

---

## Testing Requirements

### Unit tests

Cover scheduling rules, overlap validation, RSVP transitions, attendance rules, payment status calculation, lesson-change approval rules, and WhatsApp payload parsing.

### Integration tests

Cover PostgreSQL repositories, Flyway migrations, safe scheduler job claiming, webhook signature verification, webhook idempotency, and role-based authorization. Use Testcontainers for database integration tests.

### End-to-end tests

At minimum:

1. Administrator creates a student, guardian, teacher, and lesson series.
2. The system generates a lesson and reminder job.
3. A WhatsApp RSVP is stored once even when the provider retries the event.
4. A teacher marks attendance and adds a note.
5. An administrator records a payment and the receivable changes state.
6. A lesson-change request is approved and a notification is created.

---

## Local Development and Deployment

The project should start locally with:

~~~text
docker compose up
~~~

The local environment should provide PostgreSQL, backend, and frontend.

Use environment variables for database credentials, session/token secrets, WhatsApp credentials, webhook verification token, timezone, school identity, and notification settings.

For local WhatsApp webhook development, document secure-tunnel usage without committing tunnel credentials.

Initial production shape:

~~~text
Cloudflare / HTTPS
    -> Frontend deployment
    -> Backend container
         -> Managed PostgreSQL
         -> Meta WhatsApp Cloud API
~~~

Daily managed database backups are preferred. Do not deploy Kubernetes or a distributed queue for the initial school.

---

## MVP Scope

Implement:

1. Dashboard
2. Student and guardian management
3. Teacher management
4. Instrument and enrollment management
5. Weekly lesson calendar
6. Recurring lesson series and concrete lessons
7. Attendance, cancellation, and make-up support
8. Lesson-change requests
9. Fee plans, receivables, and manual payments
10. One-hour WhatsApp lesson reminders
11. WhatsApp RSVP buttons
12. Webhook verification and idempotency
13. Lesson notes and homework
14. Student skill tracking
15. Birthdays and operational reminders
16. Role-based authentication and authorization
17. Audit-friendly history

---

## Explicitly Out of Scope for MVP

Do not implement native iOS/Android apps, parent or student portals, full accounting, e-invoicing, online payments, bank integration, CRM/lead management, teacher payroll, multi-branch support, multi-tenancy, microservices, Kafka/RabbitMQ/Redis/Kubernetes, workflow engines, an open-ended chatbot, real-time audio analysis, automatic transcription, AI diagnosis or grading, advanced BI/reporting, calendar synchronization, or SMS/email channels unless later required.

Leave extension points where useful, but do not implement speculative features.

---

## Implementation Order

### Phase 0 — Discovery and design

- confirm assumptions
- produce a domain glossary
- produce the module map
- produce the entity relationship model
- define roles and permissions
- define the lesson/RSVP/attendance state model
- define the first API surface
- define the migration plan
- identify unresolved product decisions

Do not write production code before this phase is reviewed.

### Phase 1 — Foundation

Project setup, Docker Compose, PostgreSQL, Flyway, authentication/authorization, shared error model, logging, Actuator, and test infrastructure.

### Phase 2 — People and scheduling

Students, guardians, teachers, instruments, enrollments, lesson series, concrete lessons, weekly calendar, and availability validation.

### Phase 3 — Attendance and changes

RSVP, attendance, teacher lesson screen, lesson notes, change requests, approval workflow, and make-up lessons.

### Phase 4 — Billing

Fee plans, receivables, payments, overdue calculations, and administrator payment views.

### Phase 5 — WhatsApp

Notification jobs, approved template integration, one-hour reminder, RSVP quick replies, webhook verification, idempotency, and deterministic inbound intents.

### Phase 6 — Progress and reminders

Skill definitions, assessments, progress timeline, birthdays, package-ending reminders, and an optional progress-summary adapter boundary.

### Phase 7 — Hardening

Integration and end-to-end tests, permission review, retry behavior, backup/restore procedure, deployment documentation, and operator documentation.

---

## Acceptance Criteria

The MVP is acceptable when:

- an administrator can create people and relationships
- an administrator can define a recurring schedule
- future concrete lessons are generated without duplicates
- teachers see only assigned lessons
- the administrator sees the whole weekly calendar
- parent RSVP is stored separately from actual attendance
- a teacher can record attendance and a short lesson note
- a teacher can request a lesson change
- an administrator can approve or reject it
- original lesson history is preserved
- an approved change creates the appropriate notification
- a guardian receives an official WhatsApp reminder
- quick replies update the correct lesson RSVP
- duplicate webhooks do not duplicate business effects
- an administrator can record cash, transfer, card, or other payments
- receivable statuses are correct
- the dashboard shows attending, not-attending, and no-response states
- birthdays and operational reminders are visible
- role restrictions are enforced server-side
- migrations and tests run from a clean environment
- Docker Compose starts the local system
- secrets are supplied through configuration and are not committed

---

## Required First Response from the Coding Agent

Before implementing, return a concise but complete design package containing:

1. assumptions and open questions
2. module boundaries
3. entity relationship diagram in Mermaid
4. proposed database tables and important constraints
5. role and permission matrix
6. lesson / RSVP / attendance state model
7. WhatsApp reminder and webhook sequence diagram
8. initial REST endpoint list
9. first migration order
10. testing strategy
11. implementation phases
12. risks and decisions requiring approval

Do not silently make major product decisions. If an assumption is safe and reversible, state it and proceed. If it changes scope, privacy, money, or external communication, surface it for approval.

---

## Engineering Style

Prefer clear names, small use cases, explicit state transitions, immutable value objects where appropriate, database constraints for invariants, transactional boundaries, idempotent integrations, readable code, meaningful tests, and simple operational behavior.

Avoid speculative abstractions, generic base classes with no business value, hidden side effects, giant service classes, duplicated business rules, direct external API calls from controllers, hard-coded credentials, business logic in frontend components, AI-generated facts, and premature distributed systems.

The final result should be simple enough for a small school to use every day, while structured well enough to support a future 20–30 teacher school without throwing away the core domain model.

