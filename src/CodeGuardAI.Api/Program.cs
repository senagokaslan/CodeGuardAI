using CodeGuardAI.Api.Errors;
using CodeGuardAI.Application.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = ApiProblemDetailsFactory.CreateValidationResponse);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services
    .AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ConnectionString),
        "Configuration key Database:ConnectionString is required.")
    .ValidateOnStart();
builder.Services
    .AddOptions<GeminiOptions>()
    .Bind(builder.Configuration.GetSection(GeminiOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApiKey),
        "Configuration key Gemini:ApiKey is required.")
    .ValidateOnStart();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

app.Run();

public partial class Program;
