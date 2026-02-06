var builder = WebApplication.CreateBuilder(args);

// Minimal API — MVP 1 has no endpoints yet.
// This project exists as a placeholder for future REST endpoints.

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
