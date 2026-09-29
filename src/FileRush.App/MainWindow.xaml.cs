using System.ComponentModel;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using FileRush.App.Dialogs;
using FileRush.App.Infrastructure;
using FileRush.App.ViewModels;
using FileRush.Core.Models;
using FileRush.Core.Services;
using FileRush.Core.Utils;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace FileRush.App;

public partial class MainWindow : Window
{
    private readonly DownloadManager _manager;
    private readonly MainViewModel _vm;
    private readonly Dictionary<Guid, ProgressWindow> _progressWindows = new();
    private readonly Dictionary<GridViewColumn, double> _columnWidths = new();
    private readonly DispatcherTimer _statusTimer;
    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _trayLimiterItem;
    private ClipboardMonitor? _clipboard;
    private bool _exiting;
    private bool _shutdownStarted;
    private bool _shutdownCompleted;

    private static AppSettings Settings => App.Services.Settings;

    public MainWindow()
    {
        InitializeComponent();
        AppIcons.Apply(this);
        _manager = App.Services.Manager;
        _vm = new MainViewModel(_manager);
        DataContext = _vm;
        Width = Math.Max(MinWidth, Settings.WindowWidth);
        Height = Math.Max(MinHeight, Settings.WindowHeight);
        if (!string.IsNullOrEmpty(Settings.SortColumn)) _vm.SortBy(Settings.SortColumn, Settings.SortDescending);

        _manager.DownloadCompleted += OnDownloadCompleted;
        _manager.DownloadFailed += OnDownloadFailed;
        _manager.QueueFinished += OnQueueFinished;
        _manager.StateChanged += (_, _) => UpdateButtonStates();
        _manager.Queues.CollectionChanged += (_, _) => BuildQueueMenus();

        BuildQueueMenus();
        BuildColumnsMenu();
        InitTray();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) =>
        {
            UpdateStatusBar();
            UpdateButtonStates();
        };
        _statusTimer.Start();

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += OnWindowStateChanged;
        SourceInitialized += (_, _) => InitClipboardMonitor();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyViewSettings();
        UpdateButtonStates();
        UpdateStatusBar();
    }

    public void ShowInTrayOnly()
    {
        WindowState = WindowState.Minimized;
        ShowInTaskbar = false;
    }

    public void HandleExternalMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message == "show")
        {
            RestoreFromTray();
            return;
        }
        var browserMessage = BrowserMessage.Parse(message);
        if (browserMessage is not null)
        {
            HandleBrowserMessage(browserMessage);
            return;
        }
        if (UrlUtils.IsDownloadableUrl(UrlUtils.NormalizeUrl(message), out var uri))
        {
            OfferDownload(new NewDownloadRequest { Url = uri.AbsoluteUri });
        }
    }

    private void HandleBrowserMessage(BrowserMessage message)
    {
        var urls = message.AllUrls();
        switch (message.Type)
        {
            case "download":
                if (urls.Count == 0) return;
                if (urls.Count == 1) OfferDownload(message.ToRequest(urls[0]));
                else ShowBatch(message, urls);
                break;
            case "batch":
                if (urls.Count == 0) return;
                ShowBatch(message, urls);
                break;
            case "show":
                RestoreFromTray();
                break;
        }
    }

    private void ShowBatch(BrowserMessage message, IReadOnlyList<string> urls)
    {
        var window = new UrlListWindow("Download links from the browser", urls, message.Referrer)
        {
            Cookie = message.Cookie,
            UserAgent = message.UserAgent,
            Topmost = true
        };
        window.Show();
        window.Activate();
    }

    public void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void OfferDownload(NewDownloadRequest request)
    {
        if (Settings.ShowStartDownloadDialog)
        {
            var window = new DownloadInfoWindow(request);
            window.Show();
            window.Activate();
            return;
        }
        var item = _manager.Add(request, true);
        ShowProgressFor(item);
    }

    public void ShowProgressFor(DownloadItem item)
    {
        if (!Settings.ShowProgressDialog) return;
        if (_progressWindows.TryGetValue(item.Id, out var existing))
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var window = new ProgressWindow(item);
        window.Closed += (_, _) => _progressWindows.Remove(item.Id);
        _progressWindows[item.Id] = window;
        window.Show();
    }

    private List<DownloadItem> SelectedItems => DownloadList.SelectedItems.Cast<DownloadItem>().ToList();

    private DownloadItem? SelectedItem => DownloadList.SelectedItem as DownloadItem;

    private DownloadQueue TargetQueue => _vm.SelectedNode is { Kind: NodeKind.Queue, Queue: { } q } ? q : _manager.MainQueue;

    private void InitTray()
    {
        _tray = new WinForms.NotifyIcon { Icon = AppIcons.TrayIcon, Text = "File Rush", Visible = true };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show File Rush", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Add URL...", null, (_, _) =>
        {
            RestoreFromTray();
            AddUrl_Click(this, new RoutedEventArgs());
        });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Start main queue", null, (_, _) => _manager.StartQueue(_manager.MainQueue));
        menu.Items.Add("Stop all downloads", null, (_, _) => _manager.StopAll());
        _trayLimiterItem = new WinForms.ToolStripMenuItem("Speed limiter") { CheckOnClick = true, Checked = Settings.SpeedLimit.Enabled };
        _trayLimiterItem.Click += (_, _) => SetSpeedLimiter(_trayLimiterItem.Checked);
        menu.Items.Add(_trayLimiterItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void InitClipboardMonitor()
    {
        _clipboard = new ClipboardMonitor(this, OnClipboardText);
        if (!Settings.MonitorClipboard) _clipboard.Stop();
    }

    private void OnClipboardText(string text)
    {
        if (!ClipboardUrlDetector.ShouldCapture(text, Settings, out var uri)) return;
        if (_manager.Items.Any(i => i.Url.Equals(uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase) && !i.IsCompleted)) return;
        OfferDownload(new NewDownloadRequest { Url = uri.AbsoluteUri });
    }

    private void ApplyViewSettings()
    {
        CategoriesPaneMenu.IsChecked = Settings.ShowCategoriesPane;
        LargeIconsMenu.IsChecked = Settings.LargeToolbarIcons;
        TreeColumn.Width = Settings.ShowCategoriesPane ? new GridLength(210) : new GridLength(0);
        CategoryTree.Visibility = Settings.ShowCategoriesPane ? Visibility.Visible : Visibility.Collapsed;
        ApplyToolbarSize();
        if (_clipboard is not null)
        {
            if (Settings.MonitorClipboard) _clipboard.Start();
            else _clipboard.Stop();
        }
        StatusClipboardText.Text = Settings.MonitorClipboard ? "Clipboard monitoring: on" : "Clipboard monitoring: off";
    }

    private void ApplyToolbarSize()
    {
        var large = Settings.LargeToolbarIcons;
        foreach (var child in Toolbar.Children)
        {
            if (child is not Button { Content: StackPanel panel } button) continue;
            panel.Orientation = large ? Orientation.Vertical : Orientation.Horizontal;
            foreach (var element in panel.Children)
            {
                if (element is TextBlock block && block.Style == FindResource("ToolbarIcon"))
                {
                    block.FontSize = large ? 26 : 16;
                    block.Margin = large ? new Thickness(0, 0, 0, 3) : new Thickness(0, 0, 6, 0);
                }
            }
            button.MinWidth = large ? 62 : 0;
            button.Padding = large ? new Thickness(8, 4, 8, 4) : new Thickness(6, 3, 6, 3);
        }
    }

    private void UpdateStatusBar()
    {
        var items = _manager.Items;
        var active = items.Count(i => i.IsActive);
        StatusCountText.Text = $"{items.Count} download(s), {active} active, {items.Count(i => i.IsCompleted)} completed";
        var speed = _manager.TotalSpeed();
        StatusSpeedText.Text = speed > 0 ? "Total speed: " + ByteFormatter.FormatSpeed(speed) : "Idle";
        StatusLimiterText.Text = Settings.SpeedLimit.Enabled ? $"Speed limiter: {Settings.SpeedLimit.MaxKilobytesPerSecond} KB/s" : "Speed limiter: off";
        if (_tray is not null)
        {
            var text = active > 0 ? $"File Rush - {active} active, {ByteFormatter.FormatSpeed(speed)}" : "File Rush";
            _tray.Text = text.Length > 63 ? text[..63] : text;
        }
        if (_trayLimiterItem is not null) _trayLimiterItem.Checked = Settings.SpeedLimit.Enabled;
    }

    private void UpdateButtonStates()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateButtonStates);
            return;
        }
        var selected = SelectedItems;
        ResumeButton.IsEnabled = selected.Any(i => i.CanResume);
        StopButton.IsEnabled = selected.Any(i => i.IsActive);
        StopAllButton.IsEnabled = _manager.Items.Any(i => i.IsActive);
        DeleteButton.IsEnabled = selected.Count > 0;
        DeleteCompletedButton.IsEnabled = _manager.Items.Any(i => i.IsCompleted);
        var queue = TargetQueue;
        StartQueueButton.IsEnabled = !queue.IsRunning;
        StopQueueButton.IsEnabled = queue.IsRunning;
        StartQueueButton.ToolTip = "Start \"" + queue.Name + "\"";
        StopQueueButton.ToolTip = "Stop \"" + queue.Name + "\"";
        SpeedLimiterOnMenu.IsChecked = Settings.SpeedLimit.Enabled;
        SpeedLimiterOffMenu.IsChecked = !Settings.SpeedLimit.Enabled;
        CheckShutdownWhenDone();
    }

    private void CheckShutdownWhenDone()
    {
        if (!ShutdownWhenDoneMenu.IsChecked) return;
        if (_manager.Items.Any(i => i.IsActive)) return;
        if (!_manager.Items.Any(i => i.IsCompleted)) return;
        ShutdownWhenDoneMenu.IsChecked = false;
        ShellHelper.Shutdown(false);
    }

    private void BuildQueueMenus()
    {
        StartQueueMenu.Items.Clear();
        StopQueueMenu.Items.Clear();
        MoveToQueueMenu.Items.Clear();
        foreach (var queue in _manager.Queues)
        {
            var q = queue;
            var start = new MenuItem { Header = q.Name };
            start.Click += (_, _) => _manager.StartQueue(q);
            StartQueueMenu.Items.Add(start);
            var stop = new MenuItem { Header = q.Name };
            stop.Click += (_, _) => _manager.StopQueue(q);
            StopQueueMenu.Items.Add(stop);
            var move = new MenuItem { Header = q.Name };
            move.Click += (_, _) =>
            {
                foreach (var item in SelectedItems) _manager.AddToQueue(item, q);
            };
            MoveToQueueMenu.Items.Add(move);
        }
        MoveToQueueMenu.Items.Add(new Separator());
        var newQueue = new MenuItem { Header = "New queue..." };
        newQueue.Click += (_, _) =>
        {
            var name = InputWindow.Ask(this, "New queue", "Queue name:", "New queue");
            if (string.IsNullOrWhiteSpace(name)) return;
            var q = _manager.CreateQueue(name);
            foreach (var item in SelectedItems) _manager.AddToQueue(item, q);
        };
        MoveToQueueMenu.Items.Add(newQueue);
    }

    private void BuildColumnsMenu()
    {
        ColumnsMenu.Items.Clear();
        foreach (var column in DownloadGrid.Columns)
        {
            var col = column;
            _columnWidths[col] = col.Width;
            var item = new MenuItem { Header = col.Header?.ToString(), IsCheckable = true, IsChecked = true };
            item.Click += (_, _) =>
            {
                if (item.IsChecked)
                {
                    col.Width = _columnWidths[col] > 0 ? _columnWidths[col] : 100;
                }
                else
                {
                    if (col.Width > 0) _columnWidths[col] = col.Width;
                    col.Width = 0;
                }
            };
            ColumnsMenu.Items.Add(item);
        }
    }

    private void OnDownloadCompleted(object? sender, DownloadItem item)
    {
        VirusScanner.TryRun(item, Settings.VirusScan);
        if (Settings.PlaySounds) SystemSounds.Asterisk.Play();
        if (_progressWindows.TryGetValue(item.Id, out var window)) window.Close();
        if (ProgressWindow.PostActions.TryRemove(item.Id, out var action))
        {
            switch (action)
            {
                case PostDownloadAction.OpenFile:
                    ShellHelper.OpenFile(item.FullPath);
                    break;
                case PostDownloadAction.OpenFolder:
                    ShellHelper.OpenFolder(item.FullPath);
                    break;
                case PostDownloadAction.Exit:
                    ExitApp();
                    return;
                case PostDownloadAction.Shutdown:
                    ShellHelper.Shutdown(false);
                    break;
            }
        }
        if (Settings.ShowCompleteDialog)
        {
            new DownloadCompleteWindow(item).Show();
        }
        else
        {
            _tray?.ShowBalloonTip(3000, "Download complete", item.FileName, WinForms.ToolTipIcon.Info);
        }
        UpdateButtonStates();
    }

    private void OnDownloadFailed(object? sender, DownloadItem item)
    {
        _tray?.ShowBalloonTip(4000, "Download error", item.FileName + ": " + item.ErrorMessage, WinForms.ToolTipIcon.Error);
        UpdateButtonStates();
    }

    private void OnQueueFinished(object? sender, DownloadQueue queue)
    {
        UpdateButtonStates();
        if (queue.StartedAt is not { } started) return;
        var didWork = _manager.Items.Any(i => i.QueueId == queue.Id && i.CompletedAt >= started);
        if (!didWork) return;
        switch (queue.OnComplete)
        {
            case QueueCompletionAction.ExitApplication:
                ExitApp();
                break;
            case QueueCompletionAction.TurnOffComputer:
                ShellHelper.Shutdown(queue.ForceShutdown);
                break;
            case QueueCompletionAction.Hibernate:
                ShellHelper.Hibernate();
                break;
            case QueueCompletionAction.Standby:
                ShellHelper.Standby();
                break;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && Settings.MinimizeToTray)
        {
            Hide();
            ShowInTaskbar = false;
        }
        else if (WindowState != WindowState.Minimized)
        {
            ShowInTaskbar = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        Settings.WindowWidth = Width;
        Settings.WindowHeight = Height;
        Settings.SortColumn = _vm.SortProperty;
        Settings.SortDescending = _vm.SortDescending;
        if (!_exiting && Settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            return;
        }
        _exiting = true;
        if (!_shutdownCompleted)
        {
            e.Cancel = true;
            _ = FinishShutdownAsync();
            return;
        }
        _statusTimer.Stop();
        _clipboard?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        foreach (var window in _progressWindows.Values.ToList()) window.Close();
        Application.Current.Shutdown();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private async Task FinishShutdownAsync()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        IsEnabled = false;
        _statusTimer.Stop();
        await Task.Yield();
        try
        {
            await App.Services.ShutdownAsync();
        }
        catch
        {
        }
        _shutdownCompleted = true;
        await Dispatcher.BeginInvoke(Close, DispatcherPriority.Normal);
    }

    private void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddUrlWindow { Owner = IsVisible ? this : null };
        if (dialog.ShowDialog() != true) return;
        OfferDownload(new NewDownloadRequest { Url = dialog.Url, Username = dialog.Username, Password = dialog.Password });
    }

    private void BatchFromClipboard_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch
        {
            text = string.Empty;
        }
        var urls = UrlUtils.ExtractUrls(text).ToList();
        if (urls.Count == 0)
        {
            MessageBox.Show(this, "The clipboard does not contain any links.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new UrlListWindow("Add batch download from clipboard", urls) { Owner = this }.ShowDialog();
    }

    private void BatchWildcards_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BatchDownloadWindow { Owner = this };
        if (dialog.ShowDialog() != true) return;
        new UrlListWindow("Add batch download with wildcards", dialog.Urls) { Owner = this }.ShowDialog();
    }

    private void Grabber_Click(object sender, RoutedEventArgs e)
    {
        new GrabberWindow { Owner = this }.Show();
    }

    private void ImportText_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", Title = "Import list of URLs" };
        if (dialog.ShowDialog(this) != true) return;
        var urls = ImportExport.FromUrlList(File.ReadAllText(dialog.FileName));
        if (urls.Count == 0)
        {
            MessageBox.Show(this, "No links were found in the file.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        new UrlListWindow("Import downloads", urls) { Owner = this }.ShowDialog();
    }

    private void ImportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "File Rush export (*.json)|*.json|All files (*.*)|*.*", Title = "Import from File Rush export file" };
        if (dialog.ShowDialog(this) != true) return;
        var entries = ImportExport.Import(File.ReadAllText(dialog.FileName));
        var added = 0;
        foreach (var entry in entries)
        {
            if (!UrlUtils.IsDownloadableUrl(UrlUtils.NormalizeUrl(entry.Url), out _)) continue;
            _manager.Add(new NewDownloadRequest
            {
                Url = entry.Url,
                FileName = string.IsNullOrWhiteSpace(entry.FileName) ? null : entry.FileName,
                SaveDirectory = string.IsNullOrWhiteSpace(entry.SaveDirectory) ? null : entry.SaveDirectory,
                Category = string.IsNullOrWhiteSpace(entry.Category) ? null : entry.Category,
                Description = entry.Description,
                Referrer = entry.Referrer,
                Username = entry.Username,
                Password = entry.Password
            }, false);
            added++;
        }
        MessageBox.Show(this, $"{added} download(s) imported.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private IReadOnlyList<DownloadItem> ItemsForExport()
    {
        var selected = SelectedItems;
        return selected.Count > 0 ? selected : _manager.Items.ToList();
    }

    private void ExportText_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Text files (*.txt)|*.txt", FileName = "downloads.txt", Title = "Export list of URLs" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, ImportExport.ToUrlList(ItemsForExport()));
    }

    private void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "File Rush export (*.json)|*.json", FileName = "downloads.json", Title = "Export to File Rush export file" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, ImportExport.ToJson(ItemsForExport()));
    }

    private async void DeleteCompleted_Click(object sender, RoutedEventArgs e)
    {
        var completed = _manager.Items.Where(i => i.IsCompleted).ToList();
        if (completed.Count == 0) return;
        if (Settings.ConfirmDelete)
        {
            var confirm = new DeleteConfirmWindow($"Remove {completed.Count} completed download(s) from the list?", true) { Owner = this };
            if (confirm.ShowDialog() != true) return;
            foreach (var item in completed) await _manager.RemoveAsync(item, confirm.DeleteFile);
            return;
        }
        foreach (var item in completed) await _manager.RemoveAsync(item, false);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ExitApp();
    }

    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in SelectedItems.Where(i => i.CanResume))
        {
            _manager.Start(item);
            ShowProgressFor(item);
        }
        UpdateButtonStates();
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in SelectedItems.Where(i => i.IsActive)) _manager.Pause(item);
        UpdateButtonStates();
    }

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        _manager.StopAll();
        UpdateButtonStates();
    }

    private void ResumeAll_Click(object sender, RoutedEventArgs e)
    {
        _manager.ResumeAll();
        UpdateButtonStates();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedItem;
        if (item is null) return;
        if (item.IsCompleted) ShellHelper.OpenFile(item.FullPath);
        else ShowProgressFor(item);
    }

    private void OpenWith_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is { IsCompleted: true } item) ShellHelper.OpenWith(item.FullPath);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item) ShellHelper.OpenFolder(item.IsCompleted ? item.FullPath : item.SaveDirectory);
    }

    private void ShowProgress_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item)
        {
            var window = _progressWindows.GetValueOrDefault(item.Id) ?? new ProgressWindow(item);
            if (!_progressWindows.ContainsKey(item.Id))
            {
                window.Closed += (_, _) => _progressWindows.Remove(item.Id);
                _progressWindows[item.Id] = window;
            }
            window.Show();
            window.Activate();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems;
        if (items.Count == 0) return;
        var deleteFile = false;
        if (Settings.ConfirmDelete)
        {
            var message = items.Count == 1 ? $"Are you sure you want to delete \"{items[0].FileName}\" from the list?" : $"Are you sure you want to delete {items.Count} downloads from the list?";
            var confirm = new DeleteConfirmWindow(message, items.Any(i => i.IsCompleted)) { Owner = this };
            if (confirm.ShowDialog() != true) return;
            deleteFile = confirm.DeleteFile;
        }
        foreach (var item in items)
        {
            if (_progressWindows.TryGetValue(item.Id, out var window)) window.Close();
            await _manager.RemoveAsync(item, deleteFile);
        }
        UpdateButtonStates();
    }

    private void DeleteFromQueue_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in SelectedItems) _manager.RemoveFromQueue(item);
    }

    private void CopyUrl_Click(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems;
        if (items.Count == 0) return;
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, items.Select(i => i.Url)));
        }
        catch
        {
        }
    }

    private async void MoveTo_Click(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems.Where(i => !_manager.IsRunning(i)).ToList();
        if (items.Count == 0) return;
        var dialog = new OpenFolderDialog { Title = "Move the selected downloads to", InitialDirectory = items[0].SaveDirectory };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var item in items)
        {
            try
            {
                await _manager.MoveAsync(item, dialog.FolderName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{item.FileName}: {ex.Message}", "File Rush", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void RefreshAddress_Click(object sender, RoutedEventArgs e)
    {
        var item = SelectedItem;
        if (item is null) return;
        if (_manager.IsRunning(item))
        {
            MessageBox.Show(this, "Stop the download before changing its address.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var url = InputWindow.Ask(this, "Refresh download address", "New address for \"" + item.FileName + "\":", item.Url);
        if (string.IsNullOrWhiteSpace(url)) return;
        var normalized = UrlUtils.NormalizeUrl(url);
        if (!UrlUtils.IsDownloadableUrl(normalized, out _))
        {
            MessageBox.Show(this, "The address is not a valid URL.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _manager.UpdateUrl(item, normalized);
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is { } item) new PropertiesWindow(item) { Owner = this }.ShowDialog();
    }

    private void Options_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OptionsWindow { Owner = this };
        dialog.ShowDialog();
        ApplyViewSettings();
        _vm.BuildTree();
        UpdateButtonStates();
        UpdateStatusBar();
    }

    private void Scheduler_Click(object sender, RoutedEventArgs e)
    {
        new SchedulerWindow(TargetQueue) { Owner = this }.ShowDialog();
        _vm.BuildTree();
        UpdateButtonStates();
    }

    private void StartQueue_Click(object sender, RoutedEventArgs e)
    {
        _manager.StartQueue(TargetQueue);
        UpdateButtonStates();
    }

    private void StopQueue_Click(object sender, RoutedEventArgs e)
    {
        _manager.StopQueue(TargetQueue);
        UpdateButtonStates();
    }

    private void SetSpeedLimiter(bool enabled)
    {
        _manager.SetSpeedLimit(enabled, Settings.SpeedLimit.MaxKilobytesPerSecond);
        App.Services.SaveSettings();
        UpdateStatusBar();
        UpdateButtonStates();
    }

    private void SpeedLimiterOn_Click(object sender, RoutedEventArgs e)
    {
        SetSpeedLimiter(true);
    }

    private void SpeedLimiterOff_Click(object sender, RoutedEventArgs e)
    {
        SetSpeedLimiter(false);
    }

    private void SpeedLimiterSettings_Click(object sender, RoutedEventArgs e)
    {
        new SpeedLimiterWindow { Owner = this }.ShowDialog();
        UpdateStatusBar();
        UpdateButtonStates();
    }

    private void CategoriesPane_Click(object sender, RoutedEventArgs e)
    {
        Settings.ShowCategoriesPane = CategoriesPaneMenu.IsChecked;
        App.Services.SaveSettings();
        ApplyViewSettings();
    }

    private void LargeIcons_Click(object sender, RoutedEventArgs e)
    {
        Settings.LargeToolbarIcons = LargeIconsMenu.IsChecked;
        App.Services.SaveSettings();
        ApplyViewSettings();
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Add downloads with the Add URL button, by copying a link to the clipboard, or by running FileRush.exe <url>.\n\n" +
            "Downloads are split into segments that are downloaded in parallel and re-split dynamically. Paused or broken downloads resume from the saved position when the server supports it.\n\n" +
            "Use the Scheduler to build queues that start and stop at set times, and the Options dialog to configure categories, connections, proxy and site logins.",
            "File Rush help", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        if (new CategoryWindow(null) { Owner = this }.ShowDialog() == true) _vm.BuildTree();
    }

    private void EditCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNode is not { Kind: NodeKind.Category, Category: { } category }) return;
        if (new CategoryWindow(category) { Owner = this }.ShowDialog() == true) _vm.BuildTree();
    }

    private void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNode is not { Kind: NodeKind.Category, Category: { } category }) return;
        if (category.IsBuiltIn)
        {
            MessageBox.Show(this, "Built-in categories cannot be deleted.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Delete category \"{category.Name}\"? Its downloads are moved to General.", "File Rush", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Settings.Categories.Remove(category);
        foreach (var item in _manager.Items.Where(i => i.Category.Equals(category.Name, StringComparison.OrdinalIgnoreCase))) item.Category = "General";
        App.Services.SaveSettings();
        _manager.ScheduleSave();
        _vm.BuildTree();
    }

    private void AddQueue_Click(object sender, RoutedEventArgs e)
    {
        var name = InputWindow.Ask(this, "New queue", "Queue name:", "New queue");
        if (!string.IsNullOrWhiteSpace(name)) _manager.CreateQueue(name);
    }

    private void StartSelectedQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNode is { Kind: NodeKind.Queue, Queue: { } queue }) _manager.StartQueue(queue);
    }

    private void StopSelectedQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNode is { Kind: NodeKind.Queue, Queue: { } queue }) _manager.StopQueue(queue);
    }

    private void DeleteQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNode is not { Kind: NodeKind.Queue, Queue: { } queue }) return;
        if (queue.IsBuiltIn)
        {
            MessageBox.Show(this, "Built-in queues cannot be deleted.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Delete queue \"{queue.Name}\"?", "File Rush", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) _manager.DeleteQueue(queue);
    }

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is CategoryNode node) _vm.SelectedNode = node;
        UpdateButtonStates();
    }

    private void DownloadList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = SelectedItem;
        if (item is null) return;
        if (item.IsCompleted)
        {
            ShellHelper.OpenFile(item.FullPath);
            return;
        }
        if (item.CanResume) _manager.Start(item);
        ShowProgressFor(item);
    }

    private void DownloadList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtonStates();
    }

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column }) return;
        var property = column.Header?.ToString() switch
        {
            "File Name" => nameof(DownloadItem.FileName),
            "Q" => nameof(DownloadItem.IsInQueue),
            "Size" => nameof(DownloadItem.TotalSize),
            "Status" => nameof(DownloadItem.Status),
            "Time left" => nameof(DownloadItem.TimeLeft),
            "Transfer rate" => nameof(DownloadItem.Speed),
            "Last Try Date" => nameof(DownloadItem.LastTryAt),
            "Description" => nameof(DownloadItem.Description),
            _ => null
        };
        if (property is not null) _vm.SortBy(property);
    }
}
