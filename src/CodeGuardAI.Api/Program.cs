using CodeGuardAI.Api;
using CodeGuardAI.Api.Errors;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
        options.InvalidModelStateResponseFactory = ApiProblemDetailsFactory.CreateValidationResponse);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddCodeGuardServices(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "CodeGuard AI API v1");
        options.DocumentTitle = "CodeGuard AI API";
    });
    app.MapGet("/", () => Results.Redirect("/swagger"))
        .ExcludeFromDescription();
}

app.MapControllers();

app.Run();

public partial class Program;
