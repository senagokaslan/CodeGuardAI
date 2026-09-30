using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.Repositories;

namespace CodeGuardAI.Application.Context;

public interface IRepositoryContextBuilder
{
    Task<Result<RepositoryContext>> BuildAsync(
        string repositoryRoot,
        ScanManifest manifest,
        CancellationToken cancellationToken);
}
