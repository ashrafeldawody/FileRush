using System.Windows;
using FileRush.App.Infrastructure;
using FileRush.Core.Models;
using FileRush.Core.Utils;

namespace FileRush.App.Dialogs;

public partial class DownloadCompleteWindow : Window
{
    private readonly DownloadItem _item;

    public DownloadCompleteWindow(DownloadItem item)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        _item = item;
        FileIcon.Source = FileIconProvider.GetIcon(item.FileName);
        FileNameText.Text = item.FileName;
        SizeText.Text = "Size: " + ByteFormatter.Format(item.TotalSize);
        FolderText.Text = "Saved to: " + item.SaveDirectory;
        FolderText.ToolTip = item.FullPath;
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        ShellHelper.OpenFile(_item.FullPath);
        Close();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        ShellHelper.OpenFolder(_item.FullPath);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DontShowCheck.IsChecked == true)
        {
            App.Services.Settings.ShowCompleteDialog = false;
            App.Services.SaveSettings();
        }
        base.OnClosed(e);
    }
}
