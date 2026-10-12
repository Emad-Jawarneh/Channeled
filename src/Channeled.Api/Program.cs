using Channeled.Application.Abstractions.Storage;
using Channeled.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FileStoreOptions>(builder.Configuration.GetSection(FileStoreOptions.SectionName));
builder.Services.AddChanneledInfrastructure();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", (IOptions<FileStoreOptions> storeOptions) =>
        Results.Ok(new
        {
            status = "ok",
            store = storeOptions.Value.Kind.ToString(),
        }))
    .WithName("Health")
    .WithTags("Health");

app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok" }))
    .WithName("ApiHealth")
    .WithTags("Health");

app.Run();

public partial class Program;
