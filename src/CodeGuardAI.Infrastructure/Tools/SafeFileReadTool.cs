using System.Security;
using System.Text;
using CodeGuardAI.Application.Tools;
using CodeGuardAI.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeGuardAI.Infrastructure.Tools;

public sealed class SafeFileReadTool : IFileReadTool
{
    private const int MaxReadableBytes = 1024 * 1024;
    private static readonly byte[] Utf8Preamble = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly SafePathResolver _safePathResolver;
    private readonly ToolExecutionEnvelope _envelope;
    private readonly ILogger<SafeFileReadTool> _logger;

    public SafeFileReadTool(
        SafePathResolver safePathResolver,
        ToolExecutionEnvelope envelope,
        ILogger<SafeFileReadTool> logger)
    {
        _safePathResolver = safePathResolver;
        _envelope = envelope;
        _logger = logger;
    }

    public SafeFileReadTool(SafePathResolver safePathResolver)
        : this(
            safePathResolver,
            new ToolExecutionEnvelope(
                new InternalToolAuthorizationPolicy(),
                new NullToolExecutionWriter(),
                TimeProvider.System),
            NullLogger<SafeFileReadTool>.Instance)
    {
    }

    public Task<ToolResult<FileReadContent>> ExecuteAsync(
        FileReadToolInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return _envelope.ExecuteAsync(
            ToolNames.FileRead,
            input.ToRedactedAuditSummary(),
            input.Execution,
            _logger,
            token => ReadCoreAsync(input, token),
            cancellationToken);
    }

    private async Task<ToolOperationResult<FileReadContent>> ReadCoreAsync(
        FileReadToolInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Path.IsPathFullyQualified(input.RelativePath) ||
            SecretPathPolicy.IsDenied(input.RelativePath))
        {
            return ToolOperationResult<FileReadContent>.Failure(FileReadErrorCodes.InvalidPath);
        }

        var rootResolution = _safePathResolver.ResolveRoot(input.RepositoryRoot);
        if (!rootResolution.IsSuccess)
        {
            return ToolOperationResult<FileReadContent>.Failure(MapPathFailure(rootResolution.Failure));
        }

        var pathResolution = _safePathResolver.ResolvePath(rootResolution.FullPath!, input.RelativePath);
        if (!pathResolution.IsSuccess)
        {
            return ToolOperationResult<FileReadContent>.Failure(MapPathFailure(pathResolution.Failure));
        }

        try
        {
            var file = new FileInfo(pathResolution.FullPath!);
            if (file.Length > MaxReadableBytes)
            {
                return ToolOperationResult<FileReadContent>.Failure(FileReadErrorCodes.TooLarge);
            }

            var bytes = await File.ReadAllBytesAsync(pathResolution.FullPath!, cancellationToken);
            if (bytes.Length > MaxReadableBytes)
            {
                return ToolOperationResult<FileReadContent>.Failure(FileReadErrorCodes.TooLarge);
            }

            if (LooksBinary(bytes))
            {
                return ToolOperationResult<FileReadContent>.Failure(FileReadErrorCodes.Binary);
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
                return ToolOperationResult<FileReadContent>.Failure(FileReadErrorCodes.InvalidEncoding);
            }

            var normalizedRelativePath = Path.GetRelativePath(
                    rootResolution.FullPath!,
                    pathResolution.FullPath!)
                .Replace(Path.DirectorySeparatorChar, '/');
            return ToolOperationResult<FileReadContent>.Success(
                new FileReadContent(normalizedRelativePath, content),
                $"success=true; bytes={bytes.Length}; truncated=false");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            return ToolOperationResult<FileReadContent>.Failure(FileReadErrorCodes.Failed);
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

    private static string MapPathFailure(SafePathFailure failure)
    {
        return failure == SafePathFailure.NotFound
            ? FileReadErrorCodes.NotFound
            : FileReadErrorCodes.InvalidPath;
    }

    private static bool IsReadException(Exception exception)
    {
        return exception is ArgumentException or IOException or UnauthorizedAccessException or
            NotSupportedException or SecurityException;
    }
}
