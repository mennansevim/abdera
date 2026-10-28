using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using static Abdera.Api.Shared.SpreadsheetImport;

namespace Abdera.Api.Modules.People.Features;

// Excel'den öğrenci + veli + kurs kaydı içe aktarma (docs/10-decisions.md AF). Yalnızca Admin.
// Satır başına bir kurs kaydı: iki enstrüman çalan öğrenci iki satırda yazılır (aynı ad, soyad,
// doğum tarihi), öğrenci bir kez açılır. Sistemde zaten olan öğrenci (aynı ad soyad + aynı doğum
// tarihi ya da aynı veli telefonu) atlanır, üzerine yazılmaz. Veli telefonu sistemde kayıtlıysa
// yeni veli açılmaz, öğrenci o veliye bağlanır (kardeş).
//
// Kurs kaydı Enrollments.CreateAsync'in kurallarıyla açılır (aktif öğretmen, öğretmen o enstrümanı
// öğretiyor, "enrollment.created" audit'i, bu ayın aidatı EnrollmentReceivableOpener ile).
// Veliye hiçbir mesaj gitmez: karşılama mesajı yalnızca yönetici şifre verdiğinde gönderilir.
public static class StudentImport
{
    private const string FirstName = "Öğrenci Adı";
    private const string LastName = "Öğrenci Soyadı";
    private const string BirthDate = "Doğum Tarihi";
    private const string GuardianFirstName = "Veli Adı";
    private const string GuardianLastName = "Veli Soyadı";
    private const string GuardianPhone = "Veli Telefonu";
    private const string Relationship = "Yakınlık";
    private const string Instrument = "Enstrüman";
    private const string Teacher = "Öğretmen";
    private const string Kind = "Ders Türü";
    private const string StartDate = "Başlangıç Tarihi";

    public static readonly IReadOnlyList<Column> Columns =
    [
        new(FirstName, true, "Öğrencinin adı.", "Zeynep"),
        new(LastName, true, "Öğrencinin soyadı.", "Kaya"),
        new(BirthDate, true, "Gün.ay.yıl ya da Excel tarih hücresi.", "12.03.2015"),
        new(GuardianFirstName, false, "Velinin adı. Telefon yazıldıysa zorunlu.", "Ayşe"),
        new(GuardianLastName, false, "Velinin soyadı. Telefon yazıldıysa zorunlu.", "Kaya"),
        new(GuardianPhone, false, "Sistemde kayıtlı bir numaraysa öğrenci o veliye bağlanır (kardeş).", "0532 123 45 67"),
        new(Relationship, false, "Anne, Baba vb.", "Anne"),
        new(Instrument, false, "Sistemdeki enstrüman adı. Boşsa kurs kaydı açılmaz.", "Piyano"),
        new(Teacher, false, "Öğretmenin adı soyadı. Enstrüman yazıldıysa zorunlu.", "Ali Yılmaz"),
        new(Kind, false, "Birebir ya da Grup. Boşsa Birebir.", "Birebir"),
        new(StartDate, false, "Kurs başlangıcı. Boşsa bugün.", "01.09.2026"),
    ];

    public static void MapStudentImport(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports/students").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("/template", () => Template("ogrenci-veli-sablonu.xlsx", "Öğrenciler", Columns));
        group.MapPost("/preview", (IFormFile? file, ClaimsPrincipal principal, AbderaDbContext db, IClock clock) =>
            HandleAsync(file, commit: false, principal, db, clock)).DisableAntiforgery();
        group.MapPost("", (IFormFile? file, ClaimsPrincipal principal, AbderaDbContext db, IClock clock) =>
            HandleAsync(file, commit: true, principal, db, clock)).DisableAntiforgery();
    }

    private sealed record ExistingStudent(Guid Id, DateOnly BirthDate, List<string> GuardianPhones);
    private sealed record PlannedStudent(Student Student, Guardian? Guardian);

