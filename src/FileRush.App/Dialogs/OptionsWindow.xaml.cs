using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using FileRush.App.Infrastructure;
using FileRush.Core.Models;
using Microsoft.Win32;

namespace FileRush.App.Dialogs;

public partial class OptionsWindow : Window
{
    private readonly AppSettings _settings = App.Services.Settings;
    private readonly ObservableCollection<Category> _categories;
    private readonly ObservableCollection<SiteLogin> _logins;
    private bool _loading = true;

    public OptionsWindow(int initialTab = 0)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        _categories = new ObservableCollection<Category>(_settings.Categories);
        _logins = new ObservableCollection<SiteLogin>(_settings.SiteLogins);
        CategoriesGrid.ItemsSource = _categories;
        LoginsGrid.ItemsSource = _logins;
        for (int i = 1; i <= 32; i++) ConnectionsBox.Items.Add(i.ToString());
        for (int i = 1; i <= 32; i++) SimultaneousBox.Items.Add(i.ToString());
        LoadValues();
        Tabs.SelectedIndex = Math.Clamp(initialTab, 0, Tabs.Items.Count - 1);
        _loading = false;
    }

    public bool CategoriesChanged { get; private set; }

    private void LoadValues()
    {
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
        CloseToTrayCheck.IsChecked = _settings.CloseToTray;
        ConfirmDeleteCheck.IsChecked = _settings.ConfirmDelete;
        MonitorClipboardCheck.IsChecked = _settings.MonitorClipboard;
        BrowserIntegrationCheck.IsChecked = _settings.BrowserIntegration;
        UpdateBrowserStatus();
        ExtensionsBox.Text = _settings.MonitoredExtensions;
        ExceptionsBox.Text = string.Join(Environment.NewLine, _settings.UrlExceptions);
        RememberPathCheck.IsChecked = _settings.RememberLastPathPerCategory;
        TempDirBox.Text = _settings.TempDirectory;
        ConnectionsBox.SelectedIndex = Math.Clamp(_settings.MaxConnectionsPerFile, 1, 32) - 1;
        ConnectionTypeBox.SelectedIndex = _settings.MaxConnectionsPerFile switch { 2 => 0, 8 => 1, 16 => 2, _ => 3 };
        TimeoutBox.Text = _settings.TimeoutSeconds.ToString();
        RetriesBox.Text = _settings.MaxRetries.ToString();
        RetryDelayBox.Text = _settings.RetryDelaySeconds.ToString();
        MinSegmentBox.Text = _settings.MinSegmentSizeKb.ToString();
        SimultaneousBox.SelectedIndex = Math.Clamp(_settings.MaxSimultaneousDownloads, 1, 32) - 1;
        ShowInfoDialogCheck.IsChecked = _settings.ShowStartDownloadDialog;
        StartWhileInfoCheck.IsChecked = _settings.StartDownloadWhileShowingInfoDialog;
        ShowProgressCheck.IsChecked = _settings.ShowProgressDialog;
        ShowCompleteCheck.IsChecked = _settings.ShowCompleteDialog;
        IgnoreModTimeCheck.IsChecked = _settings.IgnoreModificationTimeOnResume;
        UserAgentBox.Text = _settings.UserAgent;
        VirusEnabledCheck.IsChecked = _settings.VirusScan.Enabled;
        VirusCommandBox.Text = _settings.VirusScan.Command;
        VirusArgsBox.Text = _settings.VirusScan.Arguments;
        VirusExeOnlyCheck.IsChecked = _settings.VirusScan.ScanOnlyExecutables;
        ProxyNoneRadio.IsChecked = _settings.Proxy.Mode == ProxyMode.None;
        ProxySystemRadio.IsChecked = _settings.Proxy.Mode == ProxyMode.System;
        ProxyManualRadio.IsChecked = _settings.Proxy.Mode == ProxyMode.Manual;
        ProxyTypeBox.SelectedIndex = (int)_settings.Proxy.Kind;
        ProxyAddressBox.Text = _settings.Proxy.Address;
        ProxyPortBox.Text = _settings.Proxy.Port.ToString();
        ProxyUserBox.Text = _settings.Proxy.Username;
        ProxyPasswordBox.Password = _settings.Proxy.Password;
        ProxyBypassBox.Text = _settings.Proxy.Bypass;
        FtpPassiveCheck.IsChecked = _settings.Proxy.UseFtpPassive;
        SpeedEnabledCheck.IsChecked = _settings.SpeedLimit.Enabled;
        SpeedBox.Text = _settings.SpeedLimit.MaxKilobytesPerSecond.ToString();
        SoundsCheck.IsChecked = _settings.PlaySounds;
    }

    private bool SaveValues()
    {
        if (!int.TryParse(TimeoutBox.Text.Trim(), out var timeout) || timeout < 5 ||
            !int.TryParse(RetriesBox.Text.Trim(), out var retries) || retries < 0 ||
            !int.TryParse(RetryDelayBox.Text.Trim(), out var retryDelay) || retryDelay < 1 ||
            !int.TryParse(MinSegmentBox.Text.Trim(), out var minSegment) || minSegment < 64 ||
            !int.TryParse(SpeedBox.Text.Trim(), out var speed) || speed < 1 ||
            !int.TryParse(ProxyPortBox.Text.Trim(), out var port) || port < 0 || port > 65535)
        {
            MessageBox.Show(this, "Please check the numeric values: timeout (>= 5), retries (>= 0), retry delay (>= 1), minimum segment size (>= 64 KB), speed limit (>= 1) and proxy port.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        _settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        _settings.ConfirmDelete = ConfirmDeleteCheck.IsChecked == true;
        _settings.MonitorClipboard = MonitorClipboardCheck.IsChecked == true;
        _settings.BrowserIntegration = BrowserIntegrationCheck.IsChecked == true;
        _settings.MonitoredExtensions = ExtensionsBox.Text.Trim();
        _settings.UrlExceptions = ExceptionsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        _settings.RememberLastPathPerCategory = RememberPathCheck.IsChecked == true;
        _settings.TempDirectory = TempDirBox.Text.Trim();
        _settings.MaxConnectionsPerFile = ConnectionsBox.SelectedIndex + 1;
        _settings.TimeoutSeconds = timeout;
        _settings.MaxRetries = retries;
        _settings.RetryDelaySeconds = retryDelay;
        _settings.MinSegmentSizeKb = minSegment;
        _settings.MaxSimultaneousDownloads = SimultaneousBox.SelectedIndex + 1;
        _settings.ShowStartDownloadDialog = ShowInfoDialogCheck.IsChecked == true;
        _settings.StartDownloadWhileShowingInfoDialog = StartWhileInfoCheck.IsChecked == true;
        _settings.ShowProgressDialog = ShowProgressCheck.IsChecked == true;
        _settings.ShowCompleteDialog = ShowCompleteCheck.IsChecked == true;
        _settings.IgnoreModificationTimeOnResume = IgnoreModTimeCheck.IsChecked == true;
        _settings.UserAgent = UserAgentBox.Text.Trim();
        _settings.VirusScan.Enabled = VirusEnabledCheck.IsChecked == true;
        _settings.VirusScan.Command = VirusCommandBox.Text.Trim();
        _settings.VirusScan.Arguments = VirusArgsBox.Text.Trim();
        _settings.VirusScan.ScanOnlyExecutables = VirusExeOnlyCheck.IsChecked == true;
        _settings.Proxy.Mode = ProxyManualRadio.IsChecked == true ? ProxyMode.Manual : ProxySystemRadio.IsChecked == true ? ProxyMode.System : ProxyMode.None;
        _settings.Proxy.Kind = (ProxyKind)Math.Max(0, ProxyTypeBox.SelectedIndex);
        _settings.Proxy.Address = ProxyAddressBox.Text.Trim();
        _settings.Proxy.Port = port;
        _settings.Proxy.Username = ProxyUserBox.Text.Trim();
        _settings.Proxy.Password = ProxyPasswordBox.Password;
        _settings.Proxy.Bypass = ProxyBypassBox.Text.Trim();
        _settings.Proxy.UseFtpPassive = FtpPassiveCheck.IsChecked == true;
        _settings.SpeedLimit.Enabled = SpeedEnabledCheck.IsChecked == true;
        _settings.SpeedLimit.MaxKilobytesPerSecond = speed;
        _settings.PlaySounds = SoundsCheck.IsChecked == true;
        LoginsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        CategoriesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        _settings.SiteLogins = _logins.Where(l => !string.IsNullOrWhiteSpace(l.Host)).ToList();
        _settings.Categories = _categories.ToList();
        App.Services.SaveSettings();
        return true;
    }

    private void UpdateBrowserStatus()
    {
        var status = BrowserIntegration.Status();
        var registered = status.Where(s => s.Registered).Select(s => s.DisplayName).ToList();
        var text = registered.Count == 0
            ? "Native messaging host is not registered for any browser."
            : "Native messaging host registered for: " + string.Join(", ", registered) + ".";
        BrowserStatusText.Text = text + Environment.NewLine + "Extension folder: " + BrowserIntegration.ExtensionDirectory + Environment.NewLine + "Extension ID: " + Core.Services.BrowserIntegrationInfo.ChromeExtensionId;
    }

    private void RegisterBrowsers_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BrowserIntegration.Register();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "File Rush", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        UpdateBrowserStatus();
    }

    private void UnregisterBrowsers_Click(object sender, RoutedEventArgs e)
    {
        BrowserIntegration.Unregister();
        BrowserIntegrationCheck.IsChecked = false;
        UpdateBrowserStatus();
    }

    private void OpenExtensionFolder_Click(object sender, RoutedEventArgs e)
    {
        ShellHelper.OpenFolder(BrowserIntegration.ExtensionDirectory);
    }

    private void ConnectionType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var connections = ConnectionTypeBox.SelectedIndex switch { 0 => 2, 1 => 8, 2 => 16, _ => 0 };
        if (connections > 0) ConnectionsBox.SelectedIndex = connections - 1;
    }

    private void BrowseCategoryFolder_Click(object sender, RoutedEventArgs e)
    {
        if (CategoriesGrid.SelectedItem is not Category category) return;
        var dialog = new OpenFolderDialog { InitialDirectory = category.SaveDirectory, Title = "Select the folder for " + category.Name };
        if (dialog.ShowDialog(this) == true)
        {
            category.SaveDirectory = dialog.FolderName;
            CategoriesGrid.Items.Refresh();
            CategoriesChanged = true;
        }
    }

    private void EditCategory_Click(object sender, RoutedEventArgs e)
    {
        if (CategoriesGrid.SelectedItem is not Category category) return;
        var dialog = new CategoryWindow(category) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            CategoriesGrid.Items.Refresh();
            CategoriesChanged = true;
        }
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CategoryWindow(null) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _categories.Add(dialog.Category);
            CategoriesChanged = true;
        }
    }

    private void RemoveCategory_Click(object sender, RoutedEventArgs e)
    {
        if (CategoriesGrid.SelectedItem is not Category category) return;
        if (category.IsBuiltIn)
        {
            MessageBox.Show(this, "Built-in categories cannot be removed.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _categories.Remove(category);
        CategoriesChanged = true;
    }

    private void BrowseTemp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = TempDirBox.Text, Title = "Select the temporary directory" };
        if (dialog.ShowDialog(this) == true) TempDirBox.Text = dialog.FolderName;
    }

    private void BrowseScanner_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*", Title = "Select the virus scanner" };
        if (dialog.ShowDialog(this) == true) VirusCommandBox.Text = dialog.FileName;
    }

    private void NewLogin_Click(object sender, RoutedEventArgs e)
    {
        var login = new SiteLogin();
        _logins.Add(login);
        LoginsGrid.SelectedItem = login;
        LoginsGrid.ScrollIntoView(login);
        LoginsGrid.CurrentCell = new DataGridCellInfo(login, LoginsGrid.Columns[0]);
        LoginsGrid.BeginEdit();
    }

    private void DeleteLogin_Click(object sender, RoutedEventArgs e)
    {
        if (LoginsGrid.SelectedItem is SiteLogin login) _logins.Remove(login);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SaveValues();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SaveValues()) DialogResult = true;
    }
}
