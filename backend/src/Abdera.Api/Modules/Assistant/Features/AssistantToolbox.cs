using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Abdera.Api.Modules.Assistant.Domain;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Assistant.Features;

// Dil modelinin kullanabildiği araçlar (docs/10-decisions.md AE9). Hepsi SALT OKUNUR: model hiçbir
// kaydı değiştiremez. Değişiklik isteyen işlerde show_card ekranda bir kart açar; dersi taşımak,
// telafi açmak, hatırlatma göndermek ve ödeme almak o kartta kullanıcının onayıyla, mevcut
// ekranların uçlarından yapılır (AE2).
//
// Kişiler araç girdisinde ve çıktısında kodla geçer (Pseudonymizer); öğretmen notları gibi serbest
// metinler de maskelenir. Öğretmen yalnızca kendi öğrencilerini ve kendi notlarını görür.
public sealed class AssistantToolbox(
    AbderaDbContext db,
    IClock clock,
    Pseudonymizer names,
    Guid? teacherScope,
    IReadOnlyList<StudentNameMatcher.Candidate> visibleStudents)
{
    public record Card(string Kind, Guid? StudentId, AssistantChat.LessonMove? Move, NavigationTargets.Target? Navigation);

    public const int MaxCards = 3;
    // Türkçe harfler \u kaçışına çevrilmesin: model için okunaklı, daha az token.
    public static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly CultureInfo Turkish = new("tr-TR");
    private static readonly string[] CardKinds = ["progress", "lessons", "makeup", "reschedule", "dues", "navigate"];

    private bool IsAdmin => teacherScope is null;
    public List<Card> Cards { get; } = [];
    public List<string> CalledTools { get; } = [];

    public static JsonArray Definitions() =>
    [
        Tool("find_students", "Okulun öğrencileri arasında ad, soyad ya da öğrenci kodu ile arar. Kursunu, öğretmenini ve haftalık ders saatini döndürür. Metinde kod yoksa ya da ad belirsizse önce bunu kullan.",
            Properties(("query", "string", "Ad, soyad, ad soyad ya da Ö-XXXX kodu")), "query"),
        Tool("student_progress", "Öğrencinin son 3 aydaki devamı, son ders notları, çalıştığı eser, beceri puanları ve telafi hakkı sayısı.",
            Properties(("student", "string", "Öğrenci kodu (Ö-XXXX)")), "student"),
        Tool("list_lessons", "Öğrencinin önümüzdeki derslerini, haftalık programını ve aradaki resmi tatilleri listeler.",
            Properties(("student", "string", "Öğrenci kodu (Ö-XXXX)"), ("days", "integer", "Kaç gün ileriye bakılsın (varsayılan 28, en çok 42)")), "student"),
        Tool("list_makeup_credits", "Öğrencinin kullanılabilir telafi haklarını ve son kullanma tarihlerini listeler.",
            Properties(("student", "string", "Öğrenci kodu (Ö-XXXX)")), "student"),
        Tool("list_dues", "Ödenmemiş aidatlar: vadesi geçmiş ve 7 gün içinde gelenler. Öğrenci verilirse o öğrencinin tüm açık aidatları. Yalnız yönetici.",
            Properties(("student", "string", "İsteğe bağlı öğrenci kodu (Ö-XXXX)"))),
        Tool("show_card", "Ekranda kart açar. Bilgi kartları (progress, lessons, dues) ayrıntı gösterir; reschedule ve makeup kullanıcının onaylayacağı işlem kartlarıdır - işlemi kart yapar, sen yapmazsın; navigate ilgili ekrana bağlantıdır.",
            new JsonObject
            {
                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(CardKinds.Select(kind => (JsonNode)kind).ToArray()) },
                ["student"] = Prop("string", "Öğrenci kodu (Ö-XXXX). dues için isteğe bağlı, navigate için yok."),
                ["screen"] = Prop("string", "navigate için ekran anahtarı"),
                ["source_date"] = Prop("string", "reschedule: taşınacak dersin günü, YYYY-MM-DD. Kullanıcı dersin gününü ya da tarihini söylediyse (\"perşembe dersini\", \"4 Kasım dersinden\") ZORUNLU."),
                ["source_time"] = Prop("string", "reschedule: taşınacak dersin saati, HH:mm"),
                ["target_date"] = Prop("string", "reschedule ve makeup: yeni gün / telafi günü, YYYY-MM-DD (açık tarih söylendiyse)"),
                ["target_weekday"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Enum.GetNames<DayOfWeek>().Select(day => (JsonNode)day).ToArray()), ["description"] = "reschedule ve makeup: yalnız gün adı söylendiyse (\"cumaya al\", \"cuma telafi\")" },
                ["target_next_week"] = Prop("boolean", "reschedule ve makeup: \"haftaya cuma\" gibi"),
                ["target_time"] = Prop("string", "reschedule ve makeup: yeni saat / telafi saati, HH:mm"),
                ["shift_minutes"] = Prop("integer", "reschedule: dersin kendi saatine göre kaydırma; \"yarım saat ileri\" 30, \"bir saat erkene\" -60"),
                ["scope"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Once", "Recurring"), ["description"] = "reschedule: yalnız bu ders mi (Once), bundan sonraki her hafta mı (Recurring); belirsizse verme" },
            }, "kind"),
    ];

    public async Task<string> ExecuteAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        CalledTools.Add(name);
        JsonObject args;
        try
        {
            args = JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return Error("Araç girdisi geçerli JSON değil.").ToJsonString(Json);
        }

        try
        {
            var result = name switch
            {
                "find_students" => await FindStudentsAsync(Text(args, "query") ?? "", cancellationToken),
                "student_progress" => await WithStudent(args, id => StudentProgressAsync(id, cancellationToken)),
                "list_lessons" => await WithStudent(args, id => ListLessonsAsync(id, Math.Clamp(Int(args, "days") ?? 28, 1, 42), cancellationToken)),
                "list_makeup_credits" => await WithStudent(args, id => ListMakeupCreditsAsync(id, cancellationToken)),
                "list_dues" => await ListDuesAsync(Text(args, "student"), cancellationToken),
                "show_card" => ShowCard(args),
                _ => Error($"Bilinmeyen araç: {name}"),
            };
            return result.ToJsonString(Json);
        }
        catch (ToolException exception)
        {
            return Error(exception.Message).ToJsonString(Json);
        }
    }

    private async Task<JsonObject> FindStudentsAsync(string query, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> ids;
        if (names.TryResolve(query, out var codedId)) ids = [codedId];
        else
        {
            var match = StudentNameMatcher.Match(AssistantText.Tokens(AssistantText.Normalize(query)), visibleStudents);
            ids = match.StudentIds;
        }

        var visible = ids.Where(id => visibleStudents.Any(student => student.StudentId == id)).Take(8).ToList();
        var students = new JsonArray();
        foreach (var id in visible) students.Add(await DescribeStudentAsync(id, cancellationToken));
        return new JsonObject
        {
            ["matches"] = students,
            ["note"] = visible.Count switch
            {
                0 => "Eşleşen öğrenci yok. Kullanıcıdan ad ve soyadı iste.",
                1 => "Tek eşleşme.",
                _ => "Birden çok öğrenci eşleşti; kullanıcıya hangisi olduğunu sor.",
            },
        };
    }

    private async Task<JsonObject> DescribeStudentAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var status = await db.Students.Where(s => s.Id == studentId).Select(s => s.Status).SingleAsync(cancellationToken);
        var enrollments = db.Enrollments.Where(e => e.StudentId == studentId && e.Status == EnrollmentStatus.Active);
        if (teacherScope is { } scoped) enrollments = enrollments.Where(e => e.TeacherId == scoped);

        var courses = await enrollments
            .Join(db.Instruments, e => e.InstrumentId, i => i.Id, (e, i) => new { e.Id, e.TeacherId, e.CourseKind, Instrument = i.Name })
            .ToListAsync(cancellationToken);
        var enrollmentIds = courses.Select(c => c.Id).ToList();
        var today = Today();
        var series = await db.LessonSeries
            .Where(s => enrollmentIds.Contains(s.EnrollmentId) && s.Status == LessonSeriesStatus.Active && (s.EffectiveUntil == null || s.EffectiveUntil >= today))
            .ToListAsync(cancellationToken);

        return new JsonObject
        {
            ["student"] = names.CodeFor(studentId),
            ["status"] = status == StudentStatus.Active ? "aktif" : "pasif",
            ["courses"] = new JsonArray(courses.Select(course => (JsonNode)new JsonObject
            {
                ["instrument"] = course.Instrument,
                ["kind"] = course.CourseKind == CourseKind.Individual ? "birebir" : "grup",
                ["teacher"] = names.CodeFor(course.TeacherId),
                ["weekly"] = new JsonArray(series.Where(s => s.EnrollmentId == course.Id)
                    .Select(s => (JsonNode)$"{DayName(s.DayOfWeek)} {s.StartTime:HH\\:mm} ({s.DurationMinutes} dk){(s.EffectiveUntil is { } until ? $", {until:yyyy-MM-dd} tarihine kadar" : "")}")
                    .ToArray()),
            }).ToArray()),
        };
    }

    private async Task<JsonObject> StudentProgressAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var lessons = db.Lessons.Where(l => l.StudentId == studentId && l.StartAt >= now.AddDays(-90) && l.StartAt <= now && l.Status != LessonStatus.Cancelled && l.Status != LessonStatus.Rescheduled);
        if (teacherScope is { } scoped) lessons = lessons.Where(l => l.TeacherId == scoped);
        var lessonIds = await lessons.Select(l => l.Id).ToListAsync(cancellationToken);
        var attendance = await db.LessonAttendances.Where(a => lessonIds.Contains(a.LessonId)).Select(a => a.Status).ToListAsync(cancellationToken);
        int Count(AttendanceStatus status) => attendance.Count(item => item == status);
        var marked = attendance.Count;

        var notes = db.LessonNotes.Join(db.Lessons, n => n.LessonId, l => l.Id, (n, l) => new { Note = n, l.StudentId, l.StartAt })
            .Where(x => x.StudentId == studentId);
        if (teacherScope is { } noteScope) notes = notes.Where(x => x.Note.TeacherId == noteScope);
        var lastNotes = await notes.OrderByDescending(x => x.StartAt).Take(3).ToListAsync(cancellationToken);

        var skills = db.SkillAssessments.Where(s => s.StudentId == studentId);
        if (teacherScope is { } skillScope) skills = skills.Where(s => s.TeacherId == skillScope);
        var assessments = await skills
            .Join(db.SkillDefinitions, s => s.SkillDefinitionId, d => d.Id, (s, d) => new { d.Label, s.Score, s.AssessedAt })
            .ToListAsync(cancellationToken);

        var credits = await db.MakeupCredits.CountAsync(c => c.StudentId == studentId && c.Status == MakeupCreditStatus.Available && c.ExpiresAt > now, cancellationToken);

        return new JsonObject
        {
            ["student"] = names.CodeFor(studentId),
            ["attendance_last_3_months"] = new JsonObject
            {
                ["present"] = Count(AttendanceStatus.Present),
                ["excused"] = Count(AttendanceStatus.Excused),
                ["absent"] = Count(AttendanceStatus.Absent),
                ["unmarked"] = lessonIds.Count - marked,
                ["present_rate_percent"] = marked == 0 ? null : (int)Math.Round(100.0 * Count(AttendanceStatus.Present) / marked),
            },
            ["current_piece"] = lastNotes.Select(x => x.Note.PieceTitle).FirstOrDefault(title => !string.IsNullOrWhiteSpace(title)),
            ["last_notes"] = new JsonArray(lastNotes.Select(x => (JsonNode)new JsonObject
            {
                ["date"] = Local(x.StartAt).ToString("yyyy-MM-dd"),
                ["teacher"] = names.CodeFor(x.Note.TeacherId),
                ["practiced"] = names.Mask(x.Note.Practiced),
                ["note"] = names.Mask(x.Note.Note),
                ["homework"] = names.Mask(x.Note.Homework),
                ["next_goal"] = names.Mask(x.Note.NextGoal),
            }).ToArray()),
            ["skills_1_to_5"] = new JsonArray(assessments.GroupBy(a => a.Label).Select(group =>
            {
                var ordered = group.OrderByDescending(a => a.AssessedAt).ToList();
                return (JsonNode)new JsonObject { ["skill"] = group.Key, ["score"] = ordered[0].Score, ["previous"] = ordered.Count > 1 ? ordered[1].Score : null };
            }).ToArray()),
            ["available_makeup_credits"] = credits,
            ["scope"] = IsAdmin ? "tüm öğretmenler" : "yalnız bu öğretmenin dersleri ve notları",
        };
    }

    private async Task<JsonObject> ListLessonsAsync(Guid studentId, int days, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var lessons = db.Lessons.Where(l => l.StudentId == studentId && l.EndAt > now && l.StartAt < now.AddDays(days) && (l.Status == LessonStatus.Normal || l.Status == LessonStatus.Makeup));
        if (teacherScope is { } scoped) lessons = lessons.Where(l => l.TeacherId == scoped);
        var upcoming = await lessons.OrderBy(l => l.StartAt)
            .Join(db.Instruments, l => l.InstrumentId, i => i.Id, (l, i) => new { l.StartAt, l.EndAt, l.Status, l.TeacherId, Instrument = i.Name })
            .ToListAsync(cancellationToken);

        var today = Today();
        var holidays = await db.SchoolCalendarDays
            .Where(d => d.Type == SchoolCalendarDayType.Holiday && d.Date >= today && d.Date <= today.AddDays(days))
            .OrderBy(d => d.Date)
            .Select(d => new { d.Date, d.Label })
            .ToListAsync(cancellationToken);

        var description = await DescribeStudentAsync(studentId, cancellationToken);
        return new JsonObject
        {
            ["student"] = names.CodeFor(studentId),
            ["courses"] = description["courses"]?.DeepClone(),
            ["upcoming"] = new JsonArray(upcoming.Select(l => (JsonNode)new JsonObject
            {
                ["date"] = Local(l.StartAt).ToString("yyyy-MM-dd"),
                ["weekday"] = DayName(Local(l.StartAt).DayOfWeek),
                ["time"] = $"{Local(l.StartAt):HH\\:mm}-{Local(l.EndAt):HH\\:mm}",
                ["instrument"] = l.Instrument,
                ["teacher"] = names.CodeFor(l.TeacherId),
                ["makeup"] = l.Status == LessonStatus.Makeup,
                ["on_holiday"] = holidays.Any(h => h.Date == DateOnly.FromDateTime(Local(l.StartAt).Date)),
            }).ToArray()),
            ["holidays"] = new JsonArray(holidays.Select(h => (JsonNode)new JsonObject { ["date"] = h.Date.ToString("yyyy-MM-dd"), ["label"] = h.Label }).ToArray()),
        };
    }

    private async Task<JsonObject> ListMakeupCreditsAsync(Guid studentId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var credits = await db.MakeupCredits
            .Where(c => c.StudentId == studentId && c.Status == MakeupCreditStatus.Available && c.ExpiresAt > now)
            .Join(db.Lessons, c => c.SourceLessonId, l => l.Id, (c, l) => new { l.StartAt, c.ExpiresAt })
            .OrderBy(x => x.ExpiresAt)
            .ToListAsync(cancellationToken);

        return new JsonObject
        {
            ["student"] = names.CodeFor(studentId),
            ["available"] = new JsonArray(credits.Select(c => (JsonNode)new JsonObject
            {
                ["from_lesson"] = Local(c.StartAt).ToString("yyyy-MM-dd"),
                // Son kullanma anı gün sonu; son geçerli gün bir önceki gündür.
                ["last_valid_day"] = Local(c.ExpiresAt.AddTicks(-1)).ToString("yyyy-MM-dd"),
            }).ToArray()),
        };
    }

    private async Task<JsonObject> ListDuesAsync(string? studentArgument, CancellationToken cancellationToken)
    {
        if (!IsAdmin) throw new ToolException("Aidat bilgileri yalnızca yöneticiye açık.");
        Guid? studentId = string.IsNullOrWhiteSpace(studentArgument) ? null : ResolveStudent(studentArgument);

        var today = Today();
        var receivables = await db.Receivables
            .Where(r => r.Status != ReceivableStatus.Paid && r.Status != ReceivableStatus.Cancelled)
            .Join(db.Enrollments, r => r.EnrollmentId, e => e.Id, (r, e) => new { Receivable = r, e.StudentId })
            .Where(x => studentId == null ? x.Receivable.DueDate <= today.AddDays(7) : x.StudentId == studentId)
            .ToListAsync(cancellationToken);
        var paid = await Receivables.ComputeTotalsPaidAsync(receivables.Select(x => x.Receivable.Id), db);

        var lines = receivables
            .Select(x => new { x.StudentId, x.Receivable, Remaining = x.Receivable.Amount - paid.GetValueOrDefault(x.Receivable.Id) })
            .Where(x => x.Remaining > 0 && visibleStudents.Any(s => s.StudentId == x.StudentId))
            .OrderBy(x => x.Receivable.DueDate)
            .ToList();

        return new JsonObject
        {
            ["today"] = today.ToString("yyyy-MM-dd"),
            ["dues"] = new JsonArray(lines.Select(x => (JsonNode)new JsonObject
            {
                ["student"] = names.CodeFor(x.StudentId),
                ["period"] = x.Receivable.Period,
                ["remaining"] = x.Remaining,
                ["currency"] = x.Receivable.Currency,
                ["due_date"] = x.Receivable.DueDate.ToString("yyyy-MM-dd"),
                ["state"] = x.Receivable.DueDate < today ? "vadesi geçti" : x.Receivable.DueDate == today ? "bugün" : "yaklaşıyor",
                ["partially_paid"] = x.Receivable.Status == ReceivableStatus.Partial,
            }).ToArray()),
            ["total_remaining"] = lines.Sum(x => x.Remaining),
        };
    }

    private JsonObject ShowCard(JsonObject args)
    {
        if (Cards.Count >= MaxCards) throw new ToolException($"Bir yanıtta en fazla {MaxCards} kart açılabilir.");
        var kind = Text(args, "kind") ?? "";
        if (!CardKinds.Contains(kind)) throw new ToolException($"Geçersiz kart türü: {kind}");

        if (kind == "navigate")
        {
            var key = Text(args, "screen") ?? "";
            var target = NavigationTargets.All.FirstOrDefault(t => t.Key == key) ?? throw new ToolException($"Bilinmeyen ekran: {key}");
            if (target.AdminOnly && !IsAdmin) throw new ToolException($"{target.Label} ekranı yalnızca yöneticiye açık.");
            Cards.Add(new Card(kind, null, null, target));
            return Ok($"{target.Label} ekranına giden bağlantı gösterildi.");
        }

        if (kind == "dues")
        {
            if (!IsAdmin) throw new ToolException("Aidat bilgileri yalnızca yöneticiye açık.");
            var dueStudent = Text(args, "student") is { } code ? ResolveStudent(code) : (Guid?)null;
            Cards.Add(new Card(kind, dueStudent, null, null));
            return Ok("Aidat kartı gösterildi; hatırlatma ve ödeme alma kullanıcının onayıyla karttan yapılır.");
        }

        var studentId = ResolveStudent(Text(args, "student") ?? throw new ToolException("Bu kart için öğrenci kodu gerekli."));
        // Telafi kartı da istenen gün/saati alır; kart o saati denetler, uygun değilse nedenini söyler.
        var move = kind is "reschedule" or "makeup" ? ReadMove(args) : null;
        Cards.Add(new Card(kind, studentId, move, null));
        return Ok(kind switch
        {
            "reschedule" => "Ders taşıma kartı açıldı. Kart dersi bulur, müsaitliği ve çakışmayı denetler, kullanıcı onaylarsa taşır. Henüz hiçbir şey değişmedi.",
            "makeup" => "Telafi kartı açıldı. Kart istenen saati denetler ya da uygun saatleri önerir, kullanıcı onaylarsa telafi dersini açar. Henüz hiçbir şey değişmedi.",
            _ => "Kart gösterildi.",
        });
    }

    private AssistantChat.LessonMove ReadMove(JsonObject args)
    {
        DateOnly? Date(string key) => Text(args, key) is { } value
            ? DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date) ? date : throw new ToolException($"{key} YYYY-MM-DD biçiminde olmalı.")
            : null;
        TimeOnly? Time(string key) => Text(args, key) is { } value
            ? TimeOnly.TryParseExact(value, ["HH:mm", "H:mm", "HH:mm:ss"], out var time) ? time : throw new ToolException($"{key} HH:mm biçiminde olmalı.")
            : null;

        DayOfWeek? weekday = Text(args, "target_weekday") is { } day
            ? Enum.TryParse<DayOfWeek>(day, true, out var parsed) ? parsed : throw new ToolException("target_weekday Monday..Sunday olmalı.")
            : null;
        var nextWeek = args["target_next_week"]?.GetValueKind() == JsonValueKind.True;
        var targetDate = Date("target_date");
        // Yalnız gün adı verildiyse istemcinin geri uyumu için bugünden sonraki ilk o gün de döner;
        // tek seferlik taşımada istemci asıl tarihi dersin haftasından hesaplar (AE8).
        if (targetDate is null && weekday is { } targetDay)
        {
            var today = Today();
            targetDate = today.AddDays(((int)targetDay - (int)today.DayOfWeek + 7) % 7 + (nextWeek ? 7 : 0));
        }

        var scope = Text(args, "scope") switch
        {
            "Once" => RescheduleScope.Once,
            "Recurring" => RescheduleScope.Recurring,
            _ => RescheduleScope.Unspecified,
        };
        var shift = Int(args, "shift_minutes");
        if (shift is { } minutes && (minutes == 0 || Math.Abs(minutes) > 600)) shift = null;

        return new AssistantChat.LessonMove(Date("source_date"), Time("source_time"), targetDate, Time("target_time"), scope, shift, weekday, nextWeek);
    }

    private async Task<JsonObject> WithStudent(JsonObject args, Func<Guid, Task<JsonObject>> run) =>
        await run(ResolveStudent(Text(args, "student") ?? throw new ToolException("Öğrenci kodu gerekli.")));

    // Kod beklenir; model ad yazdıysa tek eşleşmede kabul edilir, aksi halde find_students'a yönlendirilir.
    private Guid ResolveStudent(string value)
    {
        if (names.TryResolve(value, out var id))
        {
            if (visibleStudents.Any(student => student.StudentId == id)) return id;
            throw new ToolException("Bu öğrenciyi göremiyorsun ya da bu kod bir öğrenciye ait değil.");
        }

        var match = StudentNameMatcher.Match(AssistantText.Tokens(AssistantText.Normalize(value)), visibleStudents);
        return match.StudentIds.Count == 1
            ? match.StudentIds[0]
            : throw new ToolException(match.StudentIds.Count == 0 ? "Öğrenci bulunamadı; find_students ile ara." : "Birden çok öğrenci eşleşti; find_students ile ara ve kullanıcıya sor.");
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
    private DateTimeOffset Local(DateTimeOffset instant) => clock.ToSchoolLocal(instant);
    private static string DayName(DayOfWeek day) => Turkish.DateTimeFormat.GetDayName(day);

    private static string? Text(JsonObject args, string key) =>
        args[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    // Model sayıyı bazen metin ya da ondalıklı yazar ("30", 30.0).
    private static int? Int(JsonObject args, string key)
    {
        if (args[key] is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var number)) return number;
        if (value.TryGetValue<double>(out var real)) return (int)Math.Round(real);
        return value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed) ? parsed : null;
    }

    private static JsonObject Ok(string message) => new() { ["ok"] = true, ["message"] = message };
    private static JsonObject Error(string message) => new() { ["ok"] = false, ["error"] = message };

    private static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(required.Select(item => (JsonNode)item).ToArray()),
            },
        },
    };

    private static JsonObject Properties(params (string Name, string Type, string Description)[] items)
    {
        var properties = new JsonObject();
        foreach (var (name, type, description) in items) properties[name] = Prop(type, description);
        return properties;
    }

    private static JsonObject Prop(string type, string description) => new() { ["type"] = type, ["description"] = description };

    private sealed class ToolException(string message) : Exception(message);
}
