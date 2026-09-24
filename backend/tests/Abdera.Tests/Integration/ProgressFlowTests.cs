using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Abdera.Tests.Integration;

// Progress modülünü yalnızca entity seviyesinde değil gerçek cookie auth + Minimal API +
// PostgreSQL zinciri üzerinden korur. Özellikle öğretmen scope'u URL'deki student/lesson id'sine
// güvenmeden sunucu tarafında doğrulanmalı; bu dosyanın negatif testleri o güvenlik sınırıdır.
public class ProgressFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public ProgressFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Assigned_teacher_creates_trimmed_note_and_admin_reads_cumulative_progress()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "progress-happy");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword);

        var createResponse = await teacher.PostAsJsonAsync(
            $"/api/lessons/{seeded.LessonId}/notes",
            new LessonNotes.CreateRequest(
                "  Do majör gamı  ",
                "  Ritim daha dengeli.  ",
                "  Metronomla 15 dakika  ",
                "  80 BPM'e çıkmak  ",
                "  Bach · Minuet in G  ",
                4));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<LessonNotes.LessonNoteResponse>(TestJson.Options))!;
        Assert.Equal(seeded.LessonId, created.LessonId);
        Assert.Equal(seeded.TeacherId, created.TeacherId);
        Assert.Equal("Do majör gamı", created.Practiced);
        Assert.Equal("Ritim daha dengeli.", created.Note);
        Assert.Equal("Metronomla 15 dakika", created.Homework);
        Assert.Equal("80 BPM'e çıkmak", created.NextGoal);
        Assert.Equal("Bach · Minuet in G", created.PieceTitle);
        Assert.Equal(4, created.PieceDifficulty);
        Assert.Equal($"/api/lessons/{seeded.LessonId}/notes/{created.Id}", createResponse.Headers.Location?.ToString());

        var parentCommentResponse = await teacher.PutAsJsonAsync(
            $"/api/lesson-notes/{created.Id}/parent-comment",
            new LessonNotes.ParentCommentRequest("Ritmi belirgin biçimde dengelendi; düzenli çalışmayla tempo hedefini yakalıyor.", true));
        Assert.Equal(HttpStatusCode.OK, parentCommentResponse.StatusCode);
        var approvedComment = (await parentCommentResponse.Content.ReadFromJsonAsync<LessonNotes.LessonNoteResponse>(TestJson.Options))!;
        Assert.NotNull(approvedComment.ParentCommentApprovedAt);
        Assert.Equal(seeded.TeacherId, approvedComment.ParentCommentApprovedBy);

        await using (var auditDb = await _factory.CreateDbContextAsync())
        {
            Assert.True(await auditDb.AuditLogs.AnyAsync(item => item.Action == "lesson_note.created" && item.EntityId == created.Id));
            Assert.True(await auditDb.AuditLogs.AnyAsync(item => item.Action == "lesson_note.parent_comment_approved" && item.EntityId == created.Id));
        }

        var teacherListResponse = await teacher.GetAsync($"/api/lessons/{seeded.LessonId}/notes");
        Assert.Equal(HttpStatusCode.OK, teacherListResponse.StatusCode);
        var notes = await teacherListResponse.Content.ReadFromJsonAsync<List<LessonNotes.LessonNoteResponse>>(TestJson.Options);
        Assert.Contains(notes!, note => note.Id == created.Id);

        var progressResponse = await admin.GetAsync($"/api/students/{seeded.StudentId}/progress");
        Assert.Equal(HttpStatusCode.OK, progressResponse.StatusCode);
        var progress = (await progressResponse.Content.ReadFromJsonAsync<StudentProgress.ProgressResponse>(TestJson.Options))!;
        Assert.Equal(seeded.StudentId, progress.StudentId);
        Assert.Equal("Öğrenciprogress-happy Soyad", progress.StudentName);
        Assert.Equal(1, progress.EntryCount);
        // Create yanıtındaki DateTimeOffset 100ns hassasiyetinde olabilir; PostgreSQL
        // timestamptz ise mikrosaniyeye yuvarlar. Aynı anı temsil eden değerleri
        // veritabanının gerçek hassasiyetinde karşılaştır.
        Assert.NotNull(progress.LastEntryAt);
        Assert.InRange(
            (created.CreatedAt - progress.LastEntryAt.Value).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromMicroseconds(1));

        var entry = Assert.Single(progress.Entries);
        Assert.Equal(created.Id, entry.Id);
        Assert.Equal(seeded.LessonId, entry.LessonId);
        Assert.Equal(seeded.TeacherId, entry.TeacherId);
        Assert.Equal("Öğretmenprogress-happy Soyad", entry.TeacherName);
        Assert.Equal(seeded.InstrumentName, entry.InstrumentName);
        Assert.Equal("Bach · Minuet in G", entry.PieceTitle);
        Assert.Equal(4, entry.PieceDifficulty);
        Assert.Equal(approvedComment.ParentComment, entry.ParentComment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Piece_difficulty_outside_one_to_five_is_rejected_without_persisting(int difficulty)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, $"progress-invalid-{difficulty}");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword);

        var response = await teacher.PostAsJsonAsync(
            $"/api/lessons/{seeded.LessonId}/notes",
            new LessonNotes.CreateRequest(null, "Not", null, null, "Eser", difficulty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problemBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("1 ile 5", problemBody);
        Assert.False(await db.LessonNotes.AnyAsync(note => note.LessonId == seeded.LessonId));
    }

    [Fact]
    public async Task Admin_has_read_only_progress_access_and_cannot_create_lesson_note()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "progress-admin-readonly");

        var createResponse = await admin.PostAsJsonAsync(
            $"/api/lessons/{seeded.LessonId}/notes",
            new LessonNotes.CreateRequest(null, "Admin notu", null, null));

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/lessons/{seeded.LessonId}/notes")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/students/{seeded.StudentId}/progress")).StatusCode);
    }

    [Fact]
    public async Task Unassigned_teacher_cannot_read_or_write_another_teachers_progress_data()
    {
        var admin = await CreateAdminClientAsync();
        var owner = await SeedLessonAsync(admin, "progress-owner");
        var unrelated = await SeedLessonAsync(admin, "progress-unrelated");
        using var unrelatedTeacher = await LoginTeacherAsync(
            unrelated.TeacherEmail,
            unrelated.TeacherTemporaryPassword);

        var createResponse = await unrelatedTeacher.PostAsJsonAsync(
            $"/api/lessons/{owner.LessonId}/notes",
            new LessonNotes.CreateRequest(null, "Yetkisiz", null, null));
        var listResponse = await unrelatedTeacher.GetAsync($"/api/lessons/{owner.LessonId}/notes");
        var progressResponse = await unrelatedTeacher.GetAsync($"/api/students/{owner.StudentId}/progress");

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, progressResponse.StatusCode);
    }

    [Fact]
    public async Task Progress_timeline_is_newest_first_and_empty_student_has_null_last_entry()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "progress-order");

        var emptyResponse = await admin.GetAsync($"/api/students/{seeded.StudentId}/progress");
        var empty = (await emptyResponse.Content.ReadFromJsonAsync<StudentProgress.ProgressResponse>(TestJson.Options))!;
        Assert.Equal(0, empty.EntryCount);
        Assert.Null(empty.LastEntryAt);
        Assert.Empty(empty.Entries);

        var olderAt = new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
        var newerAt = olderAt.AddDays(1);
        var older = LessonNote.Create(
            seeded.LessonId, seeded.TeacherId, null, "Eski not", null, null, null, null, olderAt);
        var newer = LessonNote.Create(
            seeded.LessonId, seeded.TeacherId, null, "Yeni not", null, null, null, null, newerAt);
        db.LessonNotes.AddRange(older, newer);
        await db.SaveChangesAsync();

        var response = await admin.GetAsync($"/api/students/{seeded.StudentId}/progress");
        var progress = (await response.Content.ReadFromJsonAsync<StudentProgress.ProgressResponse>(TestJson.Options))!;

        Assert.Equal(2, progress.EntryCount);
        Assert.Equal(newerAt, progress.LastEntryAt);
        Assert.Equal([newer.Id, older.Id], progress.Entries.Select(entry => entry.Id).ToArray());
    }

    [Fact]
    public async Task Missing_lesson_and_student_return_not_found_instead_of_empty_success()
    {
        var admin = await CreateAdminClientAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/lessons/{Guid.NewGuid()}/notes")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/students/{Guid.NewGuid()}/progress")).StatusCode);
    }

    [Fact]
    public async Task Skill_definitions_are_seeded_and_instrument_filter_includes_common_plus_matching_skills()
    {
        var admin = await CreateAdminClientAsync();
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>(
            "/api/instruments", TestJson.Options);
        var piano = instruments!.Single(item => item.Code == "PIANO");

        var all = await admin.GetFromJsonAsync<List<SkillAssessments.SkillDefinitionResponse>>(
            "/api/skill-definitions", TestJson.Options);
        var pianoSkills = await admin.GetFromJsonAsync<List<SkillAssessments.SkillDefinitionResponse>>(
            $"/api/skill-definitions?instrumentId={piano.Id}", TestJson.Options);

        Assert.Contains(all!, skill => skill.Code == "RHYTHM" && skill.InstrumentId is null);
        Assert.Contains(all!, skill => skill.Code == "CHORD_TRANSITION" && skill.InstrumentId is not null);
        Assert.Contains(pianoSkills!, skill => skill.Code == "RHYTHM" && skill.InstrumentId is null);
        Assert.Contains(pianoSkills!, skill => skill.Code == "HAND_COORDINATION" && skill.InstrumentId == piano.Id);
        Assert.DoesNotContain(pianoSkills!, skill => skill.Code == "CHORD_TRANSITION");
    }

    [Fact]
    public async Task Progress_database_constraints_reject_duplicate_skill_code_and_out_of_range_score()
    {
        await using var duplicateContext = await _factory.CreateDbContextAsync();
        duplicateContext.SkillDefinitions.Add(SkillDefinition.Create("rhythm", "Tekrar Ritim"));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());

        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "progress-db-constraint");
        var skillId = await db.SkillDefinitions
            .Where(skill => skill.Code == "RHYTHM")
            .Select(skill => skill.Id)
            .SingleAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO skill_assessments
                (id, student_id, skill_definition_id, teacher_id, lesson_id, score, note, assessed_at)
            VALUES
                ({Guid.NewGuid()}, {seeded.StudentId}, {skillId}, {seeded.TeacherId}, {seeded.LessonId}, {6}, {"geçersiz"}, {DateTimeOffset.UtcNow})
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_skill_assessments_score", exception.ConstraintName);
    }

    [Fact]
    public async Task Assigned_teacher_records_lesson_skill_assessment_and_history_is_newest_first()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "skill-happy");
        var definitions = await admin.GetFromJsonAsync<List<SkillAssessments.SkillDefinitionResponse>>(
            "/api/skill-definitions", TestJson.Options);
        Assert.NotNull(definitions);
        var rhythm = definitions.Single(skill => skill.Code == "RHYTHM");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword);

        var firstResponse = await teacher.PostAsJsonAsync(
            $"/api/students/{seeded.StudentId}/skill-assessments",
            new SkillAssessments.CreateRequest(rhythm.Id, seeded.LessonId, 3, "  Temel ritim oturuyor  "));
        await Task.Delay(10);
        var secondResponse = await teacher.PostAsJsonAsync(
            $"/api/students/{seeded.StudentId}/skill-assessments",
            new SkillAssessments.CreateRequest(rhythm.Id, seeded.LessonId, 4, "Daha dengeli"));

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var first = (await firstResponse.Content.ReadFromJsonAsync<SkillAssessments.AssessmentResponse>(TestJson.Options))!;
        var second = (await secondResponse.Content.ReadFromJsonAsync<SkillAssessments.AssessmentResponse>(TestJson.Options))!;
        Assert.Equal(seeded.TeacherId, first.TeacherId);
        Assert.Equal(seeded.LessonId, first.LessonId);
        Assert.Equal("RHYTHM", first.SkillCode);
        Assert.Equal("Temel ritim oturuyor", first.Note);

        var historyResponse = await admin.GetAsync($"/api/students/{seeded.StudentId}/skill-assessments");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadFromJsonAsync<List<SkillAssessments.AssessmentResponse>>(TestJson.Options);
        Assert.Equal(2, history!.Count);
        Assert.Equal(second.Id, history[0].Id);
        Assert.Equal(first.Id, history[1].Id);

        var progress = await admin.GetFromJsonAsync<StudentProgress.ProgressResponse>(
            $"/api/students/{seeded.StudentId}/progress", TestJson.Options);
        Assert.Equal([second.Id, first.Id], progress!.SkillAssessments.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task Skill_assessment_enforces_score_role_student_scope_and_instrument_compatibility()
    {
        var admin = await CreateAdminClientAsync();
        var owner = await SeedLessonAsync(admin, "skill-owner");
        var unrelated = await SeedLessonAsync(admin, "skill-unrelated");
        var definitions = await admin.GetFromJsonAsync<List<SkillAssessments.SkillDefinitionResponse>>(
            "/api/skill-definitions", TestJson.Options);
        Assert.NotNull(definitions);
        var rhythm = definitions.Single(skill => skill.Code == "RHYTHM");
        var guitarSkill = definitions.Single(skill => skill.Code == "CHORD_TRANSITION");
        using var ownerTeacher = await LoginTeacherAsync(owner.TeacherEmail, owner.TeacherTemporaryPassword);
        using var unrelatedTeacher = await LoginTeacherAsync(unrelated.TeacherEmail, unrelated.TeacherTemporaryPassword);

        var invalidScore = await ownerTeacher.PostAsJsonAsync(
            $"/api/students/{owner.StudentId}/skill-assessments",
            new SkillAssessments.CreateRequest(rhythm.Id, owner.LessonId, 6, null));
        var wrongInstrument = await ownerTeacher.PostAsJsonAsync(
            $"/api/students/{owner.StudentId}/skill-assessments",
            new SkillAssessments.CreateRequest(guitarSkill.Id, owner.LessonId, 3, null));
        var unrelatedStudent = await unrelatedTeacher.PostAsJsonAsync(
            $"/api/students/{owner.StudentId}/skill-assessments",
            new SkillAssessments.CreateRequest(rhythm.Id, owner.LessonId, 3, null));
        var adminWrite = await admin.PostAsJsonAsync(
            $"/api/students/{owner.StudentId}/skill-assessments",
            new SkillAssessments.CreateRequest(rhythm.Id, owner.LessonId, 3, null));

        Assert.Equal(HttpStatusCode.BadRequest, invalidScore.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongInstrument.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unrelatedStudent.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, adminWrite.StatusCode);
    }

    [Fact]
    public async Task Teacher_creates_lists_and_completes_practice_assignment_while_admin_remains_read_only()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "practice-happy");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword);
        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));

        var createResponse = await teacher.PostAsJsonAsync(
            $"/api/lessons/{seeded.LessonId}/practice-assignments",
            new PracticeAssignments.CreateRequest("  Her gün 15 dakika metronom  ", dueDate));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<PracticeAssignments.AssignmentResponse>(TestJson.Options))!;
        Assert.Equal("Her gün 15 dakika metronom", created.Description);
        Assert.Equal(dueDate, created.DueDate);
        Assert.False(created.Completed);

        var adminList = await admin.GetFromJsonAsync<List<PracticeAssignments.AssignmentResponse>>(
            $"/api/lessons/{seeded.LessonId}/practice-assignments", TestJson.Options);
        Assert.Contains(adminList!, item => item.Id == created.Id);

        var adminCreate = await admin.PostAsJsonAsync(
            $"/api/lessons/{seeded.LessonId}/practice-assignments",
            new PracticeAssignments.CreateRequest("Admin ödevi", null));
        Assert.Equal(HttpStatusCode.Forbidden, adminCreate.StatusCode);

        var complete = await teacher.PatchAsync($"/api/practice-assignments/{created.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var completed = (await complete.Content.ReadFromJsonAsync<PracticeAssignments.AssignmentResponse>(TestJson.Options))!;
        Assert.True(completed.Completed);

        var duplicateComplete = await teacher.PatchAsync($"/api/practice-assignments/{created.Id}/complete", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicateComplete.StatusCode);
    }

    [Fact]
    public async Task Unassigned_teacher_cannot_read_create_or_complete_practice_assignment()
    {
        var admin = await CreateAdminClientAsync();
        var owner = await SeedLessonAsync(admin, "practice-owner");
        var unrelated = await SeedLessonAsync(admin, "practice-unrelated");
        using var ownerTeacher = await LoginTeacherAsync(owner.TeacherEmail, owner.TeacherTemporaryPassword);
        using var unrelatedTeacher = await LoginTeacherAsync(unrelated.TeacherEmail, unrelated.TeacherTemporaryPassword);
        var createdResponse = await ownerTeacher.PostAsJsonAsync(
            $"/api/lessons/{owner.LessonId}/practice-assignments",
            new PracticeAssignments.CreateRequest("Gam", null));
        var created = (await createdResponse.Content.ReadFromJsonAsync<PracticeAssignments.AssignmentResponse>(TestJson.Options))!;

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await unrelatedTeacher.GetAsync($"/api/lessons/{owner.LessonId}/practice-assignments")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await unrelatedTeacher.PostAsJsonAsync(
                $"/api/lessons/{owner.LessonId}/practice-assignments",
                new PracticeAssignments.CreateRequest("Yetkisiz", null))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await unrelatedTeacher.PatchAsync($"/api/practice-assignments/{created.Id}/complete", null)).StatusCode);
    }

    // --- "Genel gelişim" AI yorumu ---
    // Test ortamında Ai:Provider ayarlanmadığı için DisabledProgressSummaryGenerator aktif;
    // üretim yolunu sınayan testler sahte bir üreteci WithWebHostBuilder ile enjekte eder.

    [Fact]
    public async Task Progress_summary_reports_no_notes_and_unavailable_provider_without_failing()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "summary-disabled");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword);

        var empty = await GetSummaryAsync(teacher, seeded.StudentId);
        Assert.Equal(StudentProgressSummary.SummaryStatus.NoNotes, empty.Status);

        await CreateNoteAsync(teacher, seeded.LessonId, "Sol el zayıf, tempo dalgalı.");
        // İlk yorum için 4 not gerekli (kullanıcı kuralı) - sağlayıcıya hiç gidilmez.
        var tooFew = await GetSummaryAsync(teacher, seeded.StudentId);
        Assert.Equal(StudentProgressSummary.SummaryStatus.NotEnoughNotes, tooFew.Status);
        Assert.Equal(1, tooFew.NoteCount);
        Assert.Equal(4, tooFew.MinimumNotes);

        await CreateNotesAsync(teacher, seeded.LessonId, 3, "Ek not");
        var disabled = await GetSummaryAsync(teacher, seeded.StudentId);

        Assert.Equal(StudentProgressSummary.SummaryStatus.Unavailable, disabled.Status);
        Assert.Null(disabled.Summary);
        Assert.Equal(4, disabled.SourceNoteCount);
    }

    [Fact]
    public async Task Progress_summary_is_generated_after_four_notes_then_refreshed_at_most_monthly()
    {
        var generator = new FakeSummaryGenerator();
        using var factory = WithSummaryGenerator(generator);
        var admin = await CreateAdminClientAsync(factory);
        var seeded = await SeedLessonAsync(admin, "summary-cache");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword, factory);
        await CreateNoteAsync(teacher, seeded.LessonId, "İlk ders: ritim dağınık.");
        await CreateNotesAsync(teacher, seeded.LessonId, 3, "Ara ders");

        var first = await GetSummaryAsync(teacher, seeded.StudentId);
        var second = await GetSummaryAsync(teacher, seeded.StudentId);

        Assert.Equal(StudentProgressSummary.SummaryStatus.Ready, first.Status);
        Assert.Equal("Yorum #1", first.Summary);
        Assert.Equal("Yorum #1", second.Summary);
        Assert.Single(generator.Requests);
        Assert.Equal($"Öğrencisummary-cache", generator.Requests[0].StudentFirstName);

        // Aynı ay içinde yeni not: kayıtlı yorum gösterilir, sağlayıcıya gidilmez.
        await CreateNoteAsync(teacher, seeded.LessonId, "Son ders: ritim oturuyor.");
        var sameMonth = await GetSummaryAsync(teacher, seeded.StudentId);
        Assert.Equal("Yorum #1", sameMonth.Summary);
        Assert.Equal(4, sameMonth.SourceNoteCount);
        Assert.Equal(5, sameMonth.NoteCount);
        Assert.False(sameMonth.IsStale);
        Assert.NotNull(sameMonth.NextRefreshOn);
        Assert.Single(generator.Requests);

        // Ay değişti (yorum geçen ay üretilmiş gibi): yeni notlarla bir kez yeniden üretilir.
        await using var db = await _factory.CreateDbContextAsync();
        await db.ProgressSummaries.Where(summary => summary.StudentId == seeded.StudentId)
            .ExecuteUpdateAsync(set => set.SetProperty(summary => summary.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-40)));
        var refreshed = await GetSummaryAsync(teacher, seeded.StudentId);

        Assert.Equal("Yorum #2", refreshed.Summary);
        Assert.Equal(5, refreshed.SourceNoteCount);
        Assert.False(refreshed.IsStale);
        // Notlar eskiden yeniye gider - model zaman içindeki değişimi okuyabilsin.
        Assert.Equal("İlk ders: ritim dağınık.", generator.Requests[1].Notes.First().Note);
        Assert.Equal("Son ders: ritim oturuyor.", generator.Requests[1].Notes.Last().Note);
        Assert.Equal("Yorum #2", (await GetSummaryAsync(teacher, seeded.StudentId)).Summary);
        Assert.Equal(2, generator.Requests.Count);

        var row = await db.ProgressSummaries.SingleAsync(summary => summary.StudentId == seeded.StudentId);
        Assert.Equal(seeded.TeacherId, row.TeacherId);
        Assert.Equal("fake-model", row.Model);
    }

    [Fact]
    public async Task Admin_and_teacher_summaries_are_cached_separately()
    {
        // Öğretmen yalnızca kendi notlarını görür; yöneticinin tüm notlardan üretilen yorumu
        // öğretmene dönmemeli (başka öğretmenin notlarını dolaylı sızdırırdı).
        var generator = new FakeSummaryGenerator();
        using var factory = WithSummaryGenerator(generator);
        var admin = await CreateAdminClientAsync(factory);
        var seeded = await SeedLessonAsync(admin, "summary-scope");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword, factory);
        await CreateNotesAsync(teacher, seeded.LessonId, 4, "Ders notu");

        await GetSummaryAsync(admin, seeded.StudentId);
        await GetSummaryAsync(teacher, seeded.StudentId);

        Assert.Equal(2, generator.Requests.Count);
        await using var db = await _factory.CreateDbContextAsync();
        var scopes = await db.ProgressSummaries.Where(summary => summary.StudentId == seeded.StudentId)
            .Select(summary => summary.TeacherId).ToListAsync();
        Assert.Contains(null, scopes);
        Assert.Contains(seeded.TeacherId, scopes);
    }

    [Fact]
    public async Task Provider_failure_keeps_the_previous_summary_marked_as_stale()
    {
        var generator = new FakeSummaryGenerator();
        using var factory = WithSummaryGenerator(generator);
        var admin = await CreateAdminClientAsync(factory);
        var seeded = await SeedLessonAsync(admin, "summary-failure");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword, factory);
        await CreateNotesAsync(teacher, seeded.LessonId, 4, "Not");
        await GetSummaryAsync(teacher, seeded.StudentId);

        generator.Fail = true;
        await CreateNoteAsync(teacher, seeded.LessonId, "Beşinci not.");
        await using (var db = await _factory.CreateDbContextAsync())
        {
            await db.ProgressSummaries.Where(summary => summary.StudentId == seeded.StudentId)
                .ExecuteUpdateAsync(set => set.SetProperty(summary => summary.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-40)));
        }
        var afterFailure = await GetSummaryAsync(teacher, seeded.StudentId);

        Assert.Equal(StudentProgressSummary.SummaryStatus.Ready, afterFailure.Status);
        Assert.Equal("Yorum #1", afterFailure.Summary);
        Assert.True(afterFailure.IsStale);
    }

    [Fact]
    public async Task Progress_summary_is_refused_for_an_unassigned_teacher()
    {
        var admin = await CreateAdminClientAsync();
        var owner = await SeedLessonAsync(admin, "summary-owner");
        var unrelated = await SeedLessonAsync(admin, "summary-stranger");
        using var strangerTeacher = await LoginTeacherAsync(unrelated.TeacherEmail, unrelated.TeacherTemporaryPassword);

        var response = await strangerTeacher.GetAsync($"/api/students/{owner.StudentId}/progress-summary");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task CreateNotesAsync(HttpClient teacher, Guid lessonId, int count, string prefix)
    {
        for (var index = 1; index <= count; index++)
            await CreateNoteAsync(teacher, lessonId, $"{prefix} {index}.");
    }

    private static async Task<StudentProgressSummary.Response> GetSummaryAsync(HttpClient client, Guid studentId)
    {
        var response = await client.GetAsync($"/api/students/{studentId}/progress-summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudentProgressSummary.Response>(TestJson.Options))!;
    }

    private WebApplicationFactory<Program> WithSummaryGenerator(IProgressSummaryGenerator generator) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProgressSummaryGenerator>();
            services.AddSingleton(generator);
        }));

    private sealed class FakeSummaryGenerator : IProgressSummaryGenerator
    {
        private readonly List<ProgressSummaryRequest> _requests = [];

        public bool Fail { get; set; }
        public bool IsAvailable => true;
        public string ModelName => "fake-model";
        public IReadOnlyList<ProgressSummaryRequest> Requests { get { lock (_requests) return _requests.ToList(); } }

        public Task<ProgressSummaryResult> GenerateAsync(ProgressSummaryRequest request, CancellationToken cancellationToken = default)
        {
            int count;
            lock (_requests)
            {
                _requests.Add(request);
                count = _requests.Count;
            }
            return Task.FromResult(Fail
                ? new ProgressSummaryResult(false, null, "sağlayıcı hatası")
                : new ProgressSummaryResult(true, $"Yorum #{count}", null));
        }
    }

    private static async Task<LessonNotes.LessonNoteResponse> CreateNoteAsync(HttpClient teacher, Guid lessonId, string note)
    {
        var response = await teacher.PostAsJsonAsync(
            $"/api/lessons/{lessonId}/notes",
            new LessonNotes.CreateRequest(null, note, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LessonNotes.LessonNoteResponse>(TestJson.Options))!;
    }

    private async Task<HttpClient> CreateAdminClientAsync(WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? _factory).CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> LoginTeacherAsync(string email, string temporaryPassword, WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? _factory).CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new Login.Request(email, temporaryPassword));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<SeededLesson> SeedLessonAsync(HttpClient admin, string suffix)
    {
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(instrument => instrument.Code == "PIANO");

        var teacherEmail = $"teacher-{suffix}@test.local";
        var teacher = (await (await admin.PostAsJsonAsync(
                "/api/teachers",
                new Teachers.CreateRequest($"Öğretmen{suffix}", "Soyad", [piano.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;

        var student = (await (await admin.PostAsJsonAsync(
                "/api/students",
                new Students.CreateRequest($"Öğrenci{suffix}", "Soyad", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var enrollment = (await (await admin.PostAsJsonAsync(
                $"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Teacher.Id, piano.Id, DateOnly.FromDateTime(DateTime.UtcNow))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var seriesResponse = await admin.PostAsJsonAsync(
            "/api/lesson-series",
            new LessonSeriesFeatures.CreateRequest(
                enrollment.Id,
                DayOfWeek.Saturday,
                new TimeOnly(11, 0),
                45,
                DateOnly.FromDateTime(DateTime.UtcNow),
                null));
        Assert.Equal(HttpStatusCode.Created, seriesResponse.StatusCode);

        var calendarResponse = await admin.GetAsync(
            $"/api/calendar?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}" +
            $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(90).ToString("O"))}" +
            $"&teacherId={teacher.Teacher.Id}");
        calendarResponse.EnsureSuccessStatusCode();
        var lessons = await calendarResponse.Content.ReadFromJsonAsync<List<Calendar.LessonResponse>>(TestJson.Options);
        var lesson = lessons!.OrderBy(item => item.StartAt).First();

        return new SeededLesson(
            lesson.Id,
            student.Id,
            teacher.Teacher.Id,
            teacherEmail,
            teacher.TemporaryPassword!,
            piano.Name);
    }

    private record SeededLesson(
        Guid LessonId,
        Guid StudentId,
        Guid TeacherId,
        string TeacherEmail,
        string TeacherTemporaryPassword,
        string InstrumentName);
}
