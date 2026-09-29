using System.Diagnostics;
using System.IO;
using System.Windows;

namespace FileRush.App.Infrastructure;

public static class ShellHelper
{
    public static void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show("The file does not exist:\n" + path, "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "File Rush", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void OpenWith(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show("The file does not exist:\n" + path, "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "File Rush", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }
            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (directory is not null && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "File Rush", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    public static void Shutdown(bool force)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 30" + (force ? " /f" : string.Empty)) { UseShellExecute = true, CreateNoWindow = true });
        }
        catch
        {
        }
    }

    public static void Hibernate()
    {
        try
        {
            System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Hibernate, false, false);
        }
        catch
        {
        }
    }

    public static void Standby()
    {
        try
        {
            System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false);
        }
        catch
        {
        }
    }
}
