namespace FileRush.Core.Models;

public sealed class Category
{
    public string Name { get; set; } = string.Empty;

    public List<string> Extensions { get; set; } = new();

    public string SaveDirectory { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public bool Matches(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.');
        if (ext.Length == 0) return false;
        return Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    public static List<Category> CreateDefaults(string downloadsRoot)
    {
        return new List<Category>
        {
            new() { Name = "General", Extensions = new(), SaveDirectory = downloadsRoot, IsBuiltIn = true },
            new() { Name = "Compressed", Extensions = Split("zip rar 7z gz tgz tar bz2 xz iso cab arj lzh ace z lz zst"), SaveDirectory = Path.Combine(downloadsRoot, "Compressed"), IsBuiltIn = true },
            new() { Name = "Documents", Extensions = Split("doc docx pdf ppt pptx xls xlsx txt rtf odt ods odp epub chm csv xps djvu md"), SaveDirectory = Path.Combine(downloadsRoot, "Documents"), IsBuiltIn = true },
            new() { Name = "Music", Extensions = Split("mp3 wav wma ogg flac aac m4a mid midi opus ape aiff"), SaveDirectory = Path.Combine(downloadsRoot, "Music"), IsBuiltIn = true },
            new() { Name = "Programs", Extensions = Split("exe msi msix apk dmg deb rpm bin jar appx appimage pkg"), SaveDirectory = Path.Combine(downloadsRoot, "Programs"), IsBuiltIn = true },
            new() { Name = "Video", Extensions = Split("avi mp4 mkv mov wmv flv mpg mpeg 3gp webm m4v ts vob rmvb divx"), SaveDirectory = Path.Combine(downloadsRoot, "Video"), IsBuiltIn = true },
        };
    }

    private static List<string> Split(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
}
