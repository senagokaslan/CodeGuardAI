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
}

app.MapControllers();

app.Run();

public partial class Program;
