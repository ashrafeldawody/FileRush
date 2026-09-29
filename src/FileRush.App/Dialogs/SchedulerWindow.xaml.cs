using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FileRush.App.Infrastructure;
using FileRush.Core.Models;
using FileRush.Core.Services;

namespace FileRush.App.Dialogs;

public partial class SchedulerWindow : Window
{
    private readonly DownloadManager _manager = App.Services.Manager;
    private DownloadQueue? _current;
    private bool _loading;

    public SchedulerWindow(DownloadQueue? initial = null)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        for (int i = 1; i <= 16; i++) ConcurrentBox.Items.Add(i.ToString());
        QueueList.ItemsSource = _manager.Queues;
        QueueList.SelectedItem = initial ?? _manager.Queues.FirstOrDefault();
    }

    private void QueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_current is not null && !_loading) ApplyToQueue(_current, false);
        _current = QueueList.SelectedItem as DownloadQueue;
        LoadQueue();
    }

    private void LoadQueue()
    {
        _loading = true;
        var q = _current;
        Tabs.IsEnabled = q is not null;
        if (q is null)
        {
            FilesList.ItemsSource = null;
            _loading = false;
            return;
        }
        FilesList.ItemsSource = _manager.ItemsInQueue(q);
        ConcurrentBox.SelectedIndex = Math.Clamp(q.MaxConcurrent, 1, 16) - 1;
        var s = q.Schedule;
        StartEnabledCheck.IsChecked = s.StartEnabled;
        StartTimeBox.Text = s.StartTime.ToString(@"hh\:mm");
        StopEnabledCheck.IsChecked = s.StopEnabled;
        StopTimeBox.Text = s.StopTime.ToString(@"hh\:mm");
        OnceRadio.IsChecked = s.Mode == ScheduleMode.Once;
        DailyRadio.IsChecked = s.Mode == ScheduleMode.Daily;
        OnceDatePicker.SelectedDate = s.OnceDate;
        MonCheck.IsChecked = s.Days.HasFlag(ScheduleDays.Monday);
        TueCheck.IsChecked = s.Days.HasFlag(ScheduleDays.Tuesday);
        WedCheck.IsChecked = s.Days.HasFlag(ScheduleDays.Wednesday);
        ThuCheck.IsChecked = s.Days.HasFlag(ScheduleDays.Thursday);
        FriCheck.IsChecked = s.Days.HasFlag(ScheduleDays.Friday);
        SatCheck.IsChecked = s.Days.HasFlag(ScheduleDays.Saturday);
        SunCheck.IsChecked = s.Days.HasFlag(ScheduleDays.Sunday);
        OnCompleteBox.SelectedIndex = (int)q.OnComplete;
        ForceCheck.IsChecked = q.ForceShutdown;
        StartNowButton.IsEnabled = !q.IsRunning;
        StopNowButton.IsEnabled = q.IsRunning;
        var next = QueueScheduler.NextStart(s, DateTime.Now);
        NextRunText.Text = next is null ? (s.StartEnabled ? "No upcoming start time." : "Automatic start is disabled.") : "Next scheduled start: " + next.Value.ToString("f");
        _loading = false;
    }

    private bool ApplyToQueue(DownloadQueue q, bool showErrors)
    {
        if (!TryParseTime(StartTimeBox.Text, out var start) || !TryParseTime(StopTimeBox.Text, out var stop))
        {
            if (showErrors) MessageBox.Show(this, "Enter times as hh:mm (24-hour).", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        q.MaxConcurrent = ConcurrentBox.SelectedIndex + 1;
        var s = q.Schedule;
        s.StartEnabled = StartEnabledCheck.IsChecked == true;
        s.StartTime = start;
        s.StopEnabled = StopEnabledCheck.IsChecked == true;
        s.StopTime = stop;
        s.Mode = OnceRadio.IsChecked == true ? ScheduleMode.Once : ScheduleMode.Daily;
        s.OnceDate = OnceDatePicker.SelectedDate ?? DateTime.Today;
        var days = ScheduleDays.None;
        if (MonCheck.IsChecked == true) days |= ScheduleDays.Monday;
        if (TueCheck.IsChecked == true) days |= ScheduleDays.Tuesday;
        if (WedCheck.IsChecked == true) days |= ScheduleDays.Wednesday;
        if (ThuCheck.IsChecked == true) days |= ScheduleDays.Thursday;
        if (FriCheck.IsChecked == true) days |= ScheduleDays.Friday;
        if (SatCheck.IsChecked == true) days |= ScheduleDays.Saturday;
        if (SunCheck.IsChecked == true) days |= ScheduleDays.Sunday;
        s.Days = days;
        q.OnComplete = (QueueCompletionAction)Math.Max(0, OnCompleteBox.SelectedIndex);
        q.ForceShutdown = ForceCheck.IsChecked == true;
        _manager.ScheduleSave();
        return true;
    }

    private static bool TryParseTime(string text, out TimeSpan time)
    {
        return TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);
    }

    private void NewQueue_Click(object sender, RoutedEventArgs e)
    {
        var name = InputWindow.Ask(this, "New queue", "Queue name:", "New queue");
        if (string.IsNullOrWhiteSpace(name)) return;
        var queue = _manager.CreateQueue(name);
        QueueList.SelectedItem = queue;
    }

    private void DeleteQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        if (_current.IsBuiltIn)
        {
            MessageBox.Show(this, "Built-in queues cannot be deleted.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Delete queue \"{_current.Name}\"? Its downloads stay in the list.", "File Rush", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var queue = _current;
        _current = null;
        _manager.DeleteQueue(queue);
        QueueList.SelectedItem = _manager.Queues.FirstOrDefault();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        MoveSelected(-1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        MoveSelected(1);
    }

    private void MoveSelected(int offset)
    {
        if (_current is null || FilesList.SelectedItem is not DownloadItem item) return;
        _manager.MoveInQueue(item, offset);
        FilesList.ItemsSource = _manager.ItemsInQueue(_current);
        FilesList.SelectedItem = item;
    }

    private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        foreach (var item in FilesList.SelectedItems.Cast<DownloadItem>().ToList()) _manager.RemoveFromQueue(item);
        FilesList.ItemsSource = _manager.ItemsInQueue(_current);
    }

    private void AddToQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var candidates = _manager.Items.Where(i => !i.IsCompleted && i.QueueId != _current.Id).ToList();
        if (candidates.Count == 0)
        {
            MessageBox.Show(this, "There are no unfinished downloads outside this queue.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var picker = new QueuePickWindow(candidates) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            foreach (var item in picker.Selected) _manager.AddToQueue(item, _current);
            FilesList.ItemsSource = _manager.ItemsInQueue(_current);
        }
    }

    private void StartNow_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || !ApplyToQueue(_current, true)) return;
        _manager.StartQueue(_current);
        LoadQueue();
    }

    private void StopNow_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        _manager.StopQueue(_current);
        LoadQueue();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null && ApplyToQueue(_current, true)) LoadQueue();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) ApplyToQueue(_current, false);
        Close();
    }
}
