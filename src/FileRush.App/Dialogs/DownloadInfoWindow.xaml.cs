using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FileRush.App.Infrastructure;
using FileRush.Core.Models;
using FileRush.Core.Services;
using FileRush.Core.Utils;
using Microsoft.Win32;

namespace FileRush.App.Dialogs;

public partial class DownloadInfoWindow : Window
{
    private readonly NewDownloadRequest _request;
    private readonly DownloadManager _manager = App.Services.Manager;
    private readonly AppSettings _settings = App.Services.Settings;
    private readonly CancellationTokenSource _cts = new();
    private DownloadItem? _liveItem;
    private bool _userEditedPath;
    private bool _suppressPathEvents;
    private bool _finished;

    public DownloadInfoWindow(NewDownloadRequest request)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        _request = request;
        UrlBox.Text = request.Url;
        DescriptionBox.Text = request.Description;
        foreach (var known in _settings.Categories) CategoryBox.Items.Add(known.Name);
        var fileName = string.IsNullOrWhiteSpace(request.FileName) ? FileNameResolver.FromUrl(request.Url) : request.FileName;
        var category = request.Category ?? _manager.ResolveCategory(string.IsNullOrWhiteSpace(fileName) ? request.Url : fileName).Name;
        _suppressPathEvents = true;
        CategoryBox.SelectedItem = category;
        SetPath(Path.Combine(request.SaveDirectory ?? _manager.DirectoryForCategory(category), string.IsNullOrWhiteSpace(fileName) ? "download" : fileName));
        _suppressPathEvents = false;
        RememberPathCheck.IsChecked = _settings.RememberLastPathPerCategory;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_settings.StartDownloadWhileShowingInfoDialog)
        {
            _request.FileName = Path.GetFileName(SaveAsBox.Text);
            _request.SaveDirectory = Path.GetDirectoryName(SaveAsBox.Text);
            _request.Category = CategoryBox.SelectedItem as string;
            _liveItem = _manager.Add(_request, true);
            _liveItem.PropertyChanged += LiveItemChanged;
            RefreshFromItem();
        }
        else
        {
            _ = ProbeAsync();
        }
    }

    private void LiveItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DownloadItem.TotalSize) or nameof(DownloadItem.FileName) or nameof(DownloadItem.SupportsResume) or nameof(DownloadItem.Status))
        {
            Dispatcher.BeginInvoke(RefreshFromItem);
        }
    }

    private void RefreshFromItem()
    {
        if (_liveItem is null) return;
        SizeText.Text = _liveItem.TotalSize >= 0 ? ByteFormatter.Format(_liveItem.TotalSize) : (_liveItem.Status == DownloadStatus.Error ? "Error: " + _liveItem.ErrorMessage : "Getting file info...");
        ResumeText.Text = _liveItem.Status is DownloadStatus.Downloading or DownloadStatus.Completed ? (_liveItem.SupportsResume ? "Resume capability: Yes" : "Resume capability: No") : string.Empty;
        if (!_userEditedPath && !string.IsNullOrWhiteSpace(_liveItem.FileName))
        {
            var dir = Path.GetDirectoryName(SaveAsBox.Text) ?? _liveItem.SaveDirectory;
            _suppressPathEvents = true;
            SetPath(Path.Combine(dir, _liveItem.FileName));
            _suppressPathEvents = false;
        }
    }

    private async Task ProbeAsync()
    {
        try
        {
            var info = await _manager.ProbeAsync(_request.Url, _request.Username, _request.Password, _request.Referrer, _cts.Token);
            _request.KnownInfo = info;
            SizeText.Text = info.Size >= 0 ? ByteFormatter.Format(info.Size) : "Unknown";
            ResumeText.Text = info.SupportsRange ? "Resume capability: Yes" : "Resume capability: No";
            if (!_userEditedPath && !string.IsNullOrWhiteSpace(info.FileName))
            {
                var category = _manager.ResolveCategory(info.FileName).Name;
                _suppressPathEvents = true;
                if (_request.Category is null) CategoryBox.SelectedItem = category;
                var dir = Path.GetDirectoryName(SaveAsBox.Text) ?? _manager.DirectoryForCategory(category);
                if (_request.Category is null && _request.SaveDirectory is null) dir = _manager.DirectoryForCategory(category);
                SetPath(Path.Combine(dir, info.FileName));
                _suppressPathEvents = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SizeText.Text = "Unknown (" + ex.Message + ")";
        }
    }

    private void SetPath(string path)
    {
        SaveAsBox.Text = path;
    }

    private void SaveAsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressPathEvents) _userEditedPath = true;
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPathEvents || CategoryBox.SelectedItem is not string category) return;
        var name = Path.GetFileName(SaveAsBox.Text);
        _suppressPathEvents = true;
        SetPath(Path.Combine(_manager.DirectoryForCategory(category), name));
        _suppressPathEvents = false;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(SaveAsBox.Text),
            InitialDirectory = Path.GetDirectoryName(SaveAsBox.Text),
            Title = "Save As",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            SetPath(dialog.FileName);
        }
    }

    private (string Directory, string FileName)? ReadPath()
    {
        var path = SaveAsBox.Text.Trim();
        var dir = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Please enter a full path to save the file.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        return (dir, FileNameResolver.Sanitize(name));
    }

    private void RememberPath(string directory)
    {
        if (RememberPathCheck.IsChecked != true || CategoryBox.SelectedItem is not string categoryName) return;
        var category = CategoryResolver.Find(_settings.Categories, categoryName);
        if (category is null || string.Equals(category.SaveDirectory, directory, StringComparison.OrdinalIgnoreCase)) return;
        category.SaveDirectory = directory;
        App.Services.SaveSettings();
    }

    private void ApplyToItem(DownloadItem item, string directory, string fileName)
    {
        item.SaveDirectory = directory;
        item.FileName = fileName;
        item.Description = DescriptionBox.Text.Trim();
        if (CategoryBox.SelectedItem is string category) item.Category = category;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var path = ReadPath();
        if (path is null) return;
        RememberPath(path.Value.Directory);
        DownloadItem item;
        if (_liveItem is not null)
        {
            item = _liveItem;
            ApplyToItem(item, path.Value.Directory, path.Value.FileName);
            _manager.ScheduleSave();
            if (item.CanResume) _manager.Start(item);
        }
        else
        {
            _request.FileName = path.Value.FileName;
            _request.SaveDirectory = path.Value.Directory;
            _request.Category = CategoryBox.SelectedItem as string;
            _request.Description = DescriptionBox.Text.Trim();
            item = _manager.Add(_request, true);
        }
        _finished = true;
        App.Shell?.ShowProgressFor(item);
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        QueueMenu.Items.Clear();
        foreach (var queue in _manager.Queues)
        {
            var q = queue;
            var menuItem = new MenuItem { Header = "Add to \"" + q.Name + "\"" };
            menuItem.Click += (_, _) => AddLater(q);
            QueueMenu.Items.Add(menuItem);
        }
        QueueMenu.Items.Add(new Separator());
        var noQueue = new MenuItem { Header = "Add without a queue" };
        noQueue.Click += (_, _) => AddLater(null);
        QueueMenu.Items.Add(noQueue);
        QueueMenu.PlacementTarget = LaterButton;
        QueueMenu.IsOpen = true;
    }

    private async void AddLater(DownloadQueue? queue)
    {
        var path = ReadPath();
        if (path is null) return;
        RememberPath(path.Value.Directory);
        if (_liveItem is not null)
        {
            await _manager.PauseAsync(_liveItem);
            ApplyToItem(_liveItem, path.Value.Directory, path.Value.FileName);
            if (queue is not null) _manager.AddToQueue(_liveItem, queue);
            _liveItem.StatusText = string.Empty;
            _manager.ScheduleSave();
        }
        else
        {
            _request.FileName = path.Value.FileName;
            _request.SaveDirectory = path.Value.Directory;
            _request.Category = CategoryBox.SelectedItem as string;
            _request.Description = DescriptionBox.Text.Trim();
            _request.QueueId = queue?.Id;
            _manager.Add(_request, false);
        }
        _finished = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        _cts.Cancel();
        if (_liveItem is not null)
        {
            _liveItem.PropertyChanged -= LiveItemChanged;
            if (!_finished)
            {
                var item = _liveItem;
                _liveItem = null;
                await _manager.RemoveAsync(item, false);
            }
        }
    }
}
