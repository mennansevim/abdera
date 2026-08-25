using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// Development-only fixture for a realistic six-month school history. It is deliberately
// exposed as an explicit, admin-protected action instead of running on every app startup:
// test databases stay clean and an operator never gets surprise rows after a restart.
public static class DevelopmentMockData
{
    private const string MarkerEmail = "mock.ayse.kaya@abdera.local";
    private const string DemoGuardianPhone = "+905550000001";
    private const string DemoPassword = "DemoTeacher123!";

    private static readonly TeacherSpec[] TeacherSpecs =
    [
        new("Ayşe", "Kaya", "mock.ayse.kaya@abdera.local", "PIANO"),
        new("Mert", "Yılmaz", "mock.mert.yilmaz@abdera.local", "GUITAR"),
        new("Selin", "Demir", "mock.selin.demir@abdera.local", "VIOLIN"),
    ];

    private static readonly GuardianSpec[] GuardianSpecs =
    [
        new("Demo", "Veli", DemoGuardianPhone),
        new("Elif", "Arslan", "+905551000001"),
        new("Can", "Aydın", "+905551000002"),
        new("Buse", "Çetin", "+905551000003"),
        new("Ozan", "Kurt", "+905551000004"),
        new("Derya", "Şahin", "+905551000005"),
        new("Pelin", "Koç", "+905551000006"),
        new("Burak", "Eren", "+905551000007"),
        new("Seda", "Aksoy", "+905551000008"),
        new("Hakan", "Öztürk", "+905551000009"),
    ];

    private static readonly StudentSpec[] StudentSpecs =
    [
        new("Lara", "Arslan", new DateOnly(2015, 3, 14), 0, 0, "Anne"),
        new("Emir", "Aydın", new DateOnly(2012, 9, 7), 1, 1, "Baba"),
        new("Ada", "Çetin", new DateOnly(2016, 1, 22), 2, 2, "Anne"),
        new("Ege", "Kurt", new DateOnly(2011, 11, 3), 0, 3, "Baba"),
        new("Mina", "Şahin", new DateOnly(2014, 6, 19), 1, 4, "Anne"),
        new("Aras", "Koç", new DateOnly(2013, 2, 11), 2, 5, "Baba"),
        new("Defne", "Eren", new DateOnly(2017, 8, 29), 0, 6, "Anne"),
        new("Kerem", "Aksoy", new DateOnly(2010, 12, 16), 1, 7, "Baba"),
        new("İpek", "Öztürk", new DateOnly(2015, 10, 5), 2, 8, "Anne"),
        new("Deniz", "Arslan", new DateOnly(2013, 4, 27), 0, 1, "Baba"),
        new("Lina", "Koç", new DateOnly(2016, 12, 2), 1, 5, "Anne"),
        new("Bora", "Aydın", new DateOnly(2014, 9, 13), 2, 2, "Baba"),
    ];