    private static async Task<IResult> HandleAsync(IFormFile? file, bool commit, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var rows = await ReadAsync(file, Columns);
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        var actorId = AuthContext.GetUserId(principal);

        var instruments = await db.Instruments.AsNoTracking().ToListAsync();
        var teachers = await db.Teachers.AsNoTracking().ToListAsync();
        var teaches = (await db.TeacherInstruments.AsNoTracking().Select(ti => new { ti.TeacherId, ti.InstrumentId }).ToListAsync())
            .Select(ti => (ti.TeacherId, ti.InstrumentId)).ToHashSet();
        var guardiansByPhone = await db.Guardians.AsNoTracking().ToDictionaryAsync(g => g.PhoneNumber);
        var existingStudents = (await (
                from student in db.Students.AsNoTracking()
                join link in db.StudentGuardians on student.Id equals link.StudentId into links
                from link in links.DefaultIfEmpty()
                join guardian in db.Guardians on link.GuardianId equals guardian.Id into guardians
                from guardian in guardians.DefaultIfEmpty()
                select new { student.Id, student.FirstName, student.LastName, student.BirthDate, Phone = guardian == null ? null : guardian.PhoneNumber })
                .ToListAsync())
            .GroupBy(row => (Fold(row.FirstName), Fold(row.LastName)))
            .ToDictionary(
                group => group.Key,
                group => group.GroupBy(row => row.Id)
                    .Select(student => new ExistingStudent(student.Key, student.First().BirthDate,
                        student.Where(row => row.Phone != null).Select(row => row.Phone!).ToList()))
                    .ToList());

        var results = new List<RowResult>();
        // Dosyanın içinde aynı öğrencinin ikinci satırı (ikinci enstrüman) aynı öğrenciye bağlanır.
        var plannedStudents = new Dictionary<string, PlannedStudent>();
        var plannedGuardians = new Dictionary<string, Guardian>();
        var plannedEnrollments = new HashSet<(string StudentKey, Guid TeacherId, Guid InstrumentId)>();
        var newEnrollments = new List<Enrollment>();

        foreach (var row in rows)
        {
            var errors = new List<string>();
            var firstName = row.Text(FirstName);
            var lastName = row.Text(LastName);
            var label = $"{firstName} {lastName}".Trim();
            if (label.Length == 0) label = "(adsız)";
            if (firstName is null) errors.Add($"{FirstName} boş.");
            if (lastName is null) errors.Add($"{LastName} boş.");
            var birthDate = row.Date(BirthDate, out var birthError);
            if (birthError is not null) errors.Add(birthError);
            else if (birthDate is null) errors.Add($"{BirthDate} boş.");

            var guardianFirst = row.Text(GuardianFirstName);
            var guardianLast = row.Text(GuardianLastName);
            var rawPhone = row.Text(GuardianPhone);
            string? phone = null;
            if (rawPhone is not null)
            {
                try { phone = PhoneNumberNormalizer.Normalize(rawPhone); }
                catch (ArgumentException ex) { errors.Add(MessageOf(ex)); }
            }
            else if (guardianFirst is not null || guardianLast is not null)
            {
                errors.Add($"Veli yazıldıysa {GuardianPhone} zorunlu.");
            }

            var instrumentName = row.Text(Instrument);
            var teacherName = row.Text(Teacher);
            Instrument? instrument = null;
            Teacher? teacher = null;
            var courseKind = CourseKind.Individual;
            var startedAt = today;
            if (instrumentName is not null)
            {
                instrument = instruments.FirstOrDefault(i => Fold(i.Name) == Fold(instrumentName));
                if (instrument is null) errors.Add($"'{instrumentName}' adında bir enstrüman yok.");

                if (teacherName is null)
                {
                    errors.Add($"Enstrüman yazıldıysa {Teacher} zorunlu.");
                }
                else
                {
                    var matches = teachers.Where(t => Fold($"{t.FirstName} {t.LastName}") == Fold(teacherName)).ToList();
                    teacher = matches.Count == 1 ? matches[0] : null;
                    if (matches.Count == 0) errors.Add($"'{teacherName}' adında bir öğretmen yok.");
                    else if (matches.Count > 1) errors.Add($"'{teacherName}' adında birden fazla öğretmen var.");
                    else if (teacher!.Status != TeacherStatus.Active) errors.Add($"{teacherName} aktif değil.");
                    else if (instrument is not null && !teaches.Contains((teacher.Id, instrument.Id)))
                        errors.Add($"{teacherName} {instrument.Name} öğretmiyor.");
                }

                var kindText = Fold(row.Text(Kind));
                if (kindText is "grup") courseKind = CourseKind.Group;
                else if (kindText is not ("" or "birebir")) errors.Add($"{Kind} Birebir ya da Grup olmalı.");

                var start = row.Date(StartDate, out var startError);
                if (startError is not null) errors.Add(startError);
                else if (start is { } value) startedAt = value;
            }
            else if (teacherName is not null)
            {
                errors.Add($"Öğretmen yazıldıysa {Instrument} zorunlu.");
            }

            if (errors.Count > 0)
            {
                results.Add(new RowResult(row.Number, label, RowStatus.Error, string.Join(" ", errors)));
                continue;
            }

            var key = $"{Fold(firstName)}|{Fold(lastName)}|{birthDate:yyyy-MM-dd}";
            var alreadyInSystem = existingStudents.GetValueOrDefault((Fold(firstName), Fold(lastName)))?
                .FirstOrDefault(existing => existing.BirthDate == birthDate || (phone is not null && existing.GuardianPhones.Contains(phone)));
            if (alreadyInSystem is not null)
            {
                results.Add(new RowResult(row.Number, label, RowStatus.Skipped, "Sistemde zaten var; dokunulmadı."));
                continue;
            }

            var notes = new List<string>();
            plannedStudents.TryGetValue(key, out var planned);
            Student? newStudent = null;
            Guardian? newGuardian = null;
            Guardian? linkedGuardian = null;
            if (planned is null)
            {
                try
                {
                    newStudent = Student.Create(firstName!, lastName!, birthDate!.Value, now);
                    if (phone is not null)
                    {
                        if (guardiansByPhone.TryGetValue(phone, out var known))
                        {
                            linkedGuardian = known;
                            notes.Add($"kayıtlı veli: {known.FirstName} {known.LastName}");
                        }
                        else if (plannedGuardians.TryGetValue(phone, out var sibling))
                        {
                            linkedGuardian = sibling;
                            notes.Add("dosyadaki kardeşiyle aynı veli");
                        }
                        else
                        {
                            if (guardianFirst is null || guardianLast is null)
                                throw new ArgumentException($"Yeni veli için {GuardianFirstName} ve {GuardianLastName} zorunlu.");
                            newGuardian = Guardian.Create(guardianFirst, guardianLast, rawPhone!, now);
                            notes.Add("yeni veli");
                        }
                    }
                    else
                    {
                        notes.Add("velisiz");
                    }
                }
                catch (ArgumentException ex)
                {
                    results.Add(new RowResult(row.Number, label, RowStatus.Error, MessageOf(ex)));
                    continue;
                }
            }
            else if (instrument is null)
            {
                results.Add(new RowResult(row.Number, label, RowStatus.Skipped, "Dosyada bu öğrenci daha önce geçiyor."));
                continue;
            }
            else
            {
                notes.Add("aynı öğrenciye ek kurs");
            }

            Enrollment? enrollment = null;
            if (instrument is not null)
            {
                if (!plannedEnrollments.Add((key, teacher!.Id, instrument.Id)))
                {
                    results.Add(new RowResult(row.Number, label, RowStatus.Skipped, "Aynı kurs kaydı dosyada iki kez geçiyor."));
                    continue;
                }
                notes.Add($"{instrument.Name} · {teacher.FirstName} {teacher.LastName} · {(courseKind == CourseKind.Group ? "Grup" : "Birebir")}");
            }

            if (planned is null)
            {
                var guardian = newGuardian ?? linkedGuardian;
                planned = new PlannedStudent(newStudent!, guardian);
                plannedStudents[key] = planned;
                if (newGuardian is not null) plannedGuardians[phone!] = newGuardian;
                if (commit)
                {
                    db.Students.Add(newStudent!);
                    if (newGuardian is not null) db.Guardians.Add(newGuardian);
                    if (guardian is not null)
                        db.StudentGuardians.Add(StudentGuardian.Create(newStudent!.Id, guardian.Id, row.Text(Relationship), isPrimary: true));
                }
            }

            if (instrument is not null)
            {
                enrollment = Enrollment.Create(planned.Student.Id, teacher!.Id, instrument.Id, courseKind, startedAt, now);
                if (commit)
                {
                    db.Enrollments.Add(enrollment);
                    newEnrollments.Add(enrollment);
                    db.AuditLogs.Add(AuditLog.Record(
                        actorId, "enrollment.created", nameof(Enrollment), enrollment.Id, now,
                        afterJson: JsonSerializer.Serialize(new
                        {
                            enrollment.StudentId,
                            enrollment.TeacherId,
                            enrollment.InstrumentId,
                            CourseKind = enrollment.CourseKind.ToString(),
                            enrollment.StartedAt,
                            Status = enrollment.Status.ToString(),
                            Source = "excel_import",
                        })));
                }
            }

            results.Add(new RowResult(row.Number, label, commit ? RowStatus.Imported : RowStatus.Ready, string.Join(" · ", notes)));
        }

        if (!commit || plannedStudents.Count == 0)
            return Results.Ok(Report.From(committed: commit, results));

        await using var transaction = await db.Database.BeginTransactionAsync();
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "import.students", "Import", Guid.NewGuid(), now,
            afterJson: JsonSerializer.Serialize(new
            {
                fileName = file!.FileName,
                students = plannedStudents.Count,
                guardians = plannedGuardians.Count,
                enrollments = newEnrollments.Count,
                skipped = results.Count(r => r.Status == RowStatus.Skipped),
                errors = results.Count(r => r.Status == RowStatus.Error),
            })));
        await db.SaveChangesAsync();
        // Kurs kaydı Enrollments.CreateAsync'teki gibi önce kaydedilir: çoklu kurs indirimi
        // aynı dosyadaki ikinci kaydı da saysın.
        foreach (var enrollment in newEnrollments)
            await EnrollmentReceivableOpener.OpenCurrentPeriodAsync(db, clock, enrollment.Id, actorId);
        await transaction.CommitAsync();

        return Results.Ok(Report.From(committed: true, results));
    }
}
