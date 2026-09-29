using System.Windows;
using FileRush.App.Infrastructure;
using FileRush.Core.Models;
using Microsoft.Win32;

namespace FileRush.App.Dialogs;

public partial class CategoryWindow : Window
{
    private readonly Category _category;
    private readonly bool _isNew;

    public CategoryWindow(Category? existing)
    {
        InitializeComponent();
        AppIcons.Apply(this);
        _isNew = existing is null;
        _category = existing ?? new Category();
        Title = _isNew ? "New category" : "Edit category";
        NameBox.Text = _category.Name;
        NameBox.IsEnabled = !_category.IsBuiltIn;
        ExtensionsBox.Text = string.Join(" ", _category.Extensions);
        FolderBox.Text = _category.SaveDirectory;
    }

    public Category Category => _category;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = FolderBox.Text, Title = "Select the default folder for this category" };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Please enter a category name.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var settings = App.Services.Settings;
        if (_isNew && settings.Categories.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "A category with this name already exists.", "File Rush", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _category.Name = name;
        _category.Extensions = ExtensionsBox.Text
            .Split(new[] { ' ', ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().TrimStart('.').ToLowerInvariant())
            .Distinct()
            .ToList();
        _category.SaveDirectory = FolderBox.Text.Trim();
        if (_isNew) settings.Categories.Add(_category);
        App.Services.SaveSettings();
        DialogResult = true;
    }
}
