using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Show.Domain;
using Abdera.Api.Modules.Show.Features;

namespace Abdera.Tests.Integration;

// Yıl sonu gösterisi: program kurulumu ve gösteri gecesinin canlı akışı.
//
// Canlı akışın tamamı gerçek HTTP üzerinden çalıştırılıyor - sahne ekranı ŞU AN/SIRADAKİ/
// ONDAN SONRAKİ üçlüsünü tek bir yanıttan okuyor ve bu üçlünün kayması gösteri gecesinde
// fark edilecek bir hata olurdu.
public class ShowFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public ShowFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request(email, password));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private record Fixture(Guid ShowId, Guid PianoId, Guid TeacherId, string TeacherEmail, string TeacherPassword, Guid[] StudentIds);

    private async Task<Fixture> SeedShowAsync(HttpClient admin, string suffix)
    {
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(instrument => instrument.Code == "PIANO");

        var email = $"show.{suffix}@test.local";
        var teacherCreated = await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest($"Show{suffix}", "Ogretmen", [piano.Id], email)));

        var studentIds = new List<Guid>();
        foreach (var name in new[] { "Bir", "Iki", "Uc" })
        {
            var student = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync(
                "/api/students", new Students.CreateRequest($"{name}{suffix}", "Ogrenci", new DateOnly(2014, 1, 1))));
            studentIds.Add(student.Id);
        }

        var show = await ReadAsync<Shows.ShowDetailResponse>(await admin.PostAsJsonAsync(
            "/api/shows",
            new Shows.CreateRequest($"{suffix} Yıl Sonu Gösterisi", "Kültür Merkezi", new DateTimeOffset(2027, 6, 14, 16, 0, 0, TimeSpan.Zero))));

        return new Fixture(
            show.Id, piano.Id, teacherCreated.Teacher.Id, email,
            teacherCreated.TemporaryPassword!, studentIds.ToArray());
    }

    private static Shows.ItemRequest Performance(Guid studentId, Guid instrumentId, Guid teacherId, string group, string piece, string? composer = null, int? minutes = 4) =>
        new(ShowItemKind.Performance, group, studentId, instrumentId, teacherId, piece, composer, minutes, null);

    [Fact]
    public async Task Program_keeps_its_order_and_resolves_student_teacher_and_instrument_names()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "prog");

        foreach (var (studentIndex, piece) in new[] { (0, "Für Elise"), (1, "Gymnopédie No.1"), (2, "Menuet") }.Select(item => (item.Item1, item.Item2)))
        {
            await ReadAsync<Shows.ShowDetailResponse>(await admin.PostAsJsonAsync(
                $"/api/shows/{fixture.ShowId}/items",
                Performance(fixture.StudentIds[studentIndex], fixture.PianoId, fixture.TeacherId, "1. Bölüm", piece, "Besteci")));
        }

        var detail = await ReadAsync<Shows.ShowDetailResponse>(await admin.GetAsync($"/api/shows/{fixture.ShowId}"));

        Assert.Equal([0, 1, 2], detail.Items.Select(item => item.Position));
        Assert.Equal(["Für Elise", "Gymnopédie No.1", "Menuet"], detail.Items.Select(item => item.PieceTitle));
        Assert.All(detail.Items, item =>
        {
            Assert.Equal("Piyano", item.InstrumentName);
            Assert.StartsWith("Show", item.TeacherName);
            Assert.NotNull(item.StudentName);
            Assert.False(item.HasPhoto);
        });
        Assert.Equal(12, detail.TotalDurationMinutes);

        // Özet listesi tekil performer sayısını verir - "kaç öğrenci sahneye çıkıyor".
        var list = await ReadAsync<List<Shows.ShowSummaryResponse>>(await admin.GetAsync("/api/shows"));
        var summary = list.Single(item => item.Id == fixture.ShowId);
        Assert.Equal(3, summary.ItemCount);
        Assert.Equal(3, summary.PerformerCount);
    }

    [Fact]
    public async Task Reordering_rewrites_every_position_and_rejects_a_partial_list()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "order");

        Shows.ShowDetailResponse detail = null!;
        foreach (var (index, piece) in new[] { (0, "A"), (1, "B"), (2, "C") })
        {
            detail = await ReadAsync<Shows.ShowDetailResponse>(await admin.PostAsJsonAsync(
                $"/api/shows/{fixture.ShowId}/items",
                Performance(fixture.StudentIds[index], fixture.PianoId, fixture.TeacherId, "1. Bölüm", piece)));
        }

        var reversed = detail.Items.Select(item => item.Id).Reverse().ToList();
        var reordered = await ReadAsync<Shows.ShowDetailResponse>(await admin.PostAsJsonAsync(
            $"/api/shows/{fixture.ShowId}/items/reorder", new Shows.ReorderRequest(reversed)));

        Assert.Equal(["C", "B", "A"], reordered.Items.Select(item => item.PieceTitle));
        Assert.Equal([0, 1, 2], reordered.Items.Select(item => item.Position));

        // Eksik liste kabul edilmemeli - yarım kalmış bir sıralama programı bozardı.
        var partial = await admin.PostAsJsonAsync(
            $"/api/shows/{fixture.ShowId}/items/reorder", new Shows.ReorderRequest([reversed[0]]));
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_item_renumbers_the_rest_without_leaving_a_gap()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "delete");

        Shows.ShowDetailResponse detail = null!;
        foreach (var (index, piece) in new[] { (0, "A"), (1, "B"), (2, "C") })
        {
            detail = await ReadAsync<Shows.ShowDetailResponse>(await admin.PostAsJsonAsync(
                $"/api/shows/{fixture.ShowId}/items",
                Performance(fixture.StudentIds[index], fixture.PianoId, fixture.TeacherId, "1. Bölüm", piece)));
        }

        var middle = detail.Items[1].Id;
        var after = await ReadAsync<Shows.ShowDetailResponse>(
            await admin.DeleteAsync($"/api/shows/{fixture.ShowId}/items/{middle}"));

        Assert.Equal(["A", "C"], after.Items.Select(item => item.PieceTitle));
        Assert.Equal([0, 1], after.Items.Select(item => item.Position));
    }

    [Fact]
    public async Task Stage_walks_now_next_and_on_deck_forward_and_backward()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "stage");

        foreach (var (index, piece) in new[] { (0, "A"), (1, "B"), (2, "C") })
        {
            await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
                Performance(fixture.StudentIds[index], fixture.PianoId, fixture.TeacherId, "1. Bölüm", piece));
        }

        // Başlatmadan ilerletmek reddedilir.
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/shows/{fixture.ShowId}/advance", null)).StatusCode);

        var started = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/start", null));
        Assert.Equal(ShowEventStatus.Live, started.Status);
        Assert.Equal("A", started.Current?.PieceTitle);
        Assert.Equal("B", started.Next?.PieceTitle);
        Assert.Equal("C", started.OnDeck?.PieceTitle);
        Assert.Equal(3, started.TotalItems);
        Assert.Equal(0, started.CompletedItems);

        var second = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/advance", null));
        Assert.Equal("B", second.Current?.PieceTitle);
        Assert.Equal("C", second.Next?.PieceTitle);
        Assert.Null(second.OnDeck);
        Assert.Equal(1, second.CompletedItems);

        // Yanlış basınca geri alınabilmeli.
        var back = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/back", null));
        Assert.Equal("A", back.Current?.PieceTitle);

        // Araya girip doğrudan bir sıraya atlamak (öğrenci hazır değilse).
        var last = back.OnDeck!.Id;
        var jumped = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/goto/{last}", null));
        Assert.Equal("C", jumped.Current?.PieceTitle);
        Assert.Null(jumped.Next);

        // Son sıradan ileri gidilemez - program bitti.
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PostAsync($"/api/shows/{fixture.ShowId}/advance", null)).StatusCode);

        var finished = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/finish", null));
        Assert.Equal(ShowEventStatus.Completed, finished.Status);
        Assert.Null(finished.Current);
    }

    [Fact]
    public async Task Stage_reports_all_pieces_of_the_student_currently_on_stage()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "pieces");
        var soloist = fixture.StudentIds[0];

        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            Performance(soloist, fixture.PianoId, fixture.TeacherId, "1. Bölüm", "Prelude"));
        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            Performance(soloist, fixture.PianoId, fixture.TeacherId, "1. Bölüm", "Fugue"));
        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            Performance(fixture.StudentIds[1], fixture.PianoId, fixture.TeacherId, "1. Bölüm", "Sonatin"));

        var stage = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/start", null));

        // Sahne ekranı "2 eserden 1.si" diyebilmeli.
        Assert.Equal(["Prelude", "Fugue"], stage.Current!.StudentPieces);
        Assert.Equal(0, stage.Current.StudentPieceIndex);

        var next = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/advance", null));
        Assert.Equal(["Prelude", "Fugue"], next.Current!.StudentPieces);
        Assert.Equal(1, next.Current.StudentPieceIndex);

        // Farklı öğrenciye geçince liste de değişir.
        var third = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/advance", null));
        Assert.Equal(["Sonatin"], third.Current!.StudentPieces);
    }

    [Fact]
    public async Task Deleting_the_item_that_is_on_stage_clears_the_pointer()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "pointer");

        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            Performance(fixture.StudentIds[0], fixture.PianoId, fixture.TeacherId, "1. Bölüm", "Tek eser"));

        var stage = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/start", null));
        var currentId = stage.Current!.Id;

        await admin.DeleteAsync($"/api/shows/{fixture.ShowId}/items/{currentId}");

        var after = await ReadAsync<ShowStage.StageResponse>(
            await admin.GetAsync($"/api/shows/{fixture.ShowId}/stage"));
        Assert.Null(after.Current);
        Assert.Equal(ShowEventStatus.Live, after.Status);
    }

    [Fact]
    public async Task Teacher_can_read_the_programme_but_cannot_change_it()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "role");
        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            Performance(fixture.StudentIds[0], fixture.PianoId, fixture.TeacherId, "1. Bölüm", "Eser"));

        using var teacher = await LoginAsync(fixture.TeacherEmail, fixture.TeacherPassword);

        // Kulisteki öğretmen sırayı görebilmeli - program operasyonel bir belge.
        var detail = await ReadAsync<Shows.ShowDetailResponse>(await teacher.GetAsync($"/api/shows/{fixture.ShowId}"));
        Assert.Single(detail.Items);
        Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync($"/api/shows/{fixture.ShowId}/stage")).StatusCode);

        // Ama değiştiremez.
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync(
            $"/api/shows/{fixture.ShowId}/items",
            Performance(fixture.StudentIds[1], fixture.PianoId, fixture.TeacherId, "1. Bölüm", "İzinsiz"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsync($"/api/shows/{fixture.ShowId}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.DeleteAsync($"/api/shows/{fixture.ShowId}")).StatusCode);
    }

    [Fact]
    public async Task Student_photo_round_trips_and_is_cached_by_version()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "photo");
        var studentId = fixture.StudentIds[0];

        // 1x1 saydam PNG.
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "portre.png");

        var uploaded = await ReadAsync<StudentPhotos.UploadResponse>(
            await admin.PutAsync($"/api/students/{studentId}/photo", form));
        Assert.False(string.IsNullOrWhiteSpace(uploaded.Version));

        var fetched = await admin.GetAsync($"/api/students/{studentId}/photo");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await fetched.Content.ReadAsByteArrayAsync());

        // ETag eşleşirse gövde tekrar inmemeli - sahne ekranı saniyede bir yenileniyor.
        var etag = fetched.Headers.ETag!.ToString();
        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/students/{studentId}/photo");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await admin.SendAsync(conditional)).StatusCode);

        // Program yanıtı fotoğrafın varlığını ve sürümünü bildirir, baytları taşımaz.
        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            Performance(studentId, fixture.PianoId, fixture.TeacherId, "1. Bölüm", "Eser"));
        var detail = await ReadAsync<Shows.ShowDetailResponse>(await admin.GetAsync($"/api/shows/{fixture.ShowId}"));
        Assert.True(detail.Items.Single().HasPhoto);
        Assert.Equal(uploaded.Version, detail.Items.Single().PhotoVersion);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/students/{studentId}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/students/{studentId}/photo")).StatusCode);
    }

    [Fact]
    public async Task Photo_upload_rejects_a_non_image_payload()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "badphoto");

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.4"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "belge.pdf");

        var response = await admin.PutAsync($"/api/students/{fixture.StudentIds[0]}/photo", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Intermission_rows_take_part_in_the_running_order_without_a_performer()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "ara");

        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            Performance(fixture.StudentIds[0], fixture.PianoId, fixture.TeacherId, "1. Bölüm", "İlk eser"));
        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            new Shows.ItemRequest(ShowItemKind.Intermission, "Ara", null, null, null, null, null, 15, "15 dakika ara"));
        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            Performance(fixture.StudentIds[1], fixture.PianoId, fixture.TeacherId, "2. Bölüm", "İkinci eser"));

        var stage = await ReadAsync<ShowStage.StageResponse>(
            await admin.PostAsync($"/api/shows/{fixture.ShowId}/start", null));

        Assert.Equal(ShowItemKind.Performance, stage.Current!.Kind);
        Assert.Equal(ShowItemKind.Intermission, stage.Next!.Kind);
        Assert.Null(stage.Next.StudentId);
        Assert.Equal("2. Bölüm", stage.OnDeck!.GroupName);
        Assert.Equal(23, stage.TotalDurationMinutes);
    }

    [Fact]
    public async Task Performance_row_without_a_student_or_piece_is_rejected()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "invalid");

        var missingStudent = await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            new Shows.ItemRequest(ShowItemKind.Performance, "1. Bölüm", null, null, null, "Eser", null, 4, null));
        Assert.Equal(HttpStatusCode.BadRequest, missingStudent.StatusCode);

        var missingPiece = await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            new Shows.ItemRequest(ShowItemKind.Performance, "1. Bölüm", fixture.StudentIds[0], null, null, "  ", null, 4, null));
        Assert.Equal(HttpStatusCode.BadRequest, missingPiece.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_show_removes_its_programme_but_keeps_the_students()
    {
        var admin = await CreateAdminClientAsync();
        var fixture = await SeedShowAsync(admin, "cascade");
        await admin.PostAsJsonAsync($"/api/shows/{fixture.ShowId}/items",
            Performance(fixture.StudentIds[0], fixture.PianoId, fixture.TeacherId, "1. Bölüm", "Eser"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/shows/{fixture.ShowId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/shows/{fixture.ShowId}")).StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.False(db.ShowItems.Any(item => item.ShowEventId == fixture.ShowId));
        Assert.True(db.Students.Any(student => student.Id == fixture.StudentIds[0]));
    }
}
