using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// CLAUDE.md: tek AbderaDbContext, modül başına ayrı context yok. Repository pattern yok -
// handler'lar bu context'i doğrudan kullanır. Yeni bir modül eklendikçe buraya DbSet eklenir
// ve ApplyConfigurationsFromAssembly ilgili Persistence/*Configuration.cs dosyasını otomatik bulur.
public class AbderaDbContext : DbContext
{
    public AbderaDbContext(DbContextOptions<AbderaDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<GuardianLoginCode> GuardianLoginCodes => Set<GuardianLoginCode>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<TeacherInstrument> TeacherInstruments => Set<TeacherInstrument>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    public DbSet<TeacherAvailability> TeacherAvailabilities => Set<TeacherAvailability>();
    public DbSet<TeacherTimeOff> TeacherTimeOffs => Set<TeacherTimeOff>();
    public DbSet<SchoolCalendarDay> SchoolCalendarDays => Set<SchoolCalendarDay>();
    public DbSet<LessonSeries> LessonSeries => Set<LessonSeries>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonChangeRequest> LessonChangeRequests => Set<LessonChangeRequest>();

    public DbSet<LessonRsvp> LessonRsvps => Set<LessonRsvp>();
    public DbSet<LessonAttendance> LessonAttendances => Set<LessonAttendance>();

    public DbSet<LessonNote> LessonNotes => Set<LessonNote>();

    public DbSet<MakeupCredit> MakeupCredits => Set<MakeupCredit>();
    public DbSet<FeePlan> FeePlans => Set<FeePlan>();
    public DbSet<Receivable> Receivables => Set<Receivable>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();

    public DbSet<NotificationJob> NotificationJobs => Set<NotificationJob>();
    public DbSet<WhatsAppMessage> WhatsAppMessages => Set<WhatsAppMessage>();
    public DbSet<WhatsAppWebhookEvent> WhatsAppWebhookEvents => Set<WhatsAppWebhookEvent>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<NotificationAutomationSettings> NotificationAutomationSettings => Set<NotificationAutomationSettings>();

    public DbSet<VirtualIban> VirtualIbans => Set<VirtualIban>();
    public DbSet<BankIncomingTransaction> BankIncomingTransactions => Set<BankIncomingTransaction>();

    public DbSet<BackupRun> BackupRuns => Set<BackupRun>();
    public DbSet<SystemHealthStatus> SystemHealthStatuses => Set<SystemHealthStatus>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AbderaDbContext).Assembly);
    }
}
