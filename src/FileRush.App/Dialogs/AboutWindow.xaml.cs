using System.Reflection;
using System.Windows;
using FileRush.App.Infrastructure;

namespace FileRush.App.Dialogs;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        AppIcons.Apply(this);
        LogoImage.Source = AppIcons.WindowIcon;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3) ?? "1.0.0"}";
    }
}
