using System.Windows;
using FileRush.App.Infrastructure;
using FileRush.Core.Models;

namespace FileRush.App.Dialogs;

public partial class QueuePickWindow : Window
{
    public QueuePickWindow(IReadOnlyList<DownloadItem> candidates)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        ItemList.ItemsSource = candidates;
    }

    public IReadOnlyList<DownloadItem> Selected { get; private set; } = Array.Empty<DownloadItem>();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        Selected = ItemList.SelectedItems.Cast<DownloadItem>().ToList();
        DialogResult = Selected.Count > 0;
    }
}
