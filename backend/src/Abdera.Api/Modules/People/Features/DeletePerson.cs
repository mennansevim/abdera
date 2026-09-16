using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Infrastructure;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// Öğrenci ve öğretmenin KALICI olarak silinmesi.
//
// Projenin geri kalanında mali/devamsızlık geçmişi olan bir kayıt silinmez, durumu
// değiştirilir (CLAUDE.md). Bu uçlar o kuralın bilinçli istisnası: kullanıcının açık
// talebi - "öğretmen gitti diyelim neden pasife alıyorsun? tamamen silme opsiyonu olmalı."
//
// İstisna şu üç korumayla dengeleniyor:
//   1. ÖNCE NE OLACAĞINI SÖYLER. `/deletion-impact` kaç ders, kaç aidat, kaç ödeme
//      silineceğini işlemden önce verir; arayüz bunu onay ekranında gösterir.
//   2. PARA VARSA DURUR. Üzerinde kaydedilmiş ödeme varsa 409 döner; devam etmek için
//      isteğin açıkça `force=true` demesi gerekir. Karar kullanıcının, ama bilerek verilir.
//   3. ÖĞRETMENDE DEVİR YOLU VAR. `reassignTo` ile öğrenciler başka bir öğretmene
//      geçirilir; böylece öğretmen silinirken öğrencilerin mali geçmişine hiç dokunulmaz.
//      Gerçek hayatta olan da budur.
//
// Silme işleminin kendisi `audit_log`'a yazılır ve audit kayıtları asla temizlenmez.
public static class DeletePerson
{
    public static void MapDeletePerson(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization(AuthorizationPolicies.AdminOnly);

        group.MapGet("/students/{studentId:guid}/deletion-impact", StudentImpactAsync);
        group.MapDelete("/students/{studentId:guid}", DeleteStudentAsync);

        group.MapGet("/teachers/{teacherId:guid}/deletion-impact", TeacherImpactAsync);
        group.MapDelete("/teachers/{teacherId:guid}", DeleteTeacherAsync);
    }

    private static async Task<IResult> StudentImpactAsync(Guid studentId, AbderaDbContext db) =>
        Results.Ok(await PersonEraser.DescribeStudentAsync(studentId, db));

    private static async Task<IResult> TeacherImpactAsync(Guid teacherId, AbderaDbContext db) =>
        Results.Ok(await PersonEraser.DescribeTeacherAsync(teacherId, db));

    private static async Task<IResult> DeleteStudentAsync(
        Guid studentId, bool? force, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var impact = await PersonEraser.DescribeStudentAsync(studentId, db);

        if (impact.Payments > 0 && force != true)
            throw new ConflictException(
                $"{impact.StudentName} adına kaydedilmiş {impact.Payments} ödeme var " +
                $"(toplam {impact.CollectedAmount:0.##} {impact.Currency}). Silmek bu tahsilat geçmişini de " +
                "kalıcı olarak kaldırır. Devam etmek için onaylamanız gerekiyor.");

        var now = clock.UtcNow;
        // Audit kaydı silmeden ÖNCE yazılır: aynı transaction içinde olduğu için ya ikisi
        // birden olur ya hiçbiri, ve silinen kaydın ayrıntısı hâlâ elimizdeyken yazılır.
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "student.deleted", nameof(Student), studentId, now,
            beforeJson: JsonSerializer.Serialize(impact)));

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await PersonEraser.EraseStudentAsync(studentId, db);
        await transaction.CommitAsync();

        return Results.Ok(impact);
    }

    private static async Task<IResult> DeleteTeacherAsync(
        Guid teacherId, bool? force, Guid? reassignTo, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var impact = await PersonEraser.DescribeTeacherAsync(teacherId, db);

        if (reassignTo is { } newTeacherId)
        {
            if (newTeacherId == teacherId)
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["reassignTo"] = ["Öğrenciler silinecek öğretmenin kendisine devredilemez."],
                });

            var target = await db.Teachers.SingleOrDefaultAsync(item => item.Id == newTeacherId)
                ?? throw new NotFoundException("Devredilecek öğretmen bulunamadı.");
            if (target.Status != TeacherStatus.Active)
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["reassignTo"] = ["Devredilecek öğretmen aktif değil."],
                });
        }
        // Devir yoksa öğrencilerin dersleri ve aidatları da silinecek demektir - para
        // varsa açık onay şart.
        else if (impact.Payments > 0 && force != true)
        {
            throw new ConflictException(
                $"{impact.TeacherName} öğretmeninin {impact.AffectedStudents} öğrencisine ait " +
                $"{impact.Payments} ödeme kaydı var (toplam {impact.CollectedAmount:0.##} {impact.Currency}). " +
                "Öğrencileri başka bir öğretmene devrederek bu geçmişi koruyabilirsiniz; " +
                "yine de silmek isterseniz onaylamanız gerekiyor.");
        }

        var now = clock.UtcNow;
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "teacher.deleted", nameof(Teacher), teacherId, now,
            beforeJson: JsonSerializer.Serialize(impact),
            afterJson: reassignTo is null ? null : JsonSerializer.Serialize(new { reassignedTo = reassignTo })));

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        await PersonEraser.EraseTeacherAsync(teacherId, reassignTo, db);
        await transaction.CommitAsync();

        return Results.Ok(impact);
    }
}
