using Abdera.Api.Modules.Messaging.Domain;

namespace Abdera.Tests.Unit;

// Meta şablon değişkenleri konumsal ({{1}}, {{2}}...) ve sayısı birebir tutmalı. Ders mesajları
// lesson_time + new_lesson_time dahil altı değer üretiyordu; lesson_reminder_rsvp gövdesinde beş
// değişken var, gönderim Meta'da reddedilirdi. Değerler artık gövdedeki sıraya göre dizilir.
public class MessageTemplateTests
{
    private static readonly Dictionary<string, string> LessonValues = new()
    {
        ["guardian_name"] = "Ayşe",
        ["student_name"] = "Deniz Kaya",
        ["instrument"] = "Piyano",
        ["lesson_time"] = "29 Eylül 17:00",
        ["new_lesson_time"] = "29 Eylül 17:00",
        ["teacher_name"] = "Can Yılmaz",
    };

    [Fact]
    public void OrderParameters_follows_the_body_order_and_drops_values_the_body_does_not_use()
    {
        var template = MessageTemplate.Create(
            "lesson_reminder_rsvp",
            "Merhaba {{guardian_name}}, {{student_name}} öğrencimizin {{instrument}} dersi {{lesson_time}}. Öğretmen: {{teacher_name}}.");

        var ordered = template.OrderParameters(LessonValues, out var missing);

        Assert.Empty(missing);
        Assert.Equal(new[] { "guardian_name", "student_name", "instrument", "lesson_time", "teacher_name" }, ordered.Keys.ToArray());
        Assert.Equal(new[] { "Ayşe", "Deniz Kaya", "Piyano", "29 Eylül 17:00", "Can Yılmaz" }, ordered.Values.ToArray());
    }

    [Fact]
    public void OrderParameters_uses_first_occurrence_order_and_sends_repeated_names_once()
    {
        var template = MessageTemplate.Create(
            "custom",
            "{{teacher_name}} hoca, {{guardian_name}} ile görüşecek. Tekrar: {{teacher_name}}.");

        var ordered = template.OrderParameters(LessonValues, out _);

        Assert.Equal(new[] { "teacher_name", "guardian_name" }, ordered.Keys.ToArray());
    }

    [Fact]
    public void OrderParameters_reports_placeholders_the_notification_does_not_produce()
    {
        var template = MessageTemplate.Create("custom", "Merhaba {{guardian_name}}, tutar {{amount}}.");

        var ordered = template.OrderParameters(LessonValues, out var missing);

        Assert.Equal(new[] { "amount" }, missing.ToArray());
        Assert.Equal(new[] { "guardian_name" }, ordered.Keys.ToArray());
    }
}
