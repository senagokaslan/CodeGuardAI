using CodeGuardAI.Application.Options;
using CodeGuardAI.Infrastructure;
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

builder.Services.AddCodeGuardPersistence();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ReadFileMcpTool>();

await builder.Build().RunAsync();
