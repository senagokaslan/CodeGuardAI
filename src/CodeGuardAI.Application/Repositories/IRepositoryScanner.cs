using CodeGuardAI.Application.Common;

namespace CodeGuardAI.Application.Repositories;

public interface IRepositoryScanner
{
    Task<Result<ScanManifest>> ScanAsync(
        string repositoryRoot,
        CancellationToken cancellationToken);
}
