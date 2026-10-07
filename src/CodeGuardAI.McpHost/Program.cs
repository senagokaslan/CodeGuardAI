using CodeGuardAI.Application.Options;
using CodeGuardAI.Infrastructure;
using CodeGuardAI.Infrastructure.Tools;
using CodeGuardAI.McpHost.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
    options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ConnectionString),
        "Configuration key Database:ConnectionString is required.")
    .ValidateOnStart();
builder.Services
    .AddOptions<TestRunnerOptions>()
    .Bind(builder.Configuration.GetSection(TestRunnerOptions.SectionName))
    .Validate(TestRunnerOptions.IsValid, "Test runner configuration is invalid.")
    .ValidateOnStart();

builder.Services.AddCodeGuardPersistence();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ReadFileMcpTool>()
    .WithTools<RunTestsMcpTool>();

await builder.Build().RunAsync();