    private static readonly Dictionary<string, string[]> Pieces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PIANO"] = ["Do Majör Gam", "Bach - Minuet in G", "Clementi - Sonatina", "Für Elise"],
        ["GUITAR"] = ["Akor geçişleri", "Greensleeves", "Romance Anonimo", "Spanish Romance"],
        ["VIOLIN"] = ["Re Majör Gam", "Suzuki Minuet", "Gavotte", "Vivaldi - Spring"],
        ["DRUMS"] = ["Sekizlik ritimler", "Basic Rock Groove", "Shuffle Groove", "Funk Coordination"],
    };

    public record SeedResponse(
        string Status,
        DateOnly From,
        DateOnly To,
        int Teachers,
        int Guardians,
        int Students,
        int Enrollments,
        int Lessons,
        int AttendanceRecords,
        int ProgressNotes,
        int GuardianMessages,
        int Receivables,
        int Payments);

    private sealed record TeacherSpec(string FirstName, string LastName, string Email, string InstrumentCode);
    private sealed record GuardianSpec(string FirstName, string LastName, string PhoneNumber);
    private sealed record StudentSpec(string FirstName, string LastName, DateOnly BirthDate, int TeacherIndex, int GuardianIndex, string Relationship);

    public static void MapDevelopmentMockData(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/mock-data/seed", SeedAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> SeedAsync(
        AbderaDbContext db,
        IPasswordHasher<User> passwordHasher,
        IClock clock,
        ClaimsPrincipal principal)
    {
        var schoolZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        if (await db.Users.AnyAsync(user => user.Email == MarkerEmail))
        {
            var alreadySeededToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, schoolZone).DateTime);
            return Results.Ok(new SeedResponse("already-seeded", alreadySeededToday.AddMonths(-6), alreadySeededToday, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }

        var instruments = await db.Instruments.ToDictionaryAsync(instrument => instrument.Code, StringComparer.OrdinalIgnoreCase);
        if (!instruments.ContainsKey("PIANO") || !instruments.ContainsKey("GUITAR") || !instruments.ContainsKey("VIOLIN"))
        {
            throw new InvalidOperationException("Mock veri için enstrüman seed verileri bulunamadı.");
        }

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, schoolZone).DateTime);
        var from = today.AddMonths(-6);
        var lessonUntil = today.AddDays(28);
        var random = new Random(20260825);

        var teachers = new List<Teacher>();
        foreach (var spec in TeacherSpecs)
        {
            var user = await db.Users.SingleOrDefaultAsync(item => item.Email == spec.Email);
            if (user is null)
            {
                user = User.Create(spec.Email, "placeholder", UserRole.Teacher, now);
                user.SetPassword(passwordHasher.HashPassword(user, DemoPassword), now);
                db.Users.Add(user);
            }

            var teacher = await db.Teachers.SingleOrDefaultAsync(item => item.UserId == user.Id);
            if (teacher is null)
            {
                teacher = Teacher.Create(spec.FirstName, spec.LastName, now, user.Id);
                db.Teachers.Add(teacher);
            }

            var instrument = instruments[spec.InstrumentCode];
            if (!await db.TeacherInstruments.AnyAsync(item => item.TeacherId == teacher.Id && item.InstrumentId == instrument.Id))
            {
                db.TeacherInstruments.Add(TeacherInstrument.Create(teacher.Id, instrument.Id));
            }

            if (!await db.TeacherAvailabilities.AnyAsync(item => item.TeacherId == teacher.Id))
            {
                // Okul Pazartesi-Cumartesi açık. Cumartesi de gerçek bir ders günü olduğu için
                // öğretmen uygunluğu ve örnek program haftanın altı gününe yayılır.
                for (var day = 1; day <= 6; day++)
                {
                    db.TeacherAvailabilities.Add(TeacherAvailability.Create(
                        teacher.Id,
                        (DayOfWeek)day,
                        new TimeOnly(16 + ((day + teachers.Count) % 2), 0),
                        new TimeOnly(20, 0)));
                }
            }

            teachers.Add(teacher);
        }

        var guardians = new List<Guardian>();
        foreach (var spec in GuardianSpecs)
        {
            var guardian = await db.Guardians.SingleOrDefaultAsync(item => item.PhoneNumber == spec.PhoneNumber);
            if (guardian is null)
            {
                guardian = Guardian.Create(spec.FirstName, spec.LastName, spec.PhoneNumber, now);
                db.Guardians.Add(guardian);
            }

            guardians.Add(guardian);
        }

        var counts = new SeedCounts();
        var students = new List<(Student Student, Enrollment Enrollment, Guardian Guardian, Teacher Teacher, Instrument Instrument)>();

        for (var index = 0; index < StudentSpecs.Length; index++)
        {
            var spec = StudentSpecs[index];
            var student = await db.Students.SingleOrDefaultAsync(item => item.FirstName == spec.FirstName && item.LastName == spec.LastName);
            if (student is null)
            {
                student = Student.Create(spec.FirstName, spec.LastName, spec.BirthDate, now);
                db.Students.Add(student);
            }

            var guardian = guardians[spec.GuardianIndex];
            if (!await db.StudentGuardians.AnyAsync(item => item.StudentId == student.Id && item.GuardianId == guardian.Id))
            {
                db.StudentGuardians.Add(StudentGuardian.Create(student.Id, guardian.Id, spec.Relationship, isPrimary: true));
            }

            var teacher = teachers[spec.TeacherIndex];
            var teacherSpec = TeacherSpecs[spec.TeacherIndex];
            var instrument = instruments[teacherSpec.InstrumentCode];
            var enrollment = await db.Enrollments.SingleOrDefaultAsync(item =>
                item.StudentId == student.Id && item.TeacherId == teacher.Id && item.InstrumentId == instrument.Id);
            if (enrollment is null)
            {
                enrollment = Enrollment.Create(student.Id, teacher.Id, instrument.Id, from, now);
                db.Enrollments.Add(enrollment);
            }

            students.Add((student, enrollment, guardian, teacher, instrument));
            counts.Students++;
            counts.Enrollments++;
        }

        await db.SaveChangesAsync();

        // Aidat ekranının tüm durumları ilk açılışta görülebilsin diye 12 öğrenciye ikişer
        // dönem olmak üzere 24 deterministik kayıt oluşturulur. Marker kullanıcı kontrolü
        // nedeniyle endpoint tekrar çağrıldığında duplicate üretilmez.
        var actorId = AuthContext.GetUserId(principal);
        var priceList = PriceList.Create("Demo 2026–2027", from, null, actorId, now);
        db.PriceLists.Add(priceList);
        var priceItems = instruments.Values.ToDictionary(
            instrument => instrument.Id,
            instrument => PriceListItem.Create(
                priceList.Id,
                instrument.Id,
                50,
                BillingType.Monthly,
                instrument.Code switch
                {
                    "PIANO" => 3200m,
                    "VIOLIN" => 3000m,
                    "GUITAR" => 2800m,
                    _ => 2900m,
                },
                "TRY",
                null));
        db.PriceListItems.AddRange(priceItems.Values);

        for (var studentIndex = 0; studentIndex < students.Count; studentIndex++)
        {
            var (student, enrollment, _, _, instrument) = students[studentIndex];
            var item = priceItems[instrument.Id];
            var feePlan = FeePlan.CreateFromPriceListItem(enrollment.Id, item, 5, from, now);
            db.FeePlans.Add(feePlan);

            for (var periodIndex = 0; periodIndex < 2; periodIndex++)
            {
                var scenario = (studentIndex * 2 + periodIndex) % 4;
                var periodMonth = today.AddMonths(periodIndex - 1);
                var dueDate = scenario switch
                {
                    1 or 3 => today.AddDays(12 + periodIndex),
                    _ => today.AddDays(-12 - periodIndex),
                };
                var receivable = Receivable.Create(
                    enrollment.Id,
                    feePlan.Id,
                    item.Id,
                    periodMonth.ToString("yyyy-MM"),
                    item.Amount,
                    item.Currency,
                    dueDate,
                    now);
                db.Receivables.Add(receivable);

                if (scenario == 0)
                {
                    db.Payments.Add(Payment.Create(
                        receivable.Id,
                        item.Amount,
                        dueDate,
                        PaymentMethod.Transfer,
                        $"demo-paid-{student.Id:N}-{periodIndex}",
                        "Demo tam ödeme",
                        actorId,
                        now));
                    receivable.RecordPaymentEffect(item.Amount, now);
                    counts.Payments++;
                }
                else if (scenario == 1)
                {
                    var paid = Math.Round(item.Amount * .4m, 2);
                    db.Payments.Add(Payment.Create(
                        receivable.Id,
                        paid,
                        today,
                        PaymentMethod.Cash,
                        null,
                        "Demo kısmi ödeme",
                        actorId,
                        now));
                    receivable.RecordPaymentEffect(paid, now);
                    counts.Payments++;
                }
                else if (scenario == 2)
                {
                    receivable.MarkOverdueIfPastDue(today, now);
                }

                counts.Receivables++;
            }
        }

        await db.SaveChangesAsync();

        for (var index = 0; index < students.Count; index++)
        {
            var item = students[index];
            var dayOfWeek = (DayOfWeek)(1 + (index % 6));
            var startTime = new TimeOnly(16 + (index % 4), index % 2 == 0 ? 0 : 30);
            var series = await db.LessonSeries.SingleOrDefaultAsync(candidate => candidate.EnrollmentId == item.Enrollment.Id);
            if (series is null)
            {
                series = LessonSeries.Create(item.Enrollment.Id, dayOfWeek, startTime, 50, from, lessonUntil, now);
                db.LessonSeries.Add(series);
                await db.SaveChangesAsync();
            }

            var firstLessonDate = NextOnOrAfter(from, dayOfWeek);
            var lessonIndex = 0;
            for (var date = firstLessonDate; date <= lessonUntil; date = date.AddDays(7), lessonIndex++)
            {
                var startAt = ToUtc(date, startTime, schoolZone);
                var endAt = ToUtc(date, startTime.AddMinutes(50), schoolZone);
                var lesson = Lesson.CreateFromSeries(series.Id, item.Student.Id, item.Teacher.Id, item.Instrument.Id, startAt, endAt, now);
                var isPast = date < today;
                if (isPast)
                {
                    var attendanceStatus = random.NextDouble() < .84
                        ? AttendanceStatus.Present
                        : random.NextDouble() < .65 ? AttendanceStatus.Excused : AttendanceStatus.Absent;
                    var attendanceNote = attendanceStatus == AttendanceStatus.Present
                        ? "Derse katıldı, çalışma ritmi iyi."
                        : attendanceStatus == AttendanceStatus.Excused ? "Veli önceden bilgi verdi." : "Veliye takip mesajı gönderildi.";
                    var markedAt = startAt.AddHours(1);
                    lesson.Complete(markedAt);
                    db.LessonAttendances.Add(LessonAttendance.Create(lesson.Id, attendanceStatus, item.Teacher.Id, attendanceNote, markedAt));
                    counts.AttendanceRecords++;

                    if (attendanceStatus != AttendanceStatus.Absent && random.NextDouble() < .93)
                    {
                        var week = Math.Max(0, lessonIndex / 4);
                        var piece = Pieces[item.Instrument.Code][Math.Min(Pieces[item.Instrument.Code].Length - 1, week / 6)];
                        var difficulty = Math.Clamp(1 + week / 7 + random.Next(-1, 2), 1, 5);
                        var noteAt = startAt.AddHours(2);
                        var lessonNote = LessonNote.Create(
                            lesson.Id,
                            item.Teacher.Id,
                            $"{piece}; {PracticeFocus(item.Instrument.Code, week)}",
                            ProgressNote(item.Student.FirstName, week, attendanceStatus, difficulty),
                            Homework(item.Instrument.Code, week),
                            NextGoal(item.Instrument.Code, week),
                            piece,
                            difficulty,
                            noteAt);

                        // Notların bir kısmı veliye açılmış olsun: aksi halde demo ortamında
                        // veli portalinin gelişim sekmesi tamamen boş görünüyordu ve öğretmen
                        // notu -> onay -> veli görünürlüğü zinciri hiç gösterilemiyordu.
                        // Kasıtlı olarak HEPSİ onaylanmaz - "taslak" ve "henüz hazırlanmadı"
                        // durumları da ekranda görünsün.
                        if (lessonIndex % 3 == 0)
                        {
                            lessonNote.SetParentCommentDraft(
                                ParentComment(item.Student.FirstName, piece, week),
                                noteAt);
                            lessonNote.ApproveParentComment(item.Teacher.Id, noteAt.AddMinutes(5));
                        }
                        else if (lessonIndex % 7 == 0)
                        {
                            // Yalnızca taslak - veliye görünmez.
                            lessonNote.SetParentCommentDraft(
                                ParentComment(item.Student.FirstName, piece, week),
                                noteAt);
                        }

                        db.LessonNotes.Add(lessonNote);
                        counts.ProgressNotes++;
                    }
                }
                else if (random.NextDouble() < .78)
                {
                    var rsvp = LessonRsvp.Create(lesson.Id, item.Guardian.Id, now);
                    rsvp.Respond(
                        random.NextDouble() < .82 ? RsvpResponse.Attending : RsvpResponse.AttendingLate,
                        RsvpSource.WhatsApp,
                        now);
                    db.LessonRsvps.Add(rsvp);
                }

                db.Lessons.Add(lesson);

                if (isPast && lessonIndex % 4 == 0)
                {
                    db.WhatsAppMessages.Add(WhatsAppMessage.CreateOutbound(
                        null,
                        item.Guardian.Id,
                        null,
                        $"Abdera Müzik Okulu: {item.Student.FirstName} için {item.Instrument.Name} gelişim takibi güncellendi. Öğretmeni {item.Teacher.FirstName}, son derste düzenli pratik ve bir sonraki hedefleri paylaştı.",
                        $"mock-followup-{item.Student.Id:N}-{lessonIndex}",
                        startAt.AddHours(3)));
                    counts.GuardianMessages++;
                }

                counts.Lessons++;
            }
        }

        await db.SaveChangesAsync();

        return Results.Ok(new SeedResponse(
            "seeded",
            from,
            today,
            TeacherSpecs.Length,
            GuardianSpecs.Length,
            counts.Students,
            counts.Enrollments,
            counts.Lessons,
            counts.AttendanceRecords,
            counts.ProgressNotes,
            counts.GuardianMessages,
            counts.Receivables,
            counts.Payments));
    }

    private static DateOnly NextOnOrAfter(DateOnly from, DayOfWeek dayOfWeek)
    {
        var difference = ((int)dayOfWeek - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(difference);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }

    private static string PracticeFocus(string instrumentCode, int week) => instrumentCode switch
    {
        "PIANO" => week % 3 == 0 ? "gam ve parmak bağımsızlığı" : week % 3 == 1 ? "ritmik doğruluk" : "ifade ve nüans",
        "GUITAR" => week % 3 == 0 ? "akor geçişleri" : week % 3 == 1 ? "sağ el ritmi" : "temiz entonasyon",
        "VIOLIN" => week % 3 == 0 ? "yay kontrolü" : week % 3 == 1 ? "entonasyon" : "cümleleme",
        _ => week % 3 == 0 ? "temel groove" : week % 3 == 1 ? "koordinasyon" : "dinamik kontrol",
    };

    private static string ProgressNote(string studentName, int week, AttendanceStatus attendance, int difficulty)
    {
        var direction = week < 8
            ? "temel koordinasyon ve doğru alışkanlıklar üzerinde çalışıyor"
            : week < 16
                ? "ritim ve teknik kontrolünü belirgin biçimde geliştirdi"
                : "müzikal ifade, dayanıklılık ve parça hakimiyetinde istikrarlı ilerliyor";
        var attendanceText = attendance == AttendanceStatus.Excused ? "Telafi planı konuşuldu." : "Ders içi katılımı verimliydi.";
        return $"{studentName} {direction}. Eser zorluğu {difficulty}/5 seviyesinde; küçük bölümlere ayırınca güveni artıyor. {attendanceText}";
    }

    // Veliye gosterilen yorum, ham ogretmen notundan AYRI bir metindir: daha kisa, yapici
    // ve teknik jargonsuz. Ikisinin ayni olmamasi Faz 10'un ana fikri - demo verisi de bu
    // ayrimi gorunur kilsin diye farkli bir metin uretir.
    private static string ParentComment(string studentName, string piece, int week)
    {
        var progress = week < 8
            ? "derse uyumu ve calisma duzeni gozle gorulur sekilde oturdu"
            : week < 16
                ? "ritim duygusu ve el bagimsizligi belirgin bicimde gelisti"
                : "eseri butun olarak calabilecek hakimiyete yaklasti";
        return $"{studentName} bu donemde {progress}. \"{piece}\" uzerinde duzenli calismasi ilerlemeyi hizlandiriyor; " +
               "evde kisa ama her gun tekrarlanan calisma en cok fayda saglayan yontem oluyor.";
    }

    private static string Homework(string instrumentCode, int week) => instrumentCode switch
    {
        "PIANO" => week % 2 == 0 ? "Gamı 60 bpm ile 5 tekrar ve eserin ilk 8 ölçüsü." : "Eseri metronomla yavaş tempoda, iki farklı nüansla çalış.",
        "GUITAR" => week % 2 == 0 ? "Akor geçişlerini 10 dakika ritim kesmeden tekrar et." : "Parçanın nakaratını üç farklı tempoda kaydet.",
        "VIOLIN" => week % 2 == 0 ? "Açık tel ve gam egzersizlerini yay dağılımına dikkat ederek çalış." : "Cümle sonlarını dinleyerek uzun yay egzersizi yap.",
        _ => week % 2 == 0 ? "Metronomla temel groove'u 10 dakika kesintisiz sürdür." : "Aksanları değiştirerek ritim kalıbını dört tur tekrar et.",
    };

    private static string NextGoal(string instrumentCode, int week) => instrumentCode switch
    {
        "PIANO" => week < 12 ? "İki el koordinasyonunu 70 bpm'e taşımak." : "Parçayı baştan sona durmadan çalmak.",
        "GUITAR" => week < 12 ? "Akor geçişlerinde duraksamayı azaltmak." : "Parçaya dinamik ve temiz bir giriş eklemek.",
        "VIOLIN" => week < 12 ? "Entonasyonu sabitlemek ve yayı eşit dağıtmak." : "Cümleleri nefesli ve tutarlı bir ifadeyle birleştirmek.",
        _ => week < 12 ? "Metronom temposunu koruyarak koordinasyonu güçlendirmek." : "Groove'u dinamik geçişlerle tamamlamak.",
    };

    private sealed class SeedCounts
    {
        public int Students { get; set; }
        public int Enrollments { get; set; }
        public int Lessons { get; set; }
        public int AttendanceRecords { get; set; }
        public int ProgressNotes { get; set; }
        public int GuardianMessages { get; set; }
        public int Receivables { get; set; }
        public int Payments { get; set; }
    }
}
