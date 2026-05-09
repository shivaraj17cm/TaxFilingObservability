using Prometheus;
using System.Diagnostics;

namespace TaxFilingAPI.Middleware
{
    public class MetricsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MetricsMiddleware> _logger;

        // ─────────────────────────────────────────
        // PROMETHEUS METRICS DEFINITIONS
        // ─────────────────────────────────────────

        // Tracks how many requests are currently being processed
        private static readonly Gauge ActiveRequests = Metrics
            .CreateGauge(
                "taxfiling_active_requests",
                "Number of requests currently being processed");

        // Tracks available worker threads in the thread pool
        private static readonly Gauge AvailableWorkerThreads = Metrics
            .CreateGauge(
                "taxfiling_threadpool_available_worker_threads",
                "Available worker threads in the .NET thread pool");

        // Tracks available completion port threads
        private static readonly Gauge AvailableCompletionPortThreads = Metrics
            .CreateGauge(
                "taxfiling_threadpool_available_completion_port_threads",
                "Available I/O completion port threads in the .NET thread pool");

        // Tracks maximum worker threads configured
        private static readonly Gauge MaxWorkerThreads = Metrics
            .CreateGauge(
                "taxfiling_threadpool_max_worker_threads",
                "Maximum worker threads configured in the .NET thread pool");

        // Tracks how many threads are currently IN USE (max - available)
        private static readonly Gauge ThreadsInUse = Metrics
            .CreateGauge(
                "taxfiling_threadpool_threads_in_use",
                "Number of thread pool threads currently in use");

        // Tracks thread pool exhaustion percentage
        private static readonly Gauge ThreadPoolExhaustionPercent = Metrics
            .CreateGauge(
                "taxfiling_threadpool_exhaustion_percent",
                "Percentage of thread pool exhausted (0-100)");

        // Tracks request duration
        private static readonly Histogram RequestDuration = Metrics
            .CreateHistogram(
                "taxfiling_request_duration_seconds",
                "HTTP request duration in seconds",
                new HistogramConfiguration
                {
                    Buckets = Histogram.LinearBuckets(
                        start: 0.1,
                        width: 0.1,
                        count: 20)
                });

        // Counts total requests by endpoint and status
        private static readonly Counter TotalRequests = Metrics
            .CreateCounter(
                "taxfiling_requests_total",
                "Total number of requests",
                new CounterConfiguration
                {
                    LabelNames = new[] { "method", "endpoint", "status_code" }
                });

        // Counts thread starvation events detected
        private static readonly Counter ThreadStarvationEvents = Metrics
            .CreateCounter(
                "taxfiling_thread_starvation_events_total",
                "Number of times thread pool exhaustion exceeded 80% threshold");

        public MetricsMiddleware(RequestDelegate next, ILogger<MetricsMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();

            // Capture thread pool state BEFORE processing request
            ThreadPool.GetAvailableThreads(
                out int availableWorker,
                out int availableCompletion);

            ThreadPool.GetMaxThreads(
                out int maxWorker,
                out int maxCompletion);

            int threadsInUse = maxWorker - availableWorker;
            double exhaustionPercent = ((double)threadsInUse / maxWorker) * 100;

            // Update thread pool metrics
            AvailableWorkerThreads.Set(availableWorker);
            AvailableCompletionPortThreads.Set(availableCompletion);
            MaxWorkerThreads.Set(maxWorker);
            ThreadsInUse.Set(threadsInUse);
            ThreadPoolExhaustionPercent.Set(exhaustionPercent);
            ActiveRequests.Inc();

            // ─────────────────────────────────────────
            // THREAD STARVATION EARLY WARNING
            // ─────────────────────────────────────────
            if (exhaustionPercent >= 80)
            {
                ThreadStarvationEvents.Inc();
                _logger.LogWarning(
                    "⚠️ THREAD POOL WARNING: {ExhaustionPercent:F1}% exhausted. " +
                    "Threads in use: {ThreadsInUse}/{MaxWorker}. " +
                    "Available: {AvailableWorker}",
                    exhaustionPercent,
                    threadsInUse,
                    maxWorker,
                    availableWorker);
            }

            // ─────────────────────────────────────────
            // CRITICAL: Thread starvation detected
            // ─────────────────────────────────────────
            if (exhaustionPercent >= 95)
            {
                _logger.LogCritical(
                    "🚨 CRITICAL: Thread pool near exhaustion! " +
                    "{ExhaustionPercent:F1}% used. " +
                    "System may become unresponsive. " +
                    "Available threads: {AvailableWorker}",
                    exhaustionPercent,
                    availableWorker);
            }

            try
            {
                // Process the actual request
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();
                ActiveRequests.Dec();

                // Record request completion metrics
                var endpoint = context.Request.Path.Value ?? "unknown";
                var method = context.Request.Method;
                var statusCode = context.Response.StatusCode.ToString();

                RequestDuration.Observe(stopwatch.Elapsed.TotalSeconds);
                TotalRequests
                    .WithLabels(method, endpoint, statusCode)
                    .Inc();

                // Log slow requests — another early warning signal
                if (stopwatch.Elapsed.TotalSeconds > 2)
                {
                    _logger.LogWarning(
                        "🐢 SLOW REQUEST: {Method} {Endpoint} took {Duration:F2}s " +
                        "(StatusCode: {StatusCode})",
                        method,
                        endpoint,
                        stopwatch.Elapsed.TotalSeconds,
                        statusCode);
                }
            }
        }
    }
}