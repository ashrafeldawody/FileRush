using System.IO;
using System.Windows.Threading;
using FileRush.Core.Models;
using FileRush.Core.Services;
using Microsoft.Win32;

namespace FileRush.App.Infrastructure;

public sealed class AppServices
{
    private SettingsStore _settingsStore = null!;

    public AppSettings Settings { get; private set; } = null!;

    public DownloadManager Manager { get; private set; } = null!;

    public void Initialize(Dispatcher dispatcher)
    {
        Directory.CreateDirectory(DataPaths.Root);
        _settingsStore = new SettingsStore(DataPaths.SettingsFile);
        Settings = _settingsStore.Load();
        Manager = new DownloadManager(Settings, new DownloadStore(DataPaths.DownloadsFile))
        {
            Dispatch = action =>
            {
                if (dispatcher.CheckAccess()) action();
                else dispatcher.Invoke(action);
            }
        };
        Manager.Load();
        ApplyStartupRegistration();
        ApplyBrowserRegistration();
        if (!File.Exists(DataPaths.SettingsFile))
        {
            try { _settingsStore.Save(Settings); } catch { }
        }
    }

    public void SaveSettings()
    {
        try
        {
            _settingsStore.Save(Settings);
        }
        catch
        {
        }
        Manager.ApplySettings(Settings);
        ApplyStartupRegistration();
        ApplyBrowserRegistration();
    }

    public void ApplyBrowserRegistration()
    {
        try
        {
            if (Settings.BrowserIntegration) BrowserIntegration.Register();
        }
        catch
        {
        }
    }

    public void ApplyStartupRegistration()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null) return;
            if (Settings.StartWithWindows)
            {
                var exe = Environment.ProcessPath ?? string.Empty;
                key.SetValue("FileRush", $"\"{exe}\" --minimized");
            }
            else
            {
                key.DeleteValue("FileRush", false);
            }
        }
        catch
        {
        }
    }

    private bool _shutdownCompleted;

    public async Task ShutdownAsync()
    {
        if (_shutdownCompleted) return;
        _shutdownCompleted = true;
        try
        {
            await Manager.ShutdownAsync();
        }
        catch
        {
        }
        SaveOnExit();
    }

    public void Shutdown()
    {
        if (_shutdownCompleted) return;
        _shutdownCompleted = true;
        try
        {
            Manager.StopAll();
            Manager.Save();
        }
        catch
        {
        }
        SaveOnExit();
    }

    private void SaveOnExit()
    {
        try
        {
            _settingsStore.Save(Settings);
        }
        catch
        {
        }
        Manager.Dispose();
    }
}
