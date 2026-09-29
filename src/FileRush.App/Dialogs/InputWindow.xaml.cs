using System.Windows;
using FileRush.App.Infrastructure;

namespace FileRush.App.Dialogs;

public partial class InputWindow : Window
{
    public InputWindow(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        AppIcons.Apply(this);
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    public static string? Ask(Window owner, string title, string prompt, string initial = "")
    {
        var dialog = new InputWindow(title, prompt, initial) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }
}
