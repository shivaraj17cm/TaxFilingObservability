# Design Document
## Tax Filing Observability Platform
### Intelligent Thread Pool Monitoring & AI-Powered Diagnosis for .NET 10 APIs on Kubernetes

**Author:** Shivaraj  
**Role Applied For:** .NET Lead / Technical Architect  
**Built For:** Damco Build Challenge  
**Document Version:** 1.0  
**Date:** May 2026

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Goals & Non-Goals](#2-goals--non-goals)
3. [Architecture Overview](#3-architecture-overview)
4. [Component Design](#4-component-design)
5. [Data Model](#5-data-model)
6. [Technology Stack & Justification](#6-technology-stack--justification)
7. [Key Architectural Decisions & Trade-offs](#7-key-architectural-decisions--trade-offs)
8. [Design Patterns Used](#8-design-patterns-used)
9. [Failure Modes & Resilience](#9-failure-modes--resilience)
10. [Security Considerations](#10-security-considerations)
11. [Scalability Considerations](#11-scalability-considerations)
12. [What I Would Improve With More Time](#12-what-i-would-improve-with-more-time)
13. [Honest Self-Assessment](#13-honest-self-assessment)

---

## 1. Problem Statement

### 1.1 Background

Our team maintained a suite of ASP.NET Web APIs powering a tax filing workflow system
for the United States market. The system was responsible for managing the complete
lifecycle of tax filing tasks, including:

- **Task Creation** — Assigning new tax filing tasks to agents (Individual 1040,
  Corporate 1120, etc.)
- **Task Search** — Querying tasks by status, assigned agent, and tax year
- **Status Transitions** — Moving tasks through a defined workflow from creation
  to completion

The system was originally deployed on **IIS on Windows servers** and was functioning
correctly under normal production load.

### 1.2 The Migration Attempt

The engineering team decided to migrate the application to **Azure Kubernetes Service
(AKS)** to achieve:

- Better horizontal scalability
- Containerised, reproducible deployments
- Improved resource utilisation
- Cloud-native resilience

### 1.3 The Failure

During load testing as part of the migration validation process, the system was
subjected to **3x normal production load**. The system began failing.

**Symptoms observed via Datadog:**
- HTTP requests timing out under concurrent load
- Thread pool exhaustion errors appearing in application logs
- Response times degrading sharply as concurrent users increased
- System becoming completely unresponsive at sustained 3x load

**Root Cause Identified:**
Thread starvation — the .NET thread pool was being exhausted under concurrent load.
The most probable cause was synchronous blocking calls (`.Result` or `.Wait()`) within
async code paths, causing threads to block instead of yielding, consuming all available
thread pool capacity.

**Outcome:**
The load testing team did not sign off on the migration. The containerisation effort
was abandoned. The application remained on IIS on Windows servers.

### 1.4 The Core Gap — Reactive vs Proactive Observability

The team had Datadog capturing logs. However, Datadog was operating in a purely
**reactive** mode — it reported what had already happened.

By the time thread starvation appeared in Datadog logs, the system had already
collapsed. There was no mechanism to:

- Detect thread pool pressure **before** it reached critical levels
- Alert the team with **enough lead time** to intervene
- **Automatically diagnose** the probable root cause
- Provide **clear remediation steps** to on-call engineers

### 1.5 Problem Statement Summary

> *We need an intelligent observability platform that captures thread pool health
> metrics proactively on every request, visualises them in real time, raises early
> warnings before the system collapses, and automatically diagnoses root causes using
> AI — giving engineers a 15-second window to act instead of a post-mortem.*

---

## 2. Goals & Non-Goals

### 2.1 Goals

| ID | Goal | Success Criteria |
|----|------|-----------------|
| G1 | Detect thread pool exhaustion before system failure | Alert triggers at 80% exhaustion, well before 100% collapse |
| G2 | Real-time visibility into thread pool health | Grafana dashboard refreshes every 5 seconds |
| G3 | Automatically diagnose root cause using AI | Diagnosis endpoint returns structured root cause + recommendations |
| G4 | Demonstrate proper async patterns | Zero `.Result` or `.Wait()` calls in the codebase |
| G5 | Full stack containerised and reproducible | `docker-compose up --build` starts entire system in one command |
| G6 | Kubernetes-ready design | Health check endpoint, non-root container, retry logic |

### 2.2 Non-Goals

- This is **not** a full production AKS deployment — it runs locally in Docker
- This does **not** replace Datadog or other APM tools in production
- This does **not** include auto-scaling Kubernetes configuration (HPA)
- Load testing scripts to simulate 3x load are **not** included in this submission
- This is **not** a multi-tenant system — single application instance

---

## 3. Architecture Overview

### 3.1 High-Level Architecture

The system is composed of four containerised services orchestrated via Docker Compose,
designed to mirror what an AKS deployment would look like:

```
┌─────────────────────────────────────────────────────────────┐
│                    CLIENT / LOAD TEST                        │
└─────────────────────────────┬───────────────────────────────┘
                              │ HTTP Requests
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              .NET 10 Web API  (Port 8080)                    │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │  Create Task │  │  Search Task │  │  Move Task Status │  │
│  └──────────────┘  └──────────────┘  └──────────────────┘  │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │                 Metrics Middleware                      │ │
│  │   Thread count · Request duration · Exhaustion %       │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌──────────────────────┐  ┌─────────────────────────────┐ │
│  │  /metrics endpoint   │  │   /health endpoint           │ │
│  │  (Prometheus scrape) │  │   (Kubernetes probe)         │ │
│  └──────────────────────┘  └─────────────────────────────┘ │
└──────────┬──────────────────────────┬───────────────────────┘
           │ SQL Queries               │ Metrics scrape (5s)
           ▼                          ▼
┌──────────────────┐      ┌───────────────────────────────────┐
│   SQL Server     │      │   Prometheus  (Port 9090)          │
│   (Port 1433)    │      │   Metrics time-series storage      │
│   Task Data      │      └────────────────┬──────────────────┘
└──────────────────┘                       │
                                           ▼
                           ┌───────────────────────────────────┐
                           │   Grafana  (Port 3000)             │
                           │   Live dashboard — 8 panels        │
                           │   Auto-refresh every 5 seconds     │
                           └────────────────┬──────────────────┘
                                            │ Threshold ≥ 80%
                                            ▼
                           ┌───────────────────────────────────┐
                           │   AI Diagnosis Layer               │
                           │   (Claude API)                     │
                           │   Input:  Thread pool metrics      │
                           │   Output: Root cause + fix steps   │
                           └───────────────────────────────────┘
```

### 3.2 Three-Layer Design Philosophy

The architecture is deliberately structured in three layers, each addressing a
different aspect of the problem:

#### Layer 1 — Fix (Proper Async Implementation)
The root cause of the original failure was blocking async code. Layer 1 ensures
the API itself is correctly implemented — every I/O operation uses `async/await`,
no threads are blocked unnecessarily. This is the **prevention** layer.

#### Layer 2 — Detect (Proactive Metrics)
Even with correct async code, resource exhaustion can occur under extreme load.
Layer 2 captures thread pool state on every single request and makes it observable
via Prometheus and Grafana. This is the **early warning** layer — it gives the
team a 15-second window to act before collapse.

#### Layer 3 — Diagnose (AI-Powered Analysis)
When exhaustion crosses 80%, it is no longer sufficient to say "something is wrong."
Layer 3 sends current metrics to Claude AI and returns a human-readable diagnosis
with specific, actionable remediation steps. This is the **intelligence** layer.

### 3.3 Container Network

All four services run on a dedicated Docker bridge network `taxfiling-network`.
This mirrors AKS pod networking — services communicate via container names, not
localhost, which is how Kubernetes DNS resolution works.

```
taxfiling-api       → taxfiling-sqlserver  (connection string: Server=sqlserver)
taxfiling-prometheus → taxfiling-api       (scrape target: taxfiling-api:8080)
taxfiling-grafana   → taxfiling-prometheus  (data source: http://prometheus:9090)
```

### 3.4 Startup Dependencies

```
sqlserver  (starts first, health check confirms ready)
    ↓
taxfiling-api  (depends_on: sqlserver healthy)
    ↓
prometheus  (starts independently, scrapes api)
    ↓
grafana  (depends_on: prometheus)
```

This `depends_on` with `condition: service_healthy` ensures the API never starts
before SQL Server is ready — a common Kubernetes pod startup issue.

---

## 4. Component Design

### 4.1 TaskController

**Responsibility:** Handle all HTTP concerns for the tax filing workflow. Receive
requests, validate input, delegate to service layer, return appropriate HTTP responses.

**Location:** `Controllers/TaskController.cs`

**Endpoints:**

| Method | Route | Description | Response Codes |
|--------|-------|-------------|----------------|
| GET | `/api/task` | Retrieve all tasks | 200 |
| GET | `/api/task/{id}` | Retrieve task by ID | 200, 404 |
| GET | `/api/task/search` | Search by status/assignee/year | 200 |
| POST | `/api/task` | Create new task | 201, 400 |
| PATCH | `/api/task/{id}/status` | Move task to new status | 200, 400, 404 |

**Key design decisions:**

- **PATCH not PUT for status update** — PATCH semantics apply when updating a
  partial resource. Updating only the status field does not warrant replacing the
  entire resource (PUT). This is a deliberate REST API design choice.

- **201 Created with CreatedAtAction** — On successful task creation, the response
  includes a `Location` header pointing to the new resource. This is correct REST
  behaviour and makes the API self-discoverable.

- **ProducesResponseType attributes** — Every endpoint declares its possible response
  types. This makes the Swagger documentation accurate and complete, and allows
  clients to handle responses predictably.

- **Constructor injection** — The controller receives `ITaskService` via constructor
  injection, not `new TaskService()`. It depends on the abstraction, not the
  implementation.

### 4.2 TaskService

**Responsibility:** Contain all business logic for task management. Enforce
workflow rules. Communicate with the database via Entity Framework.

**Location:** `Services/TaskService.cs`

**Interface:** `ITaskService` — all controller dependencies are on the interface.

**State Machine — Valid Status Transitions:**

```
                 ┌─────────────┐
                 │   Pending   │
                 └──────┬──────┘
                        │
          ┌─────────────┼─────────────┐
          ▼             ▼             │
    ┌──────────┐   ┌──────────┐      │
    │Cancelled │   │InProgress│      │
    └──────────┘   └─────┬────┘      │
                         │           │
              ┌──────────┼───────┐   │
              ▼          ▼       │   │
        ┌──────────┐  ┌──────────────┐
        │Cancelled │  │ UnderReview  │
        └──────────┘  └──────┬───────┘
                             │
                    ┌────────┼────────┐
                    ▼                 ▼
             ┌──────────┐      ┌──────────┐
             │Completed │      │InProgress│ (sent back)
             └──────────┘      └──────────┘
```

Invalid transitions are rejected with a 400 Bad Request and a clear error message.
This prevents data corruption — a tax filing task cannot jump from Pending directly
to Completed, bypassing review.

**Key design decisions:**

- **`AsNoTracking()` on all read queries** — Entity Framework tracks changes to
  fetched objects by default, consuming memory. On read-only operations (GetAll,
  Search, GetById), tracking is unnecessary. Under high load, this reduces memory
  pressure significantly.

- **Structured logging** — Every operation logs with structured parameters
  (`{TaskId}`, `{From}`, `{To}`) rather than string interpolation. This makes
  logs queryable in production tools like Datadog or ELK.

- **Interface segregation** — `ITaskService` defines the contract. The controller
  never knows about `AppDbContext` — only the service does. This maintains a clean
  layer boundary.

### 4.3 MetricsMiddleware

**Responsibility:** Intercept every HTTP request, capture thread pool state before
and after processing, publish metrics to Prometheus, and log early warnings when
thresholds are crossed.

**Location:** `Middleware/MetricsMiddleware.cs`

**Metrics Published:**

| Metric Name | Type | Description | Alert Threshold |
|-------------|------|-------------|-----------------|
| `taxfiling_threadpool_exhaustion_percent` | Gauge | % of thread pool in use | 80% = Warning, 95% = Critical |
| `taxfiling_threadpool_available_worker_threads` | Gauge | Available threads remaining | — |
| `taxfiling_threadpool_threads_in_use` | Gauge | Threads currently occupied | — |
| `taxfiling_threadpool_max_worker_threads` | Gauge | Maximum configured threads | — |
| `taxfiling_threadpool_available_completion_port_threads` | Gauge | I/O completion threads | — |
| `taxfiling_active_requests` | Gauge | Requests being processed now | — |
| `taxfiling_request_duration_seconds` | Histogram | Request duration distribution | > 2s = Slow |
| `taxfiling_requests_total` | Counter | Total requests by method/path/status | — |
| `taxfiling_thread_starvation_events_total` | Counter | Times 80% threshold was crossed | Any = Investigate |

**Request Processing Flow:**

```
Request arrives
      │
      ▼
Snapshot thread pool state
(GetAvailableThreads, GetMaxThreads)
      │
      ▼
Calculate exhaustion %
((max - available) / max * 100)
      │
      ▼
Update Prometheus gauges
      │
      ▼
exhaustion ≥ 80%? ──Yes──► Log Warning + Increment starvation counter
      │
      ▼
exhaustion ≥ 95%? ──Yes──► Log Critical
      │
      ▼
Increment active_requests gauge
      │
      ▼
await _next(context)   ◄── Actual request processing happens here
      │
      ▼
Decrement active_requests
Record duration histogram
Increment request counter
      │
      ▼
duration > 2s? ──Yes──► Log slow request warning
```

**Why middleware and not an attribute or filter?**

Middleware wraps the *entire* request pipeline, including other middleware. An
action filter only wraps controller actions. By placing metrics in middleware, we
capture 100% of requests — including those that fail before reaching the controller
(e.g., authentication failures, routing errors). No request can escape measurement.

### 4.4 DiagnosisController

**Responsibility:** Provide an on-demand AI-powered diagnosis of current thread
pool health. Capture real-time metrics, send to Claude AI, return structured
diagnosis with root cause and recommendations.

**Location:** `Controllers/DiagnosisController.cs`

**Endpoint:** `POST /api/diagnosis/analyze`

**Processing Flow:**

```
POST /api/diagnosis/analyze
          │
          ▼
Capture ThreadPool state
(GetAvailableThreads, GetMaxThreads)
          │
          ▼
Build metrics snapshot object
          │
          ▼
Call Claude API with metrics + structured prompt
          │
    ┌─────┴─────┐
    │           │
  Success    Failure
    │           │
    ▼           ▼
Parse JSON   Rule-based
AI response  fallback
    │           │
    └─────┬─────┘
          │
          ▼
Return DiagnosisResult
{
  metrics: { ... },
  diagnosis: {
    healthStatus: "Healthy|Warning|Critical",
    rootCause: "...",
    recommendations: ["...", "...", "..."],
    riskIfIgnored: "..."
  },
  analyzedAt: "..."
}
```

**Three-tier fallback diagnosis:**

| Exhaustion Level | Health Status | Likely Root Cause |
|-----------------|---------------|-------------------|
| < 80% | Healthy | No issues detected |
| 80–94% | Warning | Blocking calls, slow queries, or insufficient ThreadPool config |
| ≥ 95% | Critical | Near-complete exhaustion — immediate action required |

**Why a fallback exists:**
The AI API is an external dependency. In production, external dependencies fail.
The system should never return an empty or error response when a fallback exists.
This follows the **graceful degradation** principle — a core Kubernetes resilience
pattern.

### 4.5 AppDbContext

**Responsibility:** Bridge between C# objects and SQL Server. Define schema
constraints, seed data, and retry configuration.

**Location:** `Data/AppDbContext.cs`

**Key configuration:**

```csharp
// Retry on transient failure — critical for Kubernetes
sqlOptions.EnableRetryOnFailure(
    maxRetryCount: 3,
    maxRetryDelay: TimeSpan.FromSeconds(5),
    errorNumbersToAdd: null);
```

**Why retry logic matters in Kubernetes:**
In Kubernetes, pods restart, network partitions occur briefly, and SQL Server may
not be immediately reachable after a pod reschedule. Without retry logic, these
transient events cause permanent request failures. With it, they are transparent
to the user.

**Auto-migration on startup:**

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}
```

The application creates and migrates its own schema on startup. No manual DBA
intervention, no missing tables — the application is self-sufficient. This is
essential for containerised deployments where the database may be fresh.

---

## 5. Data Model

### 5.1 TaskItem Entity

```
┌─────────────────────────────────────────────────────┐
│                    TaskItem                          │
├──────────────┬───────────────┬──────────────────────┤
│ Column       │ Type          │ Constraints           │
├──────────────┼───────────────┼──────────────────────┤
│ Id           │ int           │ PK, auto-increment    │
│ Title        │ nvarchar(200) │ Required              │
│ Description  │ nvarchar(max) │ Optional              │
│ Status       │ nvarchar(50)  │ Default: "Pending"    │
│ AssignedTo   │ nvarchar(max) │ Agent email           │
│ TaxYear      │ nvarchar(10)  │ e.g. "2024"           │
│ FilingType   │ nvarchar(50)  │ "Individual","Corp"   │
│ CreatedAt    │ datetime2     │ UTC timestamp          │
│ UpdatedAt    │ datetime2     │ Updated on change      │
└──────────────┴───────────────┴──────────────────────┘
```

### 5.2 Valid Status Values

```
Pending → InProgress → UnderReview → Completed
             ↓               ↓
          Cancelled       InProgress (returned for revision)
   ↓
Cancelled
```

### 5.3 Seed Data

Two records are seeded on first run to enable immediate testing:

| Id | Title | Status | Filing Type |
|----|-------|--------|-------------|
| 1 | File 1040 for John Smith | Pending | Individual |
| 2 | File 1120 for Acme Corp | InProgress | Corporate |

---

## 6. Technology Stack & Justification

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| API Framework | ASP.NET Core Web API | .NET 10 | Latest release, native async, excellent performance |
| Language | C# | 13 | Strong typing, modern async patterns |
| ORM | Entity Framework Core | 10.0 | Type-safe, async queries, migration support |
| Database | SQL Server | 2022 | Production parity — original system used SQL Server |
| Metrics Collection | Prometheus | Latest | Kubernetes-native, .NET client library, pull model |
| Metrics Visualisation | Grafana | Latest | Flexible dashboards, Prometheus native integration |
| Containerisation | Docker + Compose | Latest | Reproducible builds, mirrors Kubernetes deployment |
| API Documentation | Swagger / OpenAPI | Swashbuckle | Self-documenting, interactive testing |
| AI Diagnosis | Claude API (Anthropic) | claude-sonnet-4 | Structured JSON output, nuanced technical analysis |
| Source Control | Git + GitHub | — | Public repository for submission |

### Why Prometheus over Datadog for this design?

| Factor | Datadog | Prometheus + Grafana |
|--------|---------|---------------------|
| Cost | Paid, per-host pricing | Free, open source |
| .NET Integration | Generic APM agent | Native `prometheus-net` library |
| Thread pool metrics | Requires custom configuration | Built-in with our middleware |
| Kubernetes native | Yes (agent DaemonSet) | Yes (ServiceMonitor CRD) |
| Custom dashboards | Yes | Yes, more flexible |
| Self-hosted | No | Yes |

**Decision:** Prometheus + Grafana gives us more control over exactly what thread
pool metrics we capture and visualise, integrates natively with our .NET middleware,
and runs entirely within our container network without external dependencies.

---

## 7. Key Architectural Decisions & Trade-offs

### Decision 1: Middleware for Observability

**Context:** Needed to capture metrics on every request without modifying any
controller or service code.

**Options considered:**
- Action Filter (attribute-based)
- ASP.NET Core Middleware
- Background service polling

**Decision:** ASP.NET Core Middleware pipeline.

**Reasoning:**
- Middleware wraps the entire pipeline — including auth failures and routing errors
- Zero changes to business logic code — clean separation of concerns
- Execution order is explicit and deterministic
- Consistent with how ASP.NET Core is designed to handle cross-cutting concerns

**Trade-off:** Slightly more complex to unit test in isolation than an action
filter. Mitigated by the fact that middleware testing uses TestServer in integration
tests.

---

### Decision 2: AI Diagnosis with Rule-Based Fallback

**Context:** AI adds genuine value to diagnosis but introduces an external
dependency that can fail.

**Options considered:**
- AI only — fail if API unavailable
- Rules only — no AI
- AI with fallback to rules

**Decision:** AI primary, rule-based fallback.

**Reasoning:**
- External dependencies fail — this is a fundamental distributed systems reality
- A diagnosis with less nuance is better than no diagnosis
- Follows the graceful degradation principle
- System behaviour is predictable regardless of AI availability

**Trade-off:** The fallback diagnosis is less nuanced than AI analysis. A rule
cannot consider combinations of metrics the way AI can. Acceptable for a fallback.

---

### Decision 3: Scoped Lifetime for TaskService

**Context:** Choosing the right DI lifetime for the service that holds DbContext.

**Options considered:**
- Singleton (one instance for app lifetime)
- Scoped (one instance per HTTP request)
- Transient (new instance every injection)

**Decision:** Scoped.

**Reasoning:**
- `AppDbContext` is not thread-safe — it must never be shared across concurrent
  requests
- Scoped lifetime creates one DbContext per request and disposes it at request end
- This is Entity Framework's recommended lifetime
- Memory is reclaimed after each request — no accumulation

**Trade-off:** Slight instantiation overhead vs Singleton. Correctness requires
Scoped — a Singleton DbContext would cause concurrency bugs under load.

---

### Decision 4: Multi-Stage Docker Build

**Context:** Packaging the .NET 10 API as a container image.

**Options considered:**
- Single-stage build (SDK image as runtime)
- Multi-stage build (separate build and runtime stages)

**Decision:** Multi-stage build with three stages (build → publish → runtime).

**Reasoning:**
- SDK image is ~800MB. Runtime image is ~200MB. 4x size difference.
- Smaller images pull faster in Kubernetes — directly impacts pod startup time
- Build tools and source code do not exist in the production image
- Layer caching speeds up CI/CD — unchanged dependencies are cached

**Trade-off:** Slightly more complex Dockerfile. The size and security benefits
far outweigh the complexity cost.

---

### Decision 5: Static Seed Data Dates

**Context:** EF Core HasData() seeding requires static values.

**Problem:** Using `DateTime.UtcNow` in `HasData()` causes EF Core to generate a
new migration on every build because the value changes each time the model is
evaluated.

**Decision:** Use hardcoded `new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)`.

**Reasoning:** Seed data represents reference data, not live data. Static dates
are semantically correct for seed records. This eliminates spurious migrations.

---

### Decision 6: PATCH vs PUT for Status Update

**Context:** Designing the status transition endpoint.

**Decision:** `PATCH /api/task/{id}/status` with a `{ newStatus: "..." }` body.

**Reasoning:**
- The endpoint updates one field (status) of a resource, not the entire resource
- PUT semantics require sending the complete resource representation
- PATCH is semantically correct for partial updates
- The request body is minimal and clear — one field, one purpose

**Trade-off:** Some older HTTP clients have inconsistent PATCH support. In
practice, all modern clients support PATCH correctly.

---

## 8. Design Patterns Used

| Pattern | Where Applied | Why |
|---------|--------------|-----|
| **Repository / Service Pattern** | TaskService + ITaskService | Separates business logic from HTTP concerns |
| **Dependency Injection** | All controllers and services | Loose coupling, testability, SOLID-D |
| **State Machine** | Status transitions in TaskService | Enforces valid workflow at code level |
| **Middleware Pipeline** | MetricsMiddleware | Cross-cutting observability without polluting business logic |
| **Retry Pattern** | EF Core EnableRetryOnFailure | Handles transient failures in Kubernetes |
| **Fallback Pattern** | AI diagnosis fallback | Graceful degradation when external service unavailable |
| **Factory Pattern** | IHttpClientFactory | Manages HTTP client lifetimes, avoids socket exhaustion |
| **Multi-stage Build** | Dockerfile | Lean production images |
| **Health Check Pattern** | /health endpoint | Kubernetes liveness and readiness probes |

### SOLID Principles Applied

| Principle | Application |
|-----------|-------------|
| **S** — Single Responsibility | Controller handles HTTP only. Service handles business logic only. Middleware handles metrics only. |
| **O** — Open/Closed | New task types or statuses can be added without modifying existing transition logic |
| **L** — Liskov Substitution | `TaskService` can be replaced by any other `ITaskService` implementation |
| **I** — Interface Segregation | `ITaskService` exposes only what controllers need |
| **D** — Dependency Inversion | Controllers depend on `ITaskService` abstraction, not `TaskService` concrete class |

---

## 9. Failure Modes & Resilience

### 9.1 Thread Pool Exhaustion

| Stage | Detail |
|-------|--------|
| **Trigger** | Concurrent requests exhaust available thread pool threads |
| **Early Signal** | Exhaustion % climbs above 80% — visible on Grafana gauge |
| **Detection** | MetricsMiddleware logs Warning at 80%, Critical at 95% |
| **Response** | DiagnosisController returns AI-powered root cause and fixes |
| **Prevention** | Proper `async/await` throughout — no blocking `.Result` calls |
| **Recovery** | Fix blocking calls, redeploy; or scale horizontally |

### 9.2 SQL Server Unavailable

| Stage | Detail |
|-------|--------|
| **Trigger** | Network partition, SQL Server pod restart in Kubernetes |
| **Detection** | EF Core connection exception |
| **Response** | Auto-retry 3 times with 5-second delay (EnableRetryOnFailure) |
| **If persistent** | 503 Service Unavailable returned after retries exhausted |
| **Health check** | `/health` endpoint reports degraded — Kubernetes stops routing traffic |

### 9.3 AI API Unavailable

| Stage | Detail |
|-------|--------|
| **Trigger** | Claude API timeout, rate limit, or network failure |
| **Detection** | Try/catch in DiagnosisController |
| **Response** | Falls back to rule-based diagnosis automatically |
| **User impact** | Zero — diagnosis is always returned, just less nuanced |

### 9.4 Prometheus Scrape Failure

| Stage | Detail |
|-------|--------|
| **Trigger** | API container unreachable, /metrics endpoint down |
| **Detection** | Prometheus targets page shows scrape error |
| **Response** | Grafana panels show "No data" |
| **API impact** | None — metrics collection is non-blocking, separate from request processing |

### 9.5 Container Startup Order Failure

| Stage | Detail |
|-------|--------|
| **Trigger** | API starts before SQL Server is ready |
| **Detection** | `depends_on` with `condition: service_healthy` |
| **Response** | Docker Compose waits for SQL Server health check to pass before starting API |
| **Retry** | SQL Server health check retries 10 times with 10-second intervals |

---

## 10. Security Considerations

### 10.1 Container Security

- **Non-root user** — The API container runs as `appuser`, not root. Running as
  root in containers violates the principle of least privilege and is a common
  Kubernetes security policy violation.

- **Read-only filesystem** — The published application files are owned by appuser.
  No runtime writes to the application directory.

### 10.2 Secret Management

- **API keys not in source code** — The Anthropic API key is stored in
  `appsettings.json` which is excluded from Git via `.gitignore`. An
  `appsettings.Example.json` with placeholder values is committed instead.

- **Environment variable injection** — In Docker Compose, secrets are injected
  via environment variables (`${ANTHROPIC_API_KEY}`) loaded from a `.env` file
  that is also excluded from Git.

- **Production recommendation** — In a real Kubernetes deployment, secrets would
  be stored in Kubernetes Secrets or Azure Key Vault, mounted as environment
  variables at runtime.

### 10.3 Database Security

- **SA password strength** — The SQL Server SA password (`TaxFiling@123`) meets
  complexity requirements. In production, this would be rotated and stored in a
  secrets manager.

- **TrustServerCertificate** — Set to True for local development only. In
  production AKS, proper SSL certificates would be configured.

### 10.4 API Security (Not Implemented — Would Add)

- JWT Bearer authentication on all task endpoints
- Rate limiting middleware to prevent abuse
- Input validation and sanitisation beyond ModelState
- HTTPS enforcement with valid certificates

---

## 11. Scalability Considerations

### 11.1 What Scales Well

- **Stateless API** — The .NET API holds no in-memory state between requests.
  Multiple pod replicas can run simultaneously without coordination. This is the
  fundamental requirement for horizontal scaling in Kubernetes.

- **Connection pooling** — Entity Framework uses SQL Server connection pooling.
  Multiple API instances share a pool of database connections efficiently.

- **Prometheus scraping** — Prometheus can scrape multiple API pod instances using
  Kubernetes ServiceMonitor — no configuration changes needed per pod.

### 11.2 Bottlenecks at Scale

- **Single SQL Server instance** — The current design has one SQL Server container.
  At high scale, this becomes the bottleneck. Mitigation: read replicas, connection
  pooling configuration, query optimisation with indexes.

- **Thread pool limits** — .NET's thread pool has a maximum thread count. The
  entire point of this system is to detect when we approach that limit. Mitigation:
  proper async/await (already implemented), and horizontal scaling.

- **AI API rate limits** — Claude API has rate limits. If diagnosis is called
  frequently under sustained load, rate limiting could occur. Mitigation: cache
  diagnosis results for 30 seconds, implement circuit breaker.

### 11.3 Kubernetes Scaling Path

If deployed to AKS, the scaling path would be:

```
Current: 1 API pod, 1 SQL Server pod
    ↓
Near-term: 3 API pods behind a LoadBalancer Service
    ↓
Medium-term: Horizontal Pod Autoscaler on CPU metric
    ↓
Long-term: SQL Server read replicas + CQRS pattern
```

---

## 12. What I Would Improve With More Time

| Priority | Improvement | Reason |
|----------|-------------|--------|
| High | **k6 load test scripts** | Demonstrate thread pool exhaustion live — show the gauge climbing to red |
| High | **Prometheus AlertManager** | Automated Slack/email/PagerDuty on 80% threshold breach |
| High | **Kubernetes manifests** | Full AKS deployment YAML — Deployment, Service, Ingress, HPA |
| Medium | **OpenTelemetry distributed tracing** | Trace individual requests across middleware, service, and database layers |
| Medium | **Circuit breaker (Polly)** | Shed load gracefully when database is slow, prevent cascading failures |
| Medium | **Integration test suite** | Automated tests for all 6 endpoints + state machine transitions |
| Medium | **JWT authentication** | Secure task endpoints — only authenticated agents can manage tasks |
| Low | **Horizontal Pod Autoscaler config** | Auto-scale API pods on CPU/memory metrics |
| Low | **Grafana alert rules** | Visual alerts directly in Grafana without AlertManager |
| Low | **Structured log shipping** | Ship structured logs to Elasticsearch or Datadog from containers |

---

## 13. Honest Self-Assessment

### What Works Well

- All 6 API endpoints function correctly and return proper HTTP status codes
- Thread pool metrics are captured on every request and visible in Grafana in real time
- AI diagnosis returns structured, actionable output with root cause and recommendations
- Full stack starts with a single `docker-compose up --build` command
- Proper `async/await` throughout — no blocking `.Result` or `.Wait()` calls
- State machine correctly rejects invalid task status transitions
- Health check endpoint ready for Kubernetes liveness probes
- Multi-stage Docker build produces a lean runtime image
- Non-root container user follows Kubernetes security best practices

### What Is Incomplete

- **No live load test** — The most compelling demo would show the Grafana gauge
  climbing toward red under 3x load. Without k6 scripts, the dashboard shows a
  healthy system. This is the biggest gap in the submission.

- **AI diagnosis uses fallback** — Without a configured Anthropic API key, the
  diagnosis endpoint returns the rule-based fallback. The AI path works but requires
  a valid key.

- **Not deployed to AKS** — The system runs locally in Docker, not on actual
  Kubernetes. The design is Kubernetes-ready (health checks, non-root, retry logic)
  but the gap between Docker Compose and AKS is real.

- **No AlertManager** — Prometheus can detect threshold breaches but there is no
  automated notification. A complete production system would page the on-call
  engineer.

### What I Learned Building This

- The fundamental difference between **reactive logging** (Datadog capturing what
  already happened) and **proactive observability** (Prometheus capturing what is
  happening right now)

- Why **middleware order** in ASP.NET Core matters architecturally — metrics
  middleware must come before routing to capture 100% of requests

- How **Prometheus scraping** works with .NET — the pull model, the `/metrics`
  endpoint, and how Grafana queries time-series data

- Why **thread pool exhaustion** happens in .NET — the relationship between
  blocking calls, thread pool limits, and async/await as the correct solution

- How **Docker networking** works — container name resolution, bridge networks,
  and how it mirrors Kubernetes pod-to-pod communication

- The importance of **graceful degradation** — designing systems that degrade
  predictably rather than failing catastrophically

---

*Built with .NET 10 · Docker · Prometheus · Grafana · Claude AI · SQL Server*  
*GitHub: https://github.com/shivaraj17cm/TaxFilingObservability*
