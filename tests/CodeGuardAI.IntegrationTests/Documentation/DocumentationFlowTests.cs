using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeGuardAI.Api.Contracts.Reviews;
using CodeGuardAI.Application.Common;
using CodeGuardAI.Application.LLM;
using CodeGuardAI.Application.Workflows;
using CodeGuardAI.Domain.Documentation;
using CodeGuardAI.Domain.Observability;
using CodeGuardAI.Domain.Reviews;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CodeGuardAI.IntegrationTests.Documentation;

public sealed class DocumentationFlowTests
{
    private static readonly Guid ReviewId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FindingId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Offline_flow_creates_gets_and_rejects_second_report_without_test_cases()
    {
        var store = new ConcurrentDocsStore(CreateSource());
        var provider = new OfflineDocumentationProvider();
        await using var factory = CreateFactory(store, provider);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/documentation",
            ValidRequest());
        var createdBody = await created.Content.ReadFromJsonAsync<DocumentationResponse>();
        using var fetched = await client.GetAsync($"/api/reviews/{ReviewId}/documentation");
        var fetchedBody = await fetched.Content.ReadFromJsonAsync<DocumentationResponse>();
        using var duplicate = await client.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/documentation",
            ValidRequest());
        using var duplicateJson = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(createdBody);
        Assert.Contains("Severity: High", createdBody.MarkdownContent, StringComparison.Ordinal);
        Assert.Contains("No persisted test suggestions", createdBody.MarkdownContent, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal(createdBody, fetchedBody);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(
            "documentation.already_generated",
            duplicateJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(1, store.SuccessfulCompletionCount);
    }

    [Fact]
    public async Task Concurrent_offline_requests_create_exactly_one_report_and_one_conflict()
    {
        var store = new ConcurrentDocsStore(CreateSource());
        var provider = new OfflineDocumentationProvider(concurrentCallsBeforeRelease: 2);
        await using var factory = CreateFactory(store, provider);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var firstRequest = firstClient.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/documentation",
            ValidRequest());
        var secondRequest = secondClient.PostAsJsonAsync(
            $"/api/reviews/{ReviewId}/documentation",
            ValidRequest());
        using var firstResponse = await firstRequest;
        using var secondResponse = await secondRequest;
        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };

        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.Contains(HttpStatusCode.Conflict, statuses);
        Assert.Equal(2, provider.CallCount);
        Assert.Equal(1, store.SuccessfulCompletionCount);
        Assert.NotNull(await store.GetAsync(ReviewId, CancellationToken.None));
    }

    private static CreateDocumentationRequest ValidRequest() =>
        new() { Model = "offline-docs-model", TimeoutSeconds = 30 };

    private static DocsGenerationSource CreateSource()
    {
        var review = ReviewRun.Start(
            ReviewId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "review-model",
            "review-v1",
            CreatedAtUtc.AddMinutes(-2));
        Assert.True(review.TryComplete(CreatedAtUtc.AddMinutes(-1)));
        var finding = Finding.Create(
            FindingId,
            ReviewId,
            FindingSeverity.High,
            FindingCategory.Security,
            "src/Service.cs",
            10,
            12,
            "Unsafe input",
            "Input is not validated",
            "Validate before use",
            0.9m,
            CreatedAtUtc.AddMinutes(-1));
        return new DocsGenerationSource(review, "Offline project", [finding], []);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        ConcurrentDocsStore store,
        OfflineDocumentationProvider provider)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder
                    .UseEnvironment(Environments.Development)
                    .UseCodeGuardTestOptions();
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDocsWorkflowStore>();
                    services.RemoveAll<ILLMProvider>();
                    services.AddSingleton<IDocsWorkflowStore>(store);
                    services.AddSingleton<ILLMProvider>(provider);
                });
            });
    }

    private sealed class OfflineDocumentationProvider(int concurrentCallsBeforeRelease = 1)
        : ILLMProvider
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public string Name => "offline-fake";

        public int CallCount => _callCount;

        public Task<LLMProviderResult> GenerateReviewAsync(
            LLMRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fake only supports documentation generation.");

        public Task<LLMProviderResult> GenerateTestsAsync(
            LLMRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This fake only supports documentation generation.");

        public async Task<LLMProviderResult> GenerateDocumentationAsync(
            LLMRequest request,
            CancellationToken cancellationToken)
        {
            var callCount = Interlocked.Increment(ref _callCount);
            if (callCount >= concurrentCallsBeforeRelease)
            {
                _release.TrySetResult();
            }

            await _release.Task.WaitAsync(cancellationToken);
            var content = JsonSerializer.Serialize(new
            {
                title = "Technical report",
                executiveSummary = "Offline summary",
                findingSections = new[]
                {
                    new { findingId = FindingId, impact = "Input may cross the trust boundary" }
                },
                testCaseSections = Array.Empty<object>(),
                conclusion = "Apply the persisted recommendation"
            });
            return LLMProviderResult.Success(new LLMResponse(
                Name,
                request.Model,
                request.PromptVersion,
                content,
                TimeSpan.FromMilliseconds(5)));
        }
    }

    private sealed class ConcurrentDocsStore(DocsGenerationSource source) : IDocsWorkflowStore
    {
        private readonly object _gate = new();
        private DocumentationReadModel? _report;

        public int SuccessfulCompletionCount { get; private set; }

        public Task<DocsGenerationSource?> GetSourceAsync(
            Guid reviewId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<DocsGenerationSource?>(reviewId == source.ReviewRun.Id ? source : null);
        }

        public Task<bool> HasCompletedReportAsync(
            Guid reviewId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(_report is not null);
            }
        }

        public Task<bool> TryCompleteAsync(
            DocumentationReport report,
            AIModelRun modelRun,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_report is not null)
                {
                    return Task.FromResult(false);
                }

                _report = new DocumentationReadModel(
                    report.Id,
                    report.ReviewRunId,
                    report.ModelRunId,
                    report.Title,
                    report.MarkdownContent,
                    report.CreatedAtUtc);
                SuccessfulCompletionCount++;
                return Task.FromResult(true);
            }
        }

        public Task SaveFailedModelRunAsync(
            AIModelRun modelRun,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The successful offline flow must not persist a failed run.");

        public Task<DocumentationReadModel?> GetAsync(
            Guid reviewId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                return Task.FromResult(reviewId == source.ReviewRun.Id ? _report : null);
            }
        }
    }
}
