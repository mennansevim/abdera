using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
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

    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AbderaDbContext).Assembly);
    }
}
