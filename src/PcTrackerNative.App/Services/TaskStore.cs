using System.Text.Json;
using PcTrackerNative.App.Models;

namespace PcTrackerNative.App.Services;

public sealed class TaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _taskFilePath;
    private readonly List<TaskItem> _tasks = new();
    private int _currentPendingIndex;

    public TaskStore(string taskFilePath)
    {
        _taskFilePath = taskFilePath;
        EnsureExists();
        Load();
    }

    public int PendingCount => _tasks.Count(task => !task.IsDone && !task.IsBad);

    public int CompletedCount => _tasks.Count(task => task.IsDone);

    public TaskItem? CurrentTask => GetPendingTasks().ElementAtOrDefault(_currentPendingIndex);

    public void Reload()
    {
        Load();
    }

    public void MoveNext()
    {
        var count = PendingCount;
        if (count == 0)
        {
            _currentPendingIndex = 0;
            return;
        }

        _currentPendingIndex = (_currentPendingIndex + 1) % count;
    }

    public void MovePrevious()
    {
        var count = PendingCount;
        if (count == 0)
        {
            _currentPendingIndex = 0;
            return;
        }

        _currentPendingIndex = (_currentPendingIndex - 1 + count) % count;
    }

    public void MarkCurrentBad()
    {
        var task = CurrentTask;
        if (task is null)
        {
            return;
        }

        task.IsBad = true;
        Save();
        NormalizeIndex();
    }

    public void MarkCurrentDone()
    {
        var task = CurrentTask;
        if (task is null)
        {
            return;
        }

        task.IsDone = true;
        Save();
        NormalizeIndex();
    }

    private void EnsureExists()
    {
        var directory = Path.GetDirectoryName(_taskFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(_taskFilePath))
        {
            return;
        }

        var defaults = new List<TaskItem>
        {
            new() { Category = "File Explorer", Description = "Open File Explorer and create a new folder.", IsDone = false, IsBad = false },
            new() { Category = "Notepad", Description = "Open Notepad and type a short process summary.", IsDone = false, IsBad = false },
        };

        File.WriteAllText(_taskFilePath, JsonSerializer.Serialize(defaults, JsonOptions));
    }

    private void Load()
    {
        _tasks.Clear();
        var json = File.ReadAllText(_taskFilePath);
        var items = JsonSerializer.Deserialize<List<TaskItem>>(json, JsonOptions) ?? new List<TaskItem>();
        _tasks.AddRange(items);
        NormalizeIndex();
    }

    private void Save()
    {
        File.WriteAllText(_taskFilePath, JsonSerializer.Serialize(_tasks, JsonOptions));
    }

    private void NormalizeIndex()
    {
        if (PendingCount <= 0)
        {
            _currentPendingIndex = 0;
            return;
        }

        if (_currentPendingIndex >= PendingCount)
        {
            _currentPendingIndex = PendingCount - 1;
        }

        if (_currentPendingIndex < 0)
        {
            _currentPendingIndex = 0;
        }
    }

    private List<TaskItem> GetPendingTasks()
    {
        return _tasks.Where(task => !task.IsDone && !task.IsBad).ToList();
    }
}
