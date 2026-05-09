using Microsoft.EntityFrameworkCore;
using Prometheus;
using TaxFilingAPI.Data;
using TaxFilingAPI.Middleware;
using TaxFilingAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────
// 1. CONTROLLERS & API DOCUMENTATION
// ─────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Tax Filing Observability API",
        Version = "v1",
        Description = "A .NET 10 Web API demonstrating intelligent observability " +
                      "and resilience for tax filing workflows running on Kubernetes."
    });
});

// ─────────────────────────────────────────
// 2. DATABASE — SQL SERVER
// ─────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            // Resilience: auto-retry on transient failures
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
        }));

// ─────────────────────────────────────────
// 3. APPLICATION SERVICES
// ─────────────────────────────────────────
builder.Services.AddScoped<ITaskService, TaskService>();

// ─────────────────────────────────────────
// 4. HEALTH CHECKS
// ─────────────────────────────────────────
builder.Services.AddHealthChecks();

// ─────────────────────────────────────────
// 5. HTTP CLIENT FACTORY (for AI diagnosis)
// ─────────────────────────────────────────
builder.Services.AddHttpClient();

// ─────────────────────────────────────────
// 6. LOGGING
// ─────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// ─────────────────────────────────────────
// 7. AUTO-MIGRATE DATABASE ON STARTUP
// ─────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// ─────────────────────────────────────────
// 8. MIDDLEWARE PIPELINE
// ─────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Tax Filing API v1");
        c.RoutePrefix = string.Empty; // Swagger at root URL
    });
}

// Custom metrics middleware — captures thread pool metrics
app.UseMiddleware<MetricsMiddleware>();

// Prometheus metrics endpoint — Prometheus scrapes this
app.UseMetricServer();        // exposes /metrics
app.UseHttpMetrics();         // captures HTTP request metrics

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Health check endpoint
app.MapHealthChecks("/health");

app.Run();