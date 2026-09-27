using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using PersonalTracker.Application.Abstractions;
using PersonalTracker.Domain.Exceptions;

namespace PersonalTracker.Infrastructure.Storage;

public sealed class R2Options
{
    public string AccountId { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
}

public sealed class R2FileStorage : IFileStorage
{
    private static readonly HashSet<string> AllowedExtensions =
        [".jpg", ".png", ".webp"];

    private readonly IAmazonS3 _s3;
    private readonly string _bucket;

    public R2FileStorage(R2Options options)
    {
        _bucket = options.BucketName;

        var config = new AmazonS3Config
        {
            ServiceURL =
                $"https://{options.AccountId}.r2.cloudflarestorage.com",

            AuthenticationRegion = "auto",
            ForcePathStyle = true,
        };

        _s3 = new AmazonS3Client(
            options.AccessKeyId,
            options.SecretAccessKey,
            config);
    }

    public async Task<string> SaveAsync(
        Guid userId,
        Stream content,
        string extension,
        CancellationToken ct)
    {
        var ext = extension.ToLowerInvariant();

        if (!AllowedExtensions.Contains(ext))
            throw new ArgumentException(
                "Unsupported file extension.",
                nameof(extension));

        var key = $"covers/{userId:N}/{Guid.NewGuid():N}{ext}";

        var contentType = ext switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".jpg" => "image/jpeg",
            _ => throw new ArgumentException(
                "Unsupported image type.",
                nameof(extension))
        };

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,

            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };

        await _s3.PutObjectAsync(request, ct);

        return key;
    }

    public async Task<Stream> OpenReadAsync(
        string path,
        CancellationToken ct)
    {
        try
        {
            var response =
                await _s3.GetObjectAsync(
                    _bucket,
                    path,
                    ct);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex)
            when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotFoundException("File not found.");
        }
    }

    public async Task DeleteAsync(
        string path,
        CancellationToken ct)
    {
        try
        {
            await _s3.DeleteObjectAsync(
                _bucket,
                path,
                ct);
        }
        catch (AmazonS3Exception)
        {
            // Best effort.
        }
    }
}