namespace Kotodama;

/// <summary>
/// Resolves paths that depend on the application deployment layout.
/// </summary>
internal static class ApplicationPaths
{
    /// <summary>
    /// Resolves the default database path for installed and development layouts.
    /// </summary>
    /// <param name="baseDirectory">The executable base directory.</param>
    /// <returns>The default SQLite database path.</returns>
    public static string GetDefaultDatabasePath(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(baseDirectory));
        if (parent is not null)
        {
            var dataDirectory = Path.Combine(parent.FullName, "data");
            if (Directory.Exists(dataDirectory))
            {
                return Path.Combine(dataDirectory, "kotodama.db");
            }
        }

        return Path.Combine(baseDirectory, "kotodama.db");
    }

    /// <summary>配置構成に応じたログディレクトリを返します。</summary>
    public static string GetLogDirectory(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(baseDirectory));
        return parent is not null && Directory.Exists(Path.Combine(parent.FullName, "logs"))
            ? Path.Combine(parent.FullName, "logs")
            : Path.Combine(baseDirectory, "logs");
    }
}
