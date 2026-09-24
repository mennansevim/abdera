using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Library.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;

namespace Abdera.Tests.Integration;

// Nota kitabı PDF'leri yayıncı izniyle yalnızca okul içinde kullanılabilir: dosyayı giriş yapmış
// öğretmen ve yönetici görür, yalnızca yönetici yükler. Bu testlerin asıl bekçiliği yetki sınırı.
public class LibraryFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private static readonly byte[] SamplePdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");
    private readonly AbderaWebApplicationFactory _factory;

    public LibraryFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Teacher_can_list_and_open_an_uploaded_score_but_anonymous_cannot()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var entryId = $"book-test-{Guid.NewGuid():N}"[..30];

        var upload = await admin.PutAsync($"/api/library/score-files/{entryId}", PdfForm(SamplePdf, pageCount: 2));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var teacher = await CreateTeacherClientAsync(admin);
        var files = await teacher.GetFromJsonAsync<List<ScoreFiles.Summary>>("/api/library/score-files", TestJson.Options);
        var summary = Assert.Single(files!, f => f.EntryId == entryId);
        Assert.Equal(2, summary.PageCount);

        var download = await teacher.GetAsync($"/api/library/score-files/{entryId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(SamplePdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Contains("private", download.Headers.CacheControl!.ToString());

        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/library/score-files")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/library/score-files/{entryId}")).StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_upload_a_score()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var teacher = await CreateTeacherClientAsync(admin);

        var response = await teacher.PutAsync("/api/library/score-files/book-test-teacher-1", PdfForm(SamplePdf, pageCount: 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_a_file_that_is_not_a_pdf()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync("admin@test.local", "Test1234!");

        var response = await admin.PutAsync("/api/library/score-files/book-test-notpdf-1",
            PdfForm(Encoding.ASCII.GetBytes("<html>not a score</html>"), pageCount: 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var list = await admin.GetFromJsonAsync<List<ScoreFiles.Summary>>("/api/library/score-files", TestJson.Options);
        Assert.DoesNotContain(list!, f => f.EntryId == "book-test-notpdf-1");
    }

    [Fact]
    public async Task Teacher_suggests_a_piece_only_to_own_students()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var (teacher, teacherId) = await CreateTeacherAsync(admin);
        var ownStudent = await CreateStudentAsync(admin, "Kendi");
        var otherStudent = await CreateStudentAsync(admin, "Baska");
        await EnrollAsync(admin, ownStudent, teacherId);

        var suggestion = new LibrarySuggestions.Request("book-piyano-albumu-50", "Sonatin (Sol Majör)", "A. Diabelli", "Önce 1. bölüm");
        var created = await teacher.PostAsJsonAsync($"/api/students/{ownStudent}/library-suggestions", suggestion);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var list = await teacher.GetFromJsonAsync<List<LibrarySuggestions.SuggestionResponse>>(
            $"/api/students/{ownStudent}/library-suggestions", TestJson.Options);
        var item = Assert.Single(list!);
        Assert.Equal("Sonatin (Sol Majör)", item.Title);
        Assert.StartsWith("Kutuphane", item.SuggestedByName);

        // Aynı eser ikinci kez önerilemez; başka öğretmenin öğrencisine hiç önerilemez.
        Assert.Equal(HttpStatusCode.Conflict,
            (await teacher.PostAsJsonAsync($"/api/students/{ownStudent}/library-suggestions", suggestion)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync($"/api/students/{otherStudent}/library-suggestions", suggestion)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync($"/api/students/{otherStudent}/library-suggestions")).StatusCode);

        // Yönetici her öğrenciye önerebilir.
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/api/students/{otherStudent}/library-suggestions", suggestion)).StatusCode);
    }

    [Fact]
    public async Task Added_piece_and_its_pdf_can_be_changed_only_by_its_creator_or_an_admin()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var (creator, _) = await CreateTeacherAsync(admin);
        var (otherTeacher, _) = await CreateTeacherAsync(admin);

        var createdResponse = await creator.PostAsJsonAsync("/api/library/pieces",
            new LibraryPieces.Request("Rock groove 1", "Okul", "drums", "education", 1, "8'lik hi-hat"));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var piece = await createdResponse.Content.ReadFromJsonAsync<LibraryPieces.PieceResponse>(TestJson.Options);
        Assert.Equal($"piece-{piece!.Id:N}", piece.EntryId);

        // Tarayıcıdan yükleme sayfa sayısı göndermez.
        var upload = await creator.PutAsync($"/api/library/score-files/{piece.EntryId}", PdfForm(SamplePdf, pageCount: null));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherTeacher.GetAsync($"/api/library/score-files/{piece.EntryId}")).StatusCode);

        var visibleToOther = await otherTeacher.GetFromJsonAsync<List<LibraryPieces.PieceResponse>>("/api/library/pieces", TestJson.Options);
        Assert.False(Assert.Single(visibleToOther!, p => p.Id == piece.Id).CanEdit);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await otherTeacher.PutAsync($"/api/library/score-files/{piece.EntryId}", PdfForm(SamplePdf, pageCount: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacher.DeleteAsync($"/api/library/pieces/{piece.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/library/pieces/{piece.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await creator.GetAsync($"/api/library/score-files/{piece.EntryId}")).StatusCode);
    }

    [Fact]
    public async Task Adding_a_piece_rejects_an_unknown_instrument()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await LoginAsync("admin@test.local", "Test1234!");

        var response = await admin.PostAsJsonAsync("/api/library/pieces",
            new LibraryPieces.Request("Eser", null, "kazoo", "education", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static MultipartFormDataContent PdfForm(byte[] content, int? pageCount)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form = new MultipartFormDataContent { { file, "file", "score.pdf" } };
        if (pageCount is { } pages) form.Add(new StringContent(pages.ToString()), "pageCount");
        return form;
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request(email, password));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> CreateTeacherClientAsync(HttpClient admin) => (await CreateTeacherAsync(admin)).Client;

    private async Task<(HttpClient Client, Guid TeacherId)> CreateTeacherAsync(HttpClient admin)
    {
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(instrument => instrument.Code == "PIANO");
        var email = $"library.{Guid.NewGuid():N}@test.local";
        var created = await admin.PostAsJsonAsync("/api/teachers", new Teachers.CreateRequest("Kutuphane", "Ogretmen", [piano.Id], email));
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options);
        return (await LoginAsync(email, body!.TemporaryPassword!), body.Teacher.Id);
    }

    private static async Task<Guid> CreateStudentAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/students", new Students.CreateRequest($"{name}{Guid.NewGuid():N}"[..20], "Ogrenci", new DateOnly(2014, 1, 1)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!.Id;
    }

    private static async Task EnrollAsync(HttpClient admin, Guid studentId, Guid teacherId)
    {
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(instrument => instrument.Code == "PIANO");
        (await admin.PostAsJsonAsync($"/api/students/{studentId}/enrollments",
            new Enrollments.CreateRequest(teacherId, piano.Id, new DateOnly(2026, 9, 1), CourseKind.Individual))).EnsureSuccessStatusCode();
    }
}
