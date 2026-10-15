using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;

namespace Abdera.Tests.Unit;

// #31: arka plan işi sessizce durursa sistem sağlığı Degraded olur. Saf karar fonksiyonu,
// SystemHealthMonitor.Evaluate ile aynı desen (DB'ye dokunmaz).
public class BackgroundJobHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongAgo = Now.AddDays(-30);

    private static List<JobHeartbeat> AllSucceededAt(DateTimeOffset at) =>
        BackgroundJobs.All.Select(job => JobHeartbeat.Create(job.Name, at, null, null, at)).ToList();

    [Fact]
    public void All_jobs_recently_succeeded_is_healthy()
    {
        var (level, detail) = BackgroundJobs.Evaluate(AllSucceededAt(Now.AddSeconds(-30)), Now, LongAgo);

        Assert.Equal(SystemHealthLevel.Healthy, level);
        Assert.Null(detail);
    }

    [Fact]
    public void Monthly_receivables_silent_for_three_days_is_degraded_with_its_label()
    {
        var beats = AllSucceededAt(Now.AddSeconds(-30));
        beats.RemoveAll(b => b.JobName == BackgroundJobs.MonthlyReceivables.Name);
        beats.Add(JobHeartbeat.Create(BackgroundJobs.MonthlyReceivables.Name, Now.AddDays(-3).AddMinutes(-1), null, null, Now));

        var (level, detail) = BackgroundJobs.Evaluate(beats, Now, LongAgo);

        Assert.Equal(SystemHealthLevel.Degraded, level);
        Assert.Contains("Aylık aidat üretimi", detail);
        Assert.Contains("3 gün", detail);
    }

    [Fact]
    public void One_missed_run_is_tolerated()
    {
        var beats = AllSucceededAt(Now.AddSeconds(-30));
        beats.RemoveAll(b => b.JobName == BackgroundJobs.LessonNoteReminder.Name);
        beats.Add(JobHeartbeat.Create(BackgroundJobs.LessonNoteReminder.Name, Now.AddHours(-7), null, null, Now));

        Assert.Equal(SystemHealthLevel.Healthy, BackgroundJobs.Evaluate(beats, Now, LongAgo).Level);
    }

    [Fact]
    public void Failure_after_last_success_is_degraded_even_on_a_fresh_process()
    {
        var beats = AllSucceededAt(Now.AddMinutes(-5));
        beats.RemoveAll(b => b.JobName == BackgroundJobs.OverdueReceivables.Name);
        beats.Add(JobHeartbeat.Create(BackgroundJobs.OverdueReceivables.Name, Now.AddHours(-2), Now.AddMinutes(-1), "NpgsqlException: bağlantı koptu", Now));

        var (level, detail) = BackgroundJobs.Evaluate(beats, Now, runningSince: Now.AddMinutes(-2));

        Assert.Equal(SystemHealthLevel.Degraded, level);
        Assert.Contains("bağlantı koptu", detail);
    }

    [Fact]
    public void Recovered_job_after_an_old_failure_is_healthy()
    {
        var beats = AllSucceededAt(Now.AddSeconds(-30));
        beats.RemoveAll(b => b.JobName == BackgroundJobs.AttendanceReminder.Name);
        beats.Add(JobHeartbeat.Create(BackgroundJobs.AttendanceReminder.Name, Now.AddMinutes(-1), Now.AddHours(-1), "eski hata", Now));

        Assert.Equal(SystemHealthLevel.Healthy, BackgroundJobs.Evaluate(beats, Now, LongAgo).Level);
    }

    [Fact]
    public void Jobs_that_never_ran_are_not_flagged_while_the_process_is_young()
    {
        // Yoklama hatırlatması ilk turunu açılıştan 10 dk, ders yorumu 3 saat sonra atar.
        var (level, _) = BackgroundJobs.Evaluate([], Now, runningSince: Now.AddMinutes(-2));

        Assert.Equal(SystemHealthLevel.Healthy, level);
    }

    [Fact]
    public void Jobs_that_never_ran_on_a_long_running_process_are_flagged()
    {
        var (level, detail) = BackgroundJobs.Evaluate([], Now, LongAgo);

        Assert.Equal(SystemHealthLevel.Degraded, level);
        Assert.Contains("hiç başarıyla çalışmadı", detail);
    }

    [Fact]
    public void Combine_keeps_the_heavier_level_and_both_details()
    {
        var combined = SystemHealthMonitor.Combine(
            (SystemHealthLevel.Unhealthy, "Son yedekleme başarısız oldu."),
            (SystemHealthLevel.Degraded, "Aylık aidat üretimi hiç başarıyla çalışmadı."));

        Assert.Equal(SystemHealthLevel.Unhealthy, combined.Level);
        Assert.Contains("yedekleme", combined.Detail);
        Assert.Contains("Aylık aidat", combined.Detail);
    }

    [Fact]
    public void Combine_with_healthy_jobs_returns_backup_result_unchanged()
    {
        var backup = (SystemHealthLevel.Healthy, "Sistem sağlıklı.");

        Assert.Equal(backup, SystemHealthMonitor.Combine(backup, (SystemHealthLevel.Healthy, null)));
    }

    [Fact]
    public void Long_error_is_trimmed_to_the_column_size()
    {
        Assert.Equal(JobHeartbeat.MaxErrorLength, JobHeartbeat.TrimError(new string('x', 2000))!.Length);
    }
}
