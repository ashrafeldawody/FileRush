using System.Windows;
using FileRush.App.Infrastructure;

namespace FileRush.App.Dialogs;

public partial class DeleteConfirmWindow : Window
{
    public DeleteConfirmWindow(string message, bool offerDeleteFile)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        MessageText.Text = message;
        DeleteFileCheck.Visibility = offerDeleteFile ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool DeleteFile => DeleteFileCheck.IsChecked == true;

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
