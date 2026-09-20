namespace PersonalTracker.Infrastructure.Storage;

public sealed class StorageOptions
{
    /// <summary>Relative paths are resolved against the working directory. Use an absolute path in production.</summary>
    public string RootPath { get; set; } = "App_Data/storage";
}

public sealed class LocalFileStorage(StorageOptions options) : IFileStorage
{
    private static readonly HashSet<string> AllowedExtensions = [".jpg", ".png", ".webp"];
    private readonly string _root = Path.GetFullPath(options.RootPath);

    public async Task<string> SaveAsync(Guid userId, Stream content, string extension, CancellationToken ct)
    {
        var ext = extension.ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) throw new ArgumentException("Unsupported file extension.", nameof(extension));

        // The name is generated server-side: nothing the client sends ever reaches the file system path.
        var relative = $"covers/{userId:N}/{Guid.NewGuid():N}{ext}";
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        await using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
        return relative;
    }

    public Stream OpenRead(string path)
    {
        var full = Resolve(path);
        if (!File.Exists(full)) throw new NotFoundException("File not found.");
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
    }

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        try
        {
            var full = Resolve(path);
            if (File.Exists(full)) File.Delete(full);
        }
        catch (IOException)
        {
            // best effort: a locked/missing file must never fail the user's request
        }
        return Task.CompletedTask;
    }

    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid storage path.");
        return full;
    }
}
