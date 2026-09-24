using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Infrastructure;

// Öğrenci/öğretmen KALICI silme. Bu dosya bilinçli olarak ham SQL kullanıyor.
//
// Neden: bir kişiye bağlı satırlar 15 farklı tabloya yayılmış ve bunların çoğunda
// veritabanı seviyesinde FK YOK - modüller arası bağlar açık id sorgularıyla kuruluyor
// (CLAUDE.md modül sınırı kuralı). Dolayısıyla ne EF cascade'i ne de DB cascade'i bu işi
// tek başına yapabilir; silinecek satırlar tek tek ve DOĞRU SIRADA kaldırılmak zorunda.
// Tek bir transaction içinde çalışır: yarım kalan bir silme, hiçbir öğrenciye bağlı
// olmayan derslerden oluşan bir enkaz bırakırdı.
//
// ÖNEMLİ: `audit_log` hiçbir zaman temizlenmez. Silme işleminin kendisi de audit'e yazılır -
// "bu öğrenci ne zaman, kim tarafından silindi" sorusunun cevabı kalmalı.
//
// Bu listenin eksiksizliği `PersonDeletionFlowTests` içindeki yetim-satır testiyle korunur:
// yeni bir tablo öğrenciye/öğretmene referans verdiğinde o test kırılır.
public static class PersonEraser
{
    public record StudentImpact(
        Guid StudentId, string StudentName,
        int Enrollments, int Lessons, int Attendances, int Receivables,
        int Payments, decimal CollectedAmount, string Currency,
        int MakeupCredits, int Assessments, int ShowItems,
        int GuardiansLeftWithoutStudents);

    public record TeacherImpact(
        Guid TeacherId, string TeacherName,
        int Enrollments, int AffectedStudents, int Lessons, int Receivables,
        int Payments, decimal CollectedAmount, string Currency,
        int Availabilities, int TimeOffs, int Assessments, int LessonNotes, int ShowItems,
        bool HasUserAccount);

    public static async Task<StudentImpact> DescribeStudentAsync(Guid studentId, AbderaDbContext db)
    {
        var student = await db.Students.AsNoTracking().SingleOrDefaultAsync(item => item.Id == studentId)
            ?? throw new NotFoundException("Öğrenci bulunamadı.");

        var enrollmentIds = await db.Enrollments.Where(e => e.StudentId == studentId).Select(e => e.Id).ToListAsync();
        var seriesIds = await db.LessonSeries.Where(s => enrollmentIds.Contains(s.EnrollmentId)).Select(s => s.Id).ToListAsync();
        var lessonIds = await db.Lessons
            .Where(l => l.StudentId == studentId || (l.LessonSeriesId != null && seriesIds.Contains(l.LessonSeriesId.Value)))
            .Select(l => l.Id).ToListAsync();
        var receivables = await db.Receivables.Where(r => enrollmentIds.Contains(r.EnrollmentId))
            .Select(r => new { r.Id, r.Currency }).ToListAsync();
        var receivableIds = receivables.Select(r => r.Id).ToList();
        var payments = await db.Payments.Where(p => receivableIds.Contains(p.ReceivableId)).ToListAsync();

        // Silindikten sonra hiçbir öğrencisi kalmayacak veliler. Otomatik silinmezler -
        // veli kaydı başka bir çocuk için tekrar kullanılabilir, ve kişisel veriyi
        // kullanıcının haberi olmadan silmek yerine sayısını bildirip kararı ona bırakıyoruz.
        var guardianIds = await db.StudentGuardians.Where(link => link.StudentId == studentId)
            .Select(link => link.GuardianId).ToListAsync();
        var orphanGuardians = 0;
        foreach (var guardianId in guardianIds)
        {
            var others = await db.StudentGuardians
                .CountAsync(link => link.GuardianId == guardianId && link.StudentId != studentId);
            if (others == 0) orphanGuardians++;
        }

        return new StudentImpact(
            studentId, $"{student.FirstName} {student.LastName}",
            enrollmentIds.Count,
            lessonIds.Count,
            await db.LessonAttendances.CountAsync(a => lessonIds.Contains(a.LessonId)),
            receivableIds.Count,
            payments.Count,
            payments.Sum(p => p.Amount),
            receivables.FirstOrDefault()?.Currency ?? "TRY",
            await db.MakeupCredits.CountAsync(c => c.StudentId == studentId),
            await db.SkillAssessments.CountAsync(a => a.StudentId == studentId),
            await db.ShowItems.CountAsync(i => i.StudentId == studentId),
            orphanGuardians);
    }

