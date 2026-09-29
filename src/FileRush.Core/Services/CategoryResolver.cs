using FileRush.Core.Models;

namespace FileRush.Core.Services;

public static class CategoryResolver
{
    public static Category Resolve(IEnumerable<Category> categories, string fileNameOrUrl)
    {
        var list = categories.ToList();
        var name = fileNameOrUrl;
        if (Uri.TryCreate(fileNameOrUrl, UriKind.Absolute, out var uri))
        {
            name = uri.AbsolutePath;
        }
        foreach (var category in list)
        {
            if (category.Extensions.Count == 0) continue;
            if (category.Matches(name)) return category;
        }
        return list.FirstOrDefault(c => c.Name.Equals("General", StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault()
               ?? new Category { Name = "General" };
    }

    public static Category? Find(IEnumerable<Category> categories, string name)
    {
        return categories.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
