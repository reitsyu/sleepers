using System.Collections.ObjectModel;
using Sleepers.Core;

namespace Sleepers.Services;

/// <summary>Sequential job queue. Created on the UI thread so collection changes stay on it.</summary>
public sealed class QueueManager : ObservableObject
{
    readonly SynchronizationContext _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    Job? _current;
    EncodeRunner? _runner;
    CancellationTokenSource? _cts;

    public ObservableCollection<Job> Jobs { get; } = new();
    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }

    public int QueuedCount { get; private set; }
    public int RunningCount { get; private set; }
    public int DoneCount { get; private set; }
    public int FailedCount { get; private set; }
    public double OverallProgress { get; private set; }
    public string OverallText { get; private set; } = "Idle";

    public void Add(Job j)
    {
        j.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Job.Progress) or nameof(Job.Status)) _ui.Post(_ => RefreshSummary(), null);
        };
        Jobs.Add(j);
        RefreshSummary();
    }

    void RefreshSummary()
    {
        var jobs = Jobs.ToArray();
        QueuedCount = jobs.Count(j => j.Status == JobStatus.Queued);
        RunningCount = jobs.Count(j => j.Status is JobStatus.Running or JobStatus.Paused);
        DoneCount = jobs.Count(j => j.Status == JobStatus.Completed);
        FailedCount = jobs.Count(j => j.Status == JobStatus.Failed);
        var counted = jobs.Where(j => j.Status != JobStatus.Cancelled).ToArray();
        OverallProgress = counted.Length == 0 ? 0 : counted.Average(j => j.Status == JobStatus.Completed ? 100 : j.Progress);
        OverallText = jobs.Length == 0 ? "Queue is empty"
            : IsPaused ? "Paused"
            : RunningCount > 0 ? $"{OverallProgress:0}% overall — {DoneCount} of {counted.Length} done"
            : $"{DoneCount} done, {FailedCount} failed, {QueuedCount} waiting";
        foreach (var n in new[] { nameof(QueuedCount), nameof(RunningCount), nameof(DoneCount), nameof(FailedCount),
                                  nameof(OverallProgress), nameof(OverallText), nameof(IsRunning), nameof(IsPaused) })
            Raise(n);
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        IsRunning = true; RefreshSummary();
        try
        {
            while (Jobs.FirstOrDefault(j => j.Status == JobStatus.Queued) is { } next)
                await RunOne(next);
        }
        finally { IsRunning = false; IsPaused = false; _current = null; RefreshSummary(); }
    }

    async Task RunOne(Job job)
    {
        job.Reset();
        job.Status = JobStatus.Running;
        _cts = new CancellationTokenSource();
        _runner = new EncodeRunner();
        _current = job;
        try
        {
            var paths = AppServices.Paths ?? throw new MediaException(AppServices.FfmpegProblem ?? "FFmpeg was not found.");
            var hw = await AppServices.HardwareReady;
            await Task.Run(() => _runner.RunAsync(job, paths, hw, AppServices.Settings, _cts.Token));
            job.Progress = 100; job.Speed = 0; job.Eta = null;
            job.Status = JobStatus.Completed;
            if (job.OutputSize > 0 && job.OutputSize >= job.Source.FileSize && job.Settings.Kind == MediaKind.Video)
                job.Message = (job.Message == null ? "" : job.Message + " ") + "The output is not smaller than the original. Try a higher quality number or lower resolution.";
            Logger.Info($"Completed: {job.Name} -> {job.OutputPath}");
            AppServices.History.Add(HistoryEntry.From(job, true));
        }
        catch (OperationCanceledException)
        {
            job.Message = "Cancelled."; job.Status = JobStatus.Cancelled;
        }
        catch (Exception ex)
        {
            job.Message = ex is MediaException ? ex.Message : "Unexpected error: " + ex.Message;
            job.Status = JobStatus.Failed;
            Logger.Error($"Failed: {job.Name}", ex);
            AppServices.History.Add(HistoryEntry.From(job, false));
        }
        finally
        {
            job.Speed = 0; job.Eta = null;
            _cts.Dispose(); _cts = null; _runner = null; _current = null; IsPaused = false;
            RefreshSummary();
        }
    }

    public void Pause()
    {
        if (_current == null || _runner == null || IsPaused) return;
        _runner.Pause(); IsPaused = true; _current.Status = JobStatus.Paused; RefreshSummary();
    }

    public void Resume()
    {
        if (_current == null || _runner == null || !IsPaused) return;
        _runner.Resume(); IsPaused = false; _current.Status = JobStatus.Running; RefreshSummary();
    }

    public void Cancel(Job j)
    {
        if (j == _current) _cts?.Cancel();
        else if (j.Status == JobStatus.Queued) { j.Message = "Cancelled before start."; j.Status = JobStatus.Cancelled; }
    }

    public void CancelAll()
    {
        foreach (var j in Jobs.ToArray()) Cancel(j);
    }

    public void Retry(Job j)
    {
        if (!j.CanRetry) return;
        j.Reset();
        _ = StartAsync();
    }

    public void Remove(Job j)
    {
        if (j.Status is JobStatus.Running or JobStatus.Paused) return;
        Jobs.Remove(j); RefreshSummary();
    }

    public void ClearFinished()
    {
        foreach (var j in Jobs.Where(j => j.Status is JobStatus.Completed or JobStatus.Cancelled).ToArray()) Jobs.Remove(j);
        RefreshSummary();
    }

    /// <summary>App is closing: stop FFmpeg and delete partial output right now.</summary>
    public void Shutdown()
    {
        foreach (var j in Jobs.Where(j => j.Status == JobStatus.Queued)) j.Status = JobStatus.Cancelled;
        _runner?.Abort();
        _cts?.Cancel();
    }
}
