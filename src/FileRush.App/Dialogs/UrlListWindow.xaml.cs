using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FileRush.App.Infrastructure;
using FileRush.Core.Models;
using FileRush.Core.Services;
using FileRush.Core.Utils;
using Microsoft.Win32;

namespace FileRush.App.Dialogs;

public sealed class UrlRow : INotifyPropertyChanged
{
    private bool _isChecked = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Url { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }
}

public partial class UrlListWindow : Window
{
    private readonly DownloadManager _manager = App.Services.Manager;
    private readonly List<UrlRow> _rows;
    private readonly string? _referrer;

    public UrlListWindow(string title, IEnumerable<string> urls, string? referrer = null)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        Title = title;
        _referrer = referrer;
        _rows = urls.Distinct(StringComparer.OrdinalIgnoreCase).Select(u => new UrlRow { Url = u, FileName = FileNameResolver.FromUrl(u) }).ToList();
        UrlList.ItemsSource = _rows;
        CountText.Text = $"{_rows.Count} link(s) found";
        foreach (var category in App.Services.Settings.Categories) CategoryBox.Items.Add(category.Name);
        CategoryBox.SelectedItem = "General";
        FolderBox.Text = _manager.DirectoryForCategory("General");
        AutoCategory_Changed(this, new RoutedEventArgs());
    }

    public int AddedCount { get; private set; }

    public bool Confirmed { get; private set; }

    public string? Cookie { get; set; }

    public string? UserAgent { get; set; }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsChecked = true;
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsChecked = false;
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryBox.SelectedItem is string category) FolderBox.Text = _manager.DirectoryForCategory(category);
    }

    private void AutoCategory_Changed(object sender, RoutedEventArgs e)
    {
        var manual = AutoCategoryCheck.IsChecked != true;
        CategoryBox.IsEnabled = manual;
        FolderBox.IsEnabled = manual;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = FolderBox.Text, Title = "Select the folder to save the files" };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void Now_Click(object sender, RoutedEventArgs e)
    {
        AddSelected(null, true);
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        AddSelected(_manager.MainQueue.Id, false);
    }

    private void AddSelected(Guid? queueId, bool start)
    {
        var selected = _rows.Where(r => r.IsChecked).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one link.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var auto = AutoCategoryCheck.IsChecked == true;
        var category = CategoryBox.SelectedItem as string;
        var folder = FolderBox.Text.Trim();
        foreach (var row in selected)
        {
            var request = new NewDownloadRequest
            {
                Url = row.Url,
                Referrer = _referrer,
                UserAgent = UserAgent,
                QueueId = queueId,
                Category = auto ? null : category,
                SaveDirectory = auto || string.IsNullOrWhiteSpace(folder) ? null : folder
            };
            if (!string.IsNullOrWhiteSpace(Cookie)) request.Headers["Cookie"] = Cookie;
            _manager.Add(request, start);
            AddedCount++;
        }
        Confirmed = true;
        CloseWithResult();
    }

    private void CloseWithResult()
    {
        try
        {
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }
}
