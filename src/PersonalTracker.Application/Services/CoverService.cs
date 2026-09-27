namespace PersonalTracker.Application.Services;

public sealed class CoverService(
    ICurrentUser user,
    ICollectionRepository collections,
    IFileStorage storage,
    IUnitOfWork uow,
    TimeProvider clock) : ICoverService
{
    public async Task<CoverDto> SetAsync(Guid collectionId, CoverInput input, CancellationToken ct)
    {
        var collection = await LoadAsync(collectionId, ct);
        var errors = new ErrorBag();
        CoverRules.Validate(input, errors);
        errors.ThrowIfAny();

        var previousImage = collection.CoverType == CoverType.Image ? collection.CoverValue : null;
        var now = clock.GetUtcNow().UtcDateTime;
        collection.CoverType = input.Type;
        collection.CoverValue = input.Type == CoverType.None ? null : input.Value;
        collection.CoverUpdatedAt = input.Type == CoverType.None ? null : now;
        collection.UpdatedAt = now;
        await uow.SaveChangesAsync(ct);

        if (previousImage is not null) await storage.DeleteAsync(previousImage, ct);
        return CollectionMapper.ToCoverDto(collection);
    }

    public async Task<CoverDto> SetImageAsync(Guid collectionId, Stream content, long length, CancellationToken ct)
    {
        var collection = await LoadAsync(collectionId, ct);
        if (length <= 0) throw Invalid("Choose an image to upload.");
        if (length > Limits.MaxCoverBytes) throw Invalid("The image must be 5 MB or smaller.");

        // Buffer (bounded) so the real content can be sniffed; never trust the file name or Content-Type.
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > Limits.MaxCoverBytes) throw Invalid("The image must be 5 MB or smaller.");
            buffer.Write(chunk, 0, read);
        }

        var extension = DetectExtension(buffer.GetBuffer().AsSpan(0, (int)buffer.Length))
                        ?? throw Invalid("Only JPEG, PNG or WebP images are supported.");

        buffer.Position = 0;
        var newPath = await storage.SaveAsync(user.Id, buffer, extension, ct);
        var previousImage = collection.CoverType == CoverType.Image ? collection.CoverValue : null;

        var now = clock.GetUtcNow().UtcDateTime;
        collection.CoverType = CoverType.Image;
        collection.CoverValue = newPath;
        collection.CoverUpdatedAt = now;
        collection.UpdatedAt = now;

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(newPath, CancellationToken.None); // do not leave an orphan behind
            throw;
        }

        if (previousImage is not null) await storage.DeleteAsync(previousImage, ct);
        return CollectionMapper.ToCoverDto(collection);
    }

    public async Task<CoverImage> GetImageAsync(Guid collectionId, CancellationToken ct)
    {
        var collection = await collections.GetAsync(collectionId, user.Id, includeFields: false, track: false, ct)
                         ?? throw new NotFoundException("Collection not found.");
        if (collection.CoverType != CoverType.Image || collection.CoverValue is null)
            throw new NotFoundException("This collection has no cover image.");

        var contentType = Path.GetExtension(collection.CoverValue).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return new CoverImage(await storage.OpenReadAsync(collection.CoverValue, ct), contentType);
    }

    private async Task<Collection> LoadAsync(Guid id, CancellationToken ct) =>
        await collections.GetAsync(id, user.Id, includeFields: false, track: true, ct)
        ?? throw new NotFoundException("Collection not found.");

    private static RequestValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["file"] = [message] });

    private static string? DetectExtension(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 &&
            b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return ".png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' &&
            b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
        return null;
    }
}