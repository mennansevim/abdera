using Abdera.Api.Modules.Library.Domain;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Show.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// CLAUDE.md: tek AbderaDbContext, modül başına ayrı context yok. Repository pattern yok -
// handler'lar bu context'i doğrudan kullanır. Yeni bir modül eklendikçe buraya DbSet eklenir
// ve ApplyConfigurationsFromAssembly ilgili Persistence/*Configuration.cs dosyasını otomatik bulur.
public class AbderaDbContext : DbContext, IDataProtectionKeyContext
{
    public AbderaDbContext(DbContextOptions<AbderaDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<GuardianLoginCode> GuardianLoginCodes => Set<GuardianLoginCode>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherInstrument> TeacherInstruments => Set<TeacherInstrument>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<StudentDeletionRequest> StudentDeletionRequests => Set<StudentDeletionRequest>();
    public DbSet<InstrumentMaintenanceSetting> InstrumentMaintenanceSettings => Set<InstrumentMaintenanceSetting>();
    public DbSet<InstrumentMaintenanceReminder> InstrumentMaintenanceReminders => Set<InstrumentMaintenanceReminder>();

    public DbSet<TeacherAvailability> TeacherAvailabilities => Set<TeacherAvailability>();
    public DbSet<TeacherTimeOff> TeacherTimeOffs => Set<TeacherTimeOff>();
    public DbSet<SchoolCalendarDay> SchoolCalendarDays => Set<SchoolCalendarDay>();
    public DbSet<LessonSeries> LessonSeries => Set<LessonSeries>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonChangeRequest> LessonChangeRequests => Set<LessonChangeRequest>();

    public DbSet<LessonRsvp> LessonRsvps => Set<LessonRsvp>();
    public DbSet<LessonAttendance> LessonAttendances => Set<LessonAttendance>();

    public DbSet<LessonNote> LessonNotes => Set<LessonNote>();
    public DbSet<SkillDefinition> SkillDefinitions => Set<SkillDefinition>();
    public DbSet<SkillAssessment> SkillAssessments => Set<SkillAssessment>();
    public DbSet<PracticeAssignment> PracticeAssignments => Set<PracticeAssignment>();
    public DbSet<PracticeJournalEntry> PracticeJournalEntries => Set<PracticeJournalEntry>();
    public DbSet<ProgressSummary> ProgressSummaries => Set<ProgressSummary>();

    public DbSet<MakeupCredit> MakeupCredits => Set<MakeupCredit>();
    public DbSet<TuitionRate> TuitionRates => Set<TuitionRate>();
    public DbSet<BillingSettings> BillingSettings => Set<BillingSettings>();
    public DbSet<PrepayDiscountTier> PrepayDiscountTiers => Set<PrepayDiscountTier>();
    public DbSet<Receivable> Receivables => Set<Receivable>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentCorrection> PaymentCorrections => Set<PaymentCorrection>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();
    public DbSet<RecurringExpenseAmount> RecurringExpenseAmounts => Set<RecurringExpenseAmount>();

    public DbSet<NotificationJob> NotificationJobs => Set<NotificationJob>();
    public DbSet<StaffNotification> StaffNotifications => Set<StaffNotification>();
    public DbSet<WhatsAppMessage> WhatsAppMessages => Set<WhatsAppMessage>();
    public DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents => Set<WhatsAppWebhookEvent>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<NotificationAutomationSettings> NotificationAutomationSettings => Set<NotificationAutomationSettings>();

    public DbSet<VirtualIban> VirtualIbans => Set<VirtualIban>();
    public DbSet<BankIncomingTransaction> BankIncomingTransactions => Set<BankIncomingTransaction>();

    public DbSet<ShowEvent> ShowEvents => Set<ShowEvent>();
    public DbSet<ShowItem> ShowItems => Set<ShowItem>();
    public DbSet<StudentPhoto> StudentPhotos => Set<StudentPhoto>();

    public DbSet<ScoreFile> ScoreFiles => Set<ScoreFile>();
    public DbSet<LibraryPiece> LibraryPieces => Set<LibraryPiece>();
    public DbSet<LibrarySuggestion> LibrarySuggestions => Set<LibrarySuggestion>();

    public DbSet<BackupRun> BackupRuns => Set<BackupRun>();
    public DbSet<SystemHealthStatus> SystemHealthStatuses => Set<SystemHealthStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AbderaDbContext).Assembly);
    }
}
