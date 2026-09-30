using System.Security;
using System.Text;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.Repositories;

namespace CodeGuardAI.Infrastructure.Tools;

public sealed class SafeFileReadTool(SafePathResolver safePathResolver) : IFileReadTool
{
    private const int MaxReadableBytes = 1024 * 1024;
    private static readonly byte[] Utf8Preamble = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<Result<FileReadContent>> ReadAsync(
        string repositoryRoot,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathFullyQualified(relativePath) ||
            SecretPathPolicy.IsDenied(relativePath))
        {
            return Result.Failure<FileReadContent>(FileReadErrors.InvalidPath);
        }

        var rootResolution = safePathResolver.ResolveRoot(repositoryRoot);
        if (!rootResolution.IsSuccess)
        {
            return Result.Failure<FileReadContent>(MapPathFailure(rootResolution.Failure));
        }

        var pathResolution = safePathResolver.ResolvePath(rootResolution.FullPath!, relativePath);
        if (!pathResolution.IsSuccess)
        {
            return Result.Failure<FileReadContent>(MapPathFailure(pathResolution.Failure));
        }

        try
        {
            var file = new FileInfo(pathResolution.FullPath!);
            if (file.Length > MaxReadableBytes)
            {
                return Result.Failure<FileReadContent>(FileReadErrors.TooLarge);
            }

            var bytes = await File.ReadAllBytesAsync(pathResolution.FullPath!, cancellationToken);
            if (bytes.Length > MaxReadableBytes)
            {
                return Result.Failure<FileReadContent>(FileReadErrors.TooLarge);
            }

            if (LooksBinary(bytes))
            {
                return Result.Failure<FileReadContent>(FileReadErrors.Binary);
            }

            var contentBytes = bytes.AsSpan();
            if (contentBytes.StartsWith(Utf8Preamble))
            {
                contentBytes = contentBytes[Utf8Preamble.Length..];
            }

            string content;
            try
            {
                content = StrictUtf8.GetString(contentBytes);
            }
            catch (DecoderFallbackException)
            {
                return Result.Failure<FileReadContent>(FileReadErrors.InvalidEncoding);
            }

            var normalizedRelativePath = Path.GetRelativePath(
                    rootResolution.FullPath!,
                    pathResolution.FullPath!)
                .Replace(Path.DirectorySeparatorChar, '/');
            return Result.Success(new FileReadContent(normalizedRelativePath, content));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            return Result.Failure<FileReadContent>(FileReadErrors.Failed);
        }
    }

    private static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Contains((byte)0))
        {
            return true;
        }

        var inspectedLength = Math.Min(bytes.Length, 8_192);
        if (inspectedLength == 0)
        {
            return false;
        }

        var suspiciousBytes = 0;
        for (var index = 0; index < inspectedLength; index++)
        {
            var value = bytes[index];
            if (value < 0x20 && value is not (byte)'\t' and not (byte)'\n' and not (byte)'\r')
            {
                suspiciousBytes++;
            }
        }

        return suspiciousBytes * 10 > inspectedLength;
    }

    private static Error MapPathFailure(SafePathFailure failure)
    {
        return failure == SafePathFailure.NotFound
            ? FileReadErrors.NotFound
            : FileReadErrors.InvalidPath;
    }

    private static bool IsReadException(Exception exception)
    {
        return exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException or SecurityException;
    }
}
