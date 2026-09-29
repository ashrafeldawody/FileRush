using System.Windows;
using FileRush.App.Infrastructure;

namespace FileRush.App.Dialogs;

public partial class SpeedLimiterWindow : Window
{
    public SpeedLimiterWindow()
    {
        InitializeComponent();
        AppIcons.Apply(this);
        var settings = App.Services.Settings.SpeedLimit;
        EnableCheck.IsChecked = settings.Enabled;
        SpeedBox.Text = settings.MaxKilobytesPerSecond.ToString();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SpeedBox.Text.Trim(), out var speed) || speed <= 0)
        {
            MessageBox.Show(this, "Enter a positive number of KB/sec.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        App.Services.Manager.SetSpeedLimit(EnableCheck.IsChecked == true, speed);
        App.Services.SaveSettings();
        DialogResult = true;
    }
}
