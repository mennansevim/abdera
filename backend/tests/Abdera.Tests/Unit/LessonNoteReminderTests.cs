using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Progress.Infrastructure;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

// Yorum bekleyen ders hatırlatması: öğretmen başına tek, tazelenen bildirim satırı ve metni.
// Hangi dersin "yorum bekliyor" sayıldığı veritabanı sorgusu (PendingLessonNotes) -
// LessonNoteReminderFlowTests'te gerçek Postgres üzerinde doğrulanıyor.
public class LessonNoteReminderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 17, 0, 0, TimeSpan.Zero);

    private sealed class IstanbulClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
        public TimeZoneInfo SchoolTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    }

    private static StaffNotification CreateReminder() => StaffNotification.Create(
        Guid.NewGuid(), StaffNotificationType.LessonNoteMissing, "2 dersin yorumu bekliyor", "eski metin",
        LessonNoteReminderJob.ReferenceType, Guid.NewGuid(), Now.AddDays(-1));

    private static PendingLessonNotes.PendingLessonNoteResponse Pending(string studentName, DateTimeOffset startAt) =>
        new(Guid.NewGuid(), startAt, startAt.AddMinutes(45), Guid.NewGuid(), studentName, Guid.NewGuid(), "Piyano");

    [Fact]
    public void RefreshReminder_without_resurface_updates_text_but_keeps_it_read()
    {
        var reminder = CreateReminder();
        reminder.MarkRead(Now.AddHours(-2));

        reminder.RefreshReminder("1 dersin yorumu bekliyor", "yeni metin", Now, resurface: false);

        Assert.Equal("1 dersin yorumu bekliyor", reminder.Title);
        Assert.Equal("yeni metin", reminder.Body);
        Assert.Equal(Now.AddHours(-2), reminder.ReadAt);
        Assert.Equal(Now.AddDays(-1), reminder.CreatedAt);
        Assert.Equal(Now, reminder.UpdatedAt);
    }

    [Fact]
    public void RefreshReminder_with_resurface_makes_it_unread_and_newest_again()
    {
        var reminder = CreateReminder();
        reminder.MarkRead(Now.AddHours(-2));

        reminder.RefreshReminder("2 dersin yorumu bekliyor", "yeni metin", Now, resurface: true);

        Assert.Null(reminder.ReadAt);
        Assert.Equal(Now, reminder.CreatedAt);
        Assert.Equal(Now, reminder.UpdatedAt);
    }

    [Fact]
    public void RefreshReminder_rejects_an_empty_body()
    {
        var reminder = CreateReminder();

        Assert.Throws<ArgumentException>(() => reminder.RefreshReminder("başlık", " ", Now, resurface: true));
    }

    [Fact]
    public void Title_counts_pending_lessons()
    {
        Assert.Equal("1 dersin yorumu bekliyor", LessonNoteReminderJob.Title(1));
        Assert.Equal("4 dersin yorumu bekliyor", LessonNoteReminderJob.Title(4));
    }

    [Fact]
    public void Body_names_the_three_oldest_lessons_in_school_time_and_summarises_the_rest()
    {
        var pending = new[]
        {
            // 21:30 UTC = ertesi gün 00:30 İstanbul - tarih okulun yerel gününe göre yazılmalı.
            Pending("Ada Yılmaz", new DateTimeOffset(2026, 9, 21, 21, 30, 0, TimeSpan.Zero)),
            Pending("Can Demir", new DateTimeOffset(2026, 9, 24, 14, 0, 0, TimeSpan.Zero)),
            Pending("Ece Kaya", new DateTimeOffset(2026, 9, 25, 14, 0, 0, TimeSpan.Zero)),
            Pending("Mert Şahin", new DateTimeOffset(2026, 9, 26, 14, 0, 0, TimeSpan.Zero)),
            Pending("Zeynep Öz", new DateTimeOffset(2026, 9, 27, 14, 0, 0, TimeSpan.Zero)),
        };

        var body = LessonNoteReminderJob.Body(pending, new IstanbulClock());

        Assert.Contains("Ada Yılmaz (22 Eylül), Can Demir (24 Eylül), Ece Kaya (25 Eylül) ve 2 ders daha", body);
        Assert.DoesNotContain("Mert Şahin", body);
        Assert.Contains("Gelişim ekranındaki \"Yorum bekleyen dersler\"", body);
    }

    [Fact]
    public void Body_falls_back_to_a_count_when_names_would_overflow_the_column()
    {
        var longName = new string('A', 100) + " " + new string('B', 100);
        var pending = Enumerable.Range(0, 3).Select(day => Pending(longName, Now.AddDays(-day - 1))).ToArray();

        var body = LessonNoteReminderJob.Body(pending, new IstanbulClock());

        Assert.True(body.Length <= 500);
        Assert.StartsWith("Tamamlanan 3 dersin notunu girmeyi unutma.", body);
    }
}
