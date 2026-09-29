using System.IO;
using System.Windows;
using FileRush.App.Infrastructure;
using FileRush.Core.Models;
using FileRush.Core.Services;
using FileRush.Core.Utils;
using Microsoft.Win32;

namespace FileRush.App.Dialogs;

public partial class PropertiesWindow : Window
{
    private readonly DownloadItem _item;
    private readonly DownloadManager _manager = App.Services.Manager;

    public PropertiesWindow(DownloadItem item)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        _item = item;
        FileNameBox.Text = item.FileName;
        FolderBox.Text = item.SaveDirectory;
        UrlBox.Text = item.Url;
        ReferrerBox.Text = item.Referrer ?? string.Empty;
        DescriptionBox.Text = item.Description;
        foreach (var category in App.Services.Settings.Categories) CategoryBox.Items.Add(category.Name);
        CategoryBox.SelectedItem = item.Category;
        ConnectionsBox.Items.Add("Default");
        for (int i = 1; i <= 32; i++) ConnectionsBox.Items.Add(i.ToString());
        ConnectionsBox.SelectedIndex = Math.Clamp(item.MaxConnections, 0, 32);
        if (!string.IsNullOrEmpty(item.Username))
        {
            AuthCheck.IsChecked = true;
            LoginBox.Text = item.Username;
            PasswordBox.Password = item.Password ?? string.Empty;
        }
        var running = _manager.IsRunning(item) || item.Status == DownloadStatus.Queued;
        var completed = item.Status == DownloadStatus.Completed;
        UrlBox.IsEnabled = !running && !completed;
        ReferrerBox.IsEnabled = !running && !completed;
        FileNameBox.IsEnabled = !running;
        FolderBox.IsEnabled = !running;
        AuthCheck.IsEnabled = !running && !completed;
        ConnectionsBox.IsEnabled = !running && !completed;
        InfoText.Text = BuildInfo(item, running);
    }

    private static string BuildInfo(DownloadItem item, bool running)
    {
        var parts = new List<string>
        {
            "Status: " + (string.IsNullOrEmpty(item.StatusText) ? item.Status.ToString() : item.StatusText),
            "Size: " + (item.TotalSize >= 0 ? ByteFormatter.Format(item.TotalSize) : "Unknown"),
            "Downloaded: " + ByteFormatter.Format(item.DownloadedBytes),
            "Resume: " + (item.SupportsResume ? "Yes" : "No"),
            "Added: " + item.AddedAt.ToString("g")
        };
        if (item.CompletedAt is { } done) parts.Add("Completed: " + done.ToString("g"));
        if (running) parts.Add("The download is in progress; stop it to change the address or file name.");
        return string.Join("   |   ", parts);
    }

    private void AuthCheck_Changed(object sender, RoutedEventArgs e)
    {
        AuthPanel.IsEnabled = AuthCheck.IsChecked == true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = FolderBox.Text, Title = "Select the folder to save the file" };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = FileNameResolver.Sanitize(FileNameBox.Text.Trim());
        var folder = FolderBox.Text.Trim();
        if (folder.Length == 0 || name.Length == 0)
        {
            MessageBox.Show(this, "File name and folder are required.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (UrlBox.IsEnabled)
        {
            var url = UrlUtils.NormalizeUrl(UrlBox.Text);
            if (!UrlUtils.IsDownloadableUrl(url, out _))
            {
                MessageBox.Show(this, "The address is not a valid URL.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (url != _item.Url) _manager.UpdateUrl(_item, url);
            _item.Referrer = string.IsNullOrWhiteSpace(ReferrerBox.Text) ? null : ReferrerBox.Text.Trim();
            if (AuthCheck.IsChecked == true)
            {
                _item.Username = LoginBox.Text;
                _item.Password = PasswordBox.Password;
            }
            else
            {
                _item.Username = null;
                _item.Password = null;
            }
            _item.MaxConnections = Math.Max(0, ConnectionsBox.SelectedIndex);
        }
        _item.Description = DescriptionBox.Text.Trim();
        if (CategoryBox.SelectedItem is string category) _item.Category = category;
        if (!string.Equals(folder, _item.SaveDirectory, StringComparison.OrdinalIgnoreCase) || name != _item.FileName)
        {
            try
            {
                await _manager.MoveAsync(_item, folder, name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "File Rush", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        _manager.ScheduleSave();
        DialogResult = true;
    }
}
