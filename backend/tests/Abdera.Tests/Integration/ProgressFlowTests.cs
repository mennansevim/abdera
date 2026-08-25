using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.EntityFrameworkCore;
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
        Assert.Equal(created.CreatedAt, progress.LastEntryAt);

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

    // --- Faz 10: "yapıcı metne dönüştür" (AI) ---
    // Test ortamında Ai:Provider ayarlanmadığı için DisabledConstructiveTextRewriter aktif.
    // Korunan davranış: özellik kapalıyken uç nokta ANLAŞILIR bir hata verir (500 değil) ve
    // veli yorumu akışının geri kalanı hiç bozulmaz.

    [Fact]
    public async Task Ai_suggestion_is_reported_as_unavailable_when_no_provider_is_configured()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "progress-ai-disabled");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword);
        var note = await CreateNoteAsync(teacher, seeded.LessonId, "Sol el zayıf, tempo dalgalı.");

        var response = await teacher.PostAsync($"/api/lesson-notes/{note.Id}/parent-comment/suggest", null);

        // Kontrollü 409 - unhandled 500 değil (feature_targets.md: "Unhandled 500 bırakma").
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("AI sağlayıcısı", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Manual_parent_comment_flow_still_works_completely_while_ai_is_disabled()
    {
        // feature_targets.md Faz 10 kabul kriteri: "AI sağlayıcısı yoksa mevcut metni bozma;
        // özellik kapalıyken manuel düzenleme akışı eksiksiz çalışmaya devam etsin."
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "progress-ai-manual-fallback");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword);
        var note = await CreateNoteAsync(teacher, seeded.LessonId, "Ham not.");

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await teacher.PostAsync($"/api/lesson-notes/{note.Id}/parent-comment/suggest", null)).StatusCode);

        // AI reddedildikten SONRA elle yazıp onaylamak sorunsuz çalışmalı.
        var manual = await teacher.PutAsJsonAsync(
            $"/api/lesson-notes/{note.Id}/parent-comment",
            new LessonNotes.ParentCommentRequest("Elle yazılmış yapıcı yorum.", true));

        Assert.Equal(HttpStatusCode.OK, manual.StatusCode);
        var saved = (await manual.Content.ReadFromJsonAsync<LessonNotes.LessonNoteResponse>(TestJson.Options))!;
        Assert.Equal("Elle yazılmış yapıcı yorum.", saved.ParentComment);
        Assert.NotNull(saved.ParentCommentApprovedAt);
    }

    [Fact]
    public async Task Ai_suggestion_is_refused_for_a_note_written_by_another_teacher()
    {
        // Yetki sınırı sağlayıcıdan ÖNCE kontrol edilmeli: yabancı bir öğretmen başka bir
        // öğretmenin ham notunu AI'ya göndertemez (ham not sızıntısı).
        var admin = await CreateAdminClientAsync();
        var owner = await SeedLessonAsync(admin, "progress-ai-owner");
        var unrelated = await SeedLessonAsync(admin, "progress-ai-stranger");
        using var ownerTeacher = await LoginTeacherAsync(owner.TeacherEmail, owner.TeacherTemporaryPassword);
        using var strangerTeacher = await LoginTeacherAsync(unrelated.TeacherEmail, unrelated.TeacherTemporaryPassword);
        var note = await CreateNoteAsync(ownerTeacher, owner.LessonId, "Sahibinin ham notu.");

        var response = await strangerTeacher.PostAsync($"/api/lesson-notes/{note.Id}/parent-comment/suggest", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_request_an_ai_suggestion()
    {
        // Veli yorumu öğretmenin sorumluluğunda - SetParentCommentAsync ile aynı sınır.
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "progress-ai-admin");
        using var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherTemporaryPassword);
        var note = await CreateNoteAsync(teacher, seeded.LessonId, "Ham not.");

        var response = await admin.PostAsync($"/api/lesson-notes/{note.Id}/parent-comment/suggest", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_endpoint_reports_ai_rewrite_as_unavailable_when_not_configured()
    {
        // Frontend butonu bu bayrağa göre açıp kapatıyor; yanlış olursa kullanıcı
        // çalışmayan bir düğmeye basar.
        var admin = await CreateAdminClientAsync();

        var me = await (await admin.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<Me.Response>(TestJson.Options);

        Assert.False(me!.AiRewriteAvailable);
    }

    private static async Task<LessonNotes.LessonNoteResponse> CreateNoteAsync(HttpClient teacher, Guid lessonId, string note)
    {
        var response = await teacher.PostAsJsonAsync(
            $"/api/lessons/{lessonId}/notes",
            new LessonNotes.CreateRequest(null, note, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LessonNotes.LessonNoteResponse>(TestJson.Options))!;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> LoginTeacherAsync(string email, string temporaryPassword)
    {
        var client = _factory.CreateClient();
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
