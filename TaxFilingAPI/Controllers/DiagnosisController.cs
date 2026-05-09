using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace TaxFilingAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DiagnosisController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DiagnosisController> _logger;
        private readonly IConfiguration _configuration;

        public DiagnosisController(
            IHttpClientFactory httpClientFactory,
            ILogger<DiagnosisController> logger,
            IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Analyze current thread pool health and get AI diagnosis
        /// </summary>
        [HttpPost("analyze")]
        [ProducesResponseType(typeof(DiagnosisResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> AnalyzeThreadHealth()
        {
            // Step 1 — Capture current thread pool state
            ThreadPool.GetAvailableThreads(
                out int availableWorker,
                out int availableCompletion);

            ThreadPool.GetMaxThreads(
                out int maxWorker,
                out int maxCompletion);

            int threadsInUse = maxWorker - availableWorker;
            double exhaustionPercent = ((double)threadsInUse / maxWorker) * 100;

            var metrics = new ThreadPoolMetrics
            {
                MaxWorkerThreads = maxWorker,
                AvailableWorkerThreads = availableWorker,
                ThreadsInUse = threadsInUse,
                ExhaustionPercent = Math.Round(exhaustionPercent, 2),
                MaxCompletionPortThreads = maxCompletion,
                AvailableCompletionPortThreads = availableCompletion,
                ActiveRequests = threadsInUse,
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation(
                "Diagnosis requested. Thread pool: {ExhaustionPercent}% exhausted",
                metrics.ExhaustionPercent);

            // Step 2 — Get AI diagnosis
            var diagnosis = await GetAIDiagnosisAsync(metrics);

            // Step 3 — Return combined result
            return Ok(new DiagnosisResult
            {
                Metrics = metrics,
                Diagnosis = diagnosis,
                AnalyzedAt = DateTime.UtcNow
            });
        }

        private async Task<AIDiagnosis> GetAIDiagnosisAsync(ThreadPoolMetrics metrics)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var apiKey = _configuration["Anthropic:ApiKey"];

                // Build the prompt with real metrics
                var prompt = $"""
                    You are an expert .NET performance engineer analyzing a production system.
                    
                    Current Thread Pool Metrics for a .NET 10 Web API (Tax Filing System):
                    - Max Worker Threads: {metrics.MaxWorkerThreads}
                    - Available Worker Threads: {metrics.AvailableWorkerThreads}
                    - Threads Currently In Use: {metrics.ThreadsInUse}
                    - Thread Pool Exhaustion: {metrics.ExhaustionPercent}%
                    - Max Completion Port Threads: {metrics.MaxCompletionPortThreads}
                    - Available Completion Port Threads: {metrics.AvailableCompletionPortThreads}
                    - Timestamp: {metrics.Timestamp:yyyy-MM-dd HH:mm:ss} UTC
                    
                    Based on these metrics:
                    1. What is the current health status? (Healthy/Warning/Critical)
                    2. What is the likely root cause if exhaustion is high?
                    3. What are the top 3 recommended actions?
                    4. What will happen if no action is taken?
                    
                    Be specific and actionable. Format as JSON with fields:
                    healthStatus, rootCause, recommendations (array of 3 strings), riskIfIgnored
                    """;

                var requestBody = new
                {
                    model = "claude-sonnet-4-20250514",
                    max_tokens = 1000,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);
                client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

                var response = await client.PostAsync(
                    "https://api.anthropic.com/v1/messages",
                    content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("AI API call failed: {StatusCode}", response.StatusCode);
                    return GetFallbackDiagnosis(metrics);
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var claudeResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);

                var aiText = claudeResponse
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString() ?? "";

                // Parse AI response as JSON
                var cleanJson = aiText
                    .Replace("```json", "")
                    .Replace("```", "")
                    .Trim();

                var aiDiagnosis = JsonSerializer.Deserialize<AIDiagnosis>(
                    cleanJson,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                return aiDiagnosis ?? GetFallbackDiagnosis(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI diagnosis failed, using fallback");
                return GetFallbackDiagnosis(metrics);
            }
        }

        // Fallback diagnosis when AI is unavailable
        private static AIDiagnosis GetFallbackDiagnosis(ThreadPoolMetrics metrics)
        {
            if (metrics.ExhaustionPercent >= 95)
                return new AIDiagnosis
                {
                    HealthStatus = "Critical",
                    RootCause = "Thread pool near complete exhaustion. " +
                                "Likely caused by synchronous blocking calls " +
                                "(.Result or .Wait()) in async code paths.",
                    Recommendations = new[]
                    {
                        "Immediately audit all .Result and .Wait() calls in the codebase",
                        "Implement circuit breaker pattern to shed load",
                        "Scale horizontally by adding more pod replicas in Kubernetes"
                    },
                    RiskIfIgnored = "Complete system unresponsiveness within minutes. " +
                                   "All incoming requests will queue indefinitely."
                };

            if (metrics.ExhaustionPercent >= 80)
                return new AIDiagnosis
                {
                    HealthStatus = "Warning",
                    RootCause = "Thread pool under significant pressure. " +
                                "Possible causes: blocking async calls, " +
                                "long running synchronous operations, " +
                                "or insufficient thread pool configuration.",
                    Recommendations = new[]
                    {
                        "Review database query performance — slow queries hold threads longer",
                        "Check for missing await keywords in async call chains",
                        "Consider increasing ThreadPool.SetMinThreads for burst capacity"
                    },
                    RiskIfIgnored = "System will become unstable under continued load. " +
                                   "Thread starvation likely within 2-5 minutes."
                };

            return new AIDiagnosis
            {
                HealthStatus = "Healthy",
                RootCause = "No issues detected. Thread pool operating normally.",
                Recommendations = new[]
                {
                    "Continue monitoring — set alerts at 80% exhaustion threshold",
                    "Run load tests periodically to establish baseline metrics",
                    "Review Grafana dashboard for any gradual degradation trends"
                },
                RiskIfIgnored = "No immediate risk. Maintain current monitoring cadence."
            };
        }
    }

    // ─────────────────────────────────────────
    // RESPONSE MODELS
    // ─────────────────────────────────────────

    public class ThreadPoolMetrics
    {
        public int MaxWorkerThreads { get; set; }
        public int AvailableWorkerThreads { get; set; }
        public int ThreadsInUse { get; set; }
        public double ExhaustionPercent { get; set; }
        public int MaxCompletionPortThreads { get; set; }
        public int AvailableCompletionPortThreads { get; set; }
        public int ActiveRequests { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AIDiagnosis
    {
        public string HealthStatus { get; set; } = string.Empty;
        public string RootCause { get; set; } = string.Empty;
        public string[] Recommendations { get; set; } = Array.Empty<string>();
        public string RiskIfIgnored { get; set; } = string.Empty;
    }

    public class DiagnosisResult
    {
        public ThreadPoolMetrics Metrics { get; set; } = new();
        public AIDiagnosis Diagnosis { get; set; } = new();
        public DateTime AnalyzedAt { get; set; }
    }
}