    public static async Task<TeacherImpact> DescribeTeacherAsync(Guid teacherId, AbderaDbContext db)
    {
        var teacher = await db.Teachers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == teacherId)
            ?? throw new NotFoundException("Öğretmen bulunamadı.");

        var enrollments = await db.Enrollments.Where(e => e.TeacherId == teacherId)
            .Select(e => new { e.Id, e.StudentId }).ToListAsync();
        var enrollmentIds = enrollments.Select(e => e.Id).ToList();
        var lessonIds = await db.Lessons.Where(l => l.TeacherId == teacherId).Select(l => l.Id).ToListAsync();
        var receivables = await db.Receivables.Where(r => enrollmentIds.Contains(r.EnrollmentId))
            .Select(r => new { r.Id, r.Currency }).ToListAsync();
        var receivableIds = receivables.Select(r => r.Id).ToList();
        var payments = await db.Payments.Where(p => receivableIds.Contains(p.ReceivableId)).ToListAsync();

        return new TeacherImpact(
            teacherId, $"{teacher.FirstName} {teacher.LastName}",
            enrollments.Count,
            enrollments.Select(e => e.StudentId).Distinct().Count(),
            lessonIds.Count,
            receivableIds.Count,
            payments.Count,
            payments.Sum(p => p.Amount),
            receivables.FirstOrDefault()?.Currency ?? "TRY",
            await db.TeacherAvailabilities.CountAsync(a => a.TeacherId == teacherId),
            await db.TeacherTimeOffs.CountAsync(t => t.TeacherId == teacherId),
            await db.SkillAssessments.CountAsync(a => a.TeacherId == teacherId),
            await db.LessonNotes.CountAsync(n => n.TeacherId == teacherId),
            await db.ShowItems.CountAsync(i => i.TeacherId == teacherId),
            teacher.UserId is not null);
    }

    // Öğrencinin tüm izini siler. Çağıranın ödeme uyarısını zaten göstermiş olması beklenir.
    public static async Task EraseStudentAsync(Guid studentId, AbderaDbContext db) =>
        await RunAsync(db, StudentSql, ("student_id", studentId));

    // Öğretmeni siler. `reassignToTeacherId` verilirse önce öğrenciler, dersler, notlar ve
    // değerlendirmeler yeni öğretmene devredilir - bu, gerçek hayattaki tipik durum
    // ("öğretmen ayrıldı, öğrencileri başka öğretmene geçti") ve öğrencilerin mali/ders
    // geçmişine hiç dokunmadan öğretmeni kaldırır.
    public static async Task EraseTeacherAsync(Guid teacherId, Guid? reassignToTeacherId, AbderaDbContext db)
    {
        if (reassignToTeacherId is { } newTeacherId)
        {
            await RunAsync(db, ReassignSql, ("teacher_id", teacherId), ("new_teacher_id", newTeacherId));
        }

        await RunAsync(db, TeacherSql, ("teacher_id", teacherId));
    }

    // Betikler tek tek çalıştırılır, hepsi birden değil: Npgsql çok ifadeli bir komutu
    // parametrelerle gönderirken ifadeleri ayrı ayrı hazırlar ve bir ifadenin kullanmadığı
    // parametre sağlayıcıya göre hata üretebilir. Aynı transaction ve aynı bağlantı
    // üzerinde kaldığımız için ON COMMIT DROP geçici tabloları ifadeler arasında yaşar.
    //
    // Ayırmadan ÖNCE satır yorumları temizlenir. Sebebi gerçek bir hata: bir yorumun içindeki
    // noktalı virgül ("... da gider; kalırsa ...") ifade ayırıcı sanılıp yorumu ikiye bölüyor
    // ve kalan parça geçersiz SQL olarak çalıştırılmaya çalışılıyordu. Yorum metnini
    // düzeltmek yerine ayırıcıyı yoruma karşı dayanıklı yapmak doğrusu - aksi halde her yeni
    // yorumda aynı tuzak kurulur.
    private static async Task RunAsync(AbderaDbContext db, string script, params (string Name, object Value)[] parameters)
    {
        var withoutComments = string.Join('\n', script
            .Split('\n')
            .Select(line =>
            {
                var comment = line.IndexOf("--", StringComparison.Ordinal);
                return comment < 0 ? line : line[..comment];
            }));

        foreach (var statement in withoutComments.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = statement.Trim();
            if (trimmed.Length == 0) continue;

            var used = parameters
                .Where(parameter => trimmed.Contains($"@{parameter.Name}", StringComparison.Ordinal))
                .Select(parameter => (object)new Npgsql.NpgsqlParameter(parameter.Name, parameter.Value))
                .ToArray();

            await db.Database.ExecuteSqlRawAsync(trimmed, used);
        }
    }

    // Silme sırası bağımlılık zincirini izler: en uçtaki satırlardan köke doğru.
    private const string StudentSql = """
        CREATE TEMP TABLE _enr ON COMMIT DROP AS
            SELECT id FROM enrollments WHERE student_id = @student_id;
        CREATE TEMP TABLE _ser ON COMMIT DROP AS
            SELECT id FROM lesson_series WHERE enrollment_id IN (SELECT id FROM _enr);
        CREATE TEMP TABLE _les ON COMMIT DROP AS
            SELECT id FROM lessons
            WHERE student_id = @student_id OR lesson_series_id IN (SELECT id FROM _ser);
        CREATE TEMP TABLE _rec ON COMMIT DROP AS
            SELECT id FROM receivables WHERE enrollment_id IN (SELECT id FROM _enr);
        CREATE TEMP TABLE _pay ON COMMIT DROP AS
            SELECT id FROM payments WHERE receivable_id IN (SELECT id FROM _rec);

        DELETE FROM payment_corrections WHERE payment_id IN (SELECT id FROM _pay);
        DELETE FROM payments WHERE id IN (SELECT id FROM _pay);

        -- Banka işlemi harici bir kayıt: silinmez, yalnızca artık var olmayan aidata olan
        -- bağı kopar ve tekrar incelenmeyi bekler.
        UPDATE bank_incoming_transactions
           SET matched_receivable_id = NULL, status = 'NeedsReview'
         WHERE matched_receivable_id IN (SELECT id FROM _rec);

        DELETE FROM notification_jobs
         WHERE reference_id IN (SELECT id FROM _les)
            OR reference_id IN (SELECT id FROM _rec)
            OR reference_id = @student_id;
        DELETE FROM staff_notifications
         WHERE reference_id IN (SELECT id FROM _les)
            OR reference_id IN (SELECT id FROM _rec)
            OR reference_id = @student_id;

        DELETE FROM receivables WHERE id IN (SELECT id FROM _rec);
        DELETE FROM makeup_credits WHERE student_id = @student_id;

        DELETE FROM practice_assignments WHERE lesson_id IN (SELECT id FROM _les);
        DELETE FROM skill_assessments
         WHERE student_id = @student_id OR lesson_id IN (SELECT id FROM _les);
        DELETE FROM lesson_notes WHERE lesson_id IN (SELECT id FROM _les);
        DELETE FROM lesson_rsvps WHERE lesson_id IN (SELECT id FROM _les);
        DELETE FROM lesson_attendances WHERE lesson_id IN (SELECT id FROM _les);
        DELETE FROM lesson_change_requests WHERE lesson_id IN (SELECT id FROM _les);
        DELETE FROM practice_journal_entries WHERE student_id = @student_id;
        DELETE FROM progress_summaries WHERE student_id = @student_id;
        DELETE FROM show_items WHERE student_id = @student_id;

        DELETE FROM lessons WHERE id IN (SELECT id FROM _les);
        DELETE FROM lesson_series WHERE id IN (SELECT id FROM _ser);
        DELETE FROM enrollments WHERE id IN (SELECT id FROM _enr);

        DELETE FROM library_suggestions WHERE student_id = @student_id;
        DELETE FROM student_photos WHERE student_id = @student_id;
        DELETE FROM student_guardians WHERE student_id = @student_id;
        DELETE FROM students WHERE id = @student_id;
        """;

    private const string ReassignSql = """
        UPDATE enrollments SET teacher_id = @new_teacher_id, updated_at = now() WHERE teacher_id = @teacher_id;
        UPDATE lessons SET teacher_id = @new_teacher_id, updated_at = now() WHERE teacher_id = @teacher_id;
        UPDATE skill_assessments SET teacher_id = @new_teacher_id WHERE teacher_id = @teacher_id;
        UPDATE lesson_notes SET teacher_id = @new_teacher_id WHERE teacher_id = @teacher_id;
        UPDATE show_items SET teacher_id = @new_teacher_id WHERE teacher_id = @teacher_id;
        UPDATE library_suggestions SET teacher_id = @new_teacher_id WHERE teacher_id = @teacher_id;
        """;

    private const string TeacherSql = """
        CREATE TEMP TABLE _tenr ON COMMIT DROP AS
            SELECT id FROM enrollments WHERE teacher_id = @teacher_id;
        CREATE TEMP TABLE _tser ON COMMIT DROP AS
            SELECT id FROM lesson_series WHERE enrollment_id IN (SELECT id FROM _tenr);
        CREATE TEMP TABLE _tles ON COMMIT DROP AS
            SELECT id FROM lessons
            WHERE teacher_id = @teacher_id OR lesson_series_id IN (SELECT id FROM _tser);
        CREATE TEMP TABLE _trec ON COMMIT DROP AS
            SELECT id FROM receivables WHERE enrollment_id IN (SELECT id FROM _tenr);
        CREATE TEMP TABLE _tpay ON COMMIT DROP AS
            SELECT id FROM payments WHERE receivable_id IN (SELECT id FROM _trec);

        DELETE FROM payment_corrections WHERE payment_id IN (SELECT id FROM _tpay);
        DELETE FROM payments WHERE id IN (SELECT id FROM _tpay);
        UPDATE bank_incoming_transactions
           SET matched_receivable_id = NULL, status = 'NeedsReview'
         WHERE matched_receivable_id IN (SELECT id FROM _trec);
        DELETE FROM notification_jobs
         WHERE reference_id IN (SELECT id FROM _tles) OR reference_id IN (SELECT id FROM _trec);
        DELETE FROM staff_notifications
         WHERE reference_id IN (SELECT id FROM _tles)
            OR reference_id IN (SELECT id FROM _trec)
            OR reference_id = @teacher_id;
        DELETE FROM receivables WHERE id IN (SELECT id FROM _trec);

        DELETE FROM practice_assignments WHERE lesson_id IN (SELECT id FROM _tles);
        DELETE FROM skill_assessments
         WHERE teacher_id = @teacher_id OR lesson_id IN (SELECT id FROM _tles);
        DELETE FROM lesson_notes WHERE teacher_id = @teacher_id OR lesson_id IN (SELECT id FROM _tles);
        -- AI gelişim yorumu önbelleği: devirde notlar yeni öğretmene geçer, onun yorumu bir
        -- sonraki açılışta not sayısı değiştiği için kendiliğinden yeniden üretilir.
        DELETE FROM progress_summaries WHERE teacher_id = @teacher_id;
        DELETE FROM lesson_rsvps WHERE lesson_id IN (SELECT id FROM _tles);
        DELETE FROM lesson_attendances WHERE lesson_id IN (SELECT id FROM _tles);
        DELETE FROM lesson_change_requests WHERE lesson_id IN (SELECT id FROM _tles);

        DELETE FROM lessons WHERE id IN (SELECT id FROM _tles);
        DELETE FROM lesson_series WHERE id IN (SELECT id FROM _tser);
        DELETE FROM enrollments WHERE id IN (SELECT id FROM _tenr);

        UPDATE show_items SET teacher_id = NULL WHERE teacher_id = @teacher_id;
        -- Öneri öğrencinin repertuvarıdır ve kalır, eklenen eser okulun kütüphanesidir ve kalır;
        -- yalnızca silinen öğretmene/hesabına işaret eden alan boşalır.
        UPDATE library_suggestions SET teacher_id = NULL WHERE teacher_id = @teacher_id;
        DELETE FROM teacher_availability WHERE teacher_id = @teacher_id;
        DELETE FROM teacher_time_off WHERE teacher_id = @teacher_id;
        DELETE FROM teacher_instruments WHERE teacher_id = @teacher_id;

        -- Öğretmenin giriş hesabı da gider; kalırsa sahipsiz bir kimlik olurdu.
        CREATE TEMP TABLE _tuser ON COMMIT DROP AS
            SELECT user_id AS id FROM teachers WHERE id = @teacher_id AND user_id IS NOT NULL;
        UPDATE library_pieces SET created_by_user_id = NULL WHERE created_by_user_id IN (SELECT id FROM _tuser);
        DELETE FROM teachers WHERE id = @teacher_id;
        DELETE FROM users WHERE id IN (SELECT id FROM _tuser);
        """;
}
