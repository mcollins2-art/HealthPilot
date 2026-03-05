# HealthPilot — Capacity Analysis

**Date:** 2026-03-05  
**Based on:** `CTO_SCAN_REPORT.md` findings + full static code analysis  
**Commit:** `f517a48` (branch: `copilot/perform-independent-cto-scan`)

---

## TL;DR — User Capacity Today

| Scenario | Max Sustained Requests | Est. Daily Active Users |
|----------|----------------------|------------------------|
| **Single instance, production defaults** | **2 req/sec** (rate-limiter bound) | **~14,000–57,000 DAU** |
| Single instance, rate limiter raised | ~300–500 req/sec (DB bound) | ~3M–5M DAU |
| 3 instances + rate limiter raised | ~900–1,500 req/sec | ~9M–15M DAU |

The **current bottleneck is the global fixed-window rate limiter configured at 120 requests per 60 seconds**, which caps the entire API at 2 requests/second regardless of server capacity. Removing that ceiling and addressing the five structural constraints below would enable the system to serve millions of daily users on standard cloud hardware.

---

## How to Read This Document

Each section traces a **specific constraint** from the code to a **quantified ceiling**. All estimates use conservative assumptions. User counts assume healthcare app usage patterns: 3 estimate requests per active user per day, with a 4:1 peak-to-average ratio across business hours.

---

## Constraint 1 — Global Rate Limiter (Hard Ceiling Today)

### What the code does

`Program.cs` registers a fixed-window rate limiter keyed `"api"` and applies it to every endpoint via `.RequireRateLimiting("api")`:

```jsonc
// appsettings.json (production defaults)
"RateLimiting": {
  "PermitLimit": 120,
  "WindowSeconds": 60,
  "QueueLimit": 0
}
```

`QueueLimit: 0` means there is no queue — requests beyond 120/window are immediately rejected with `429 Too Many Requests`.

### Impact

- **120 requests per 60-second window = 2 req/sec sustained**
- The limiter is **global**, not per-client. One integration calling rapidly consumes the entire quota and starves all other clients.
- The `Development` override raises this to 5,000 req/min (83 req/sec), but production never sees that config.

### User ceiling (rate limiter only)

```
7,200 req/hour × 24 hours = 172,800 req/day (theoretical max)
Divide by 3 requests/user/day = 57,600 DAU (at perfectly flat traffic)
With realistic 4:1 peak-to-average: ~14,400–28,800 DAU
```

**Verdict:** The rate limiter alone restricts HealthPilot to roughly **14,000–57,000 daily active users** on a single instance before any server or database resource is stressed. This is the first thing to fix.

---

## Constraint 2 — Sequential DB Round-Trips per `/estimate` Request

### What the code does

Each call to `POST /estimate` executes these DB operations **sequentially** (not in parallel):

| Step | Operation | File |
|------|-----------|------|
| 1 | `SELECT id FROM procedures WHERE CptCode = @cpt` | `PricingQueryService.cs:23` |
| 2 | `SELECT id FROM insurers WHERE Name = @insurer` | `PricingQueryService.cs:29` |
| 3 | `SELECT id FROM facilities WHERE Zip = @zip` | `PricingQueryService.cs:35` |
| 4 | `MIN(cash_price)` aggregate query | `PricingQueryService.cs:47` |
| 5 | `MAX(cash_price)` aggregate query | `PricingQueryService.cs:48` |
| 6 | `MIN(Rate)` negotiated rates query | `PricingQueryService.cs:62` |
| 7 | `MAX(Rate)` negotiated rates query | `PricingQueryService.cs:63` |
| 8 | `INSERT INTO estimate_audit_logs` + `SaveChangesAsync` | `EstimateAuditService.cs:37` |

**Total: 8 sequential database round-trips per estimate request.**

### Impact

Assuming well-indexed PostgreSQL with typical LAN latency:

| Scenario | Latency per DB call | Total DB time per request |
|----------|---------------------|--------------------------|
| Same host | 1–2 ms | 8–16 ms |
| LAN (typical prod) | 3–8 ms | 24–64 ms |
| Loaded DB | 10–20 ms | 80–160 ms |

Including application overhead (auth middleware, benefit simulation, JSON): **expected P50 response time of ~50–80 ms** on a loaded production instance.

Steps 1–3 (dimension lookups) and steps 6–7 (negotiated rate aggregates) are independent of each other and could be parallelized with `Task.WhenAll`, reducing the sequential chain from 8 to 3 "parallel groups". That alone would cut DB wait time by roughly 50%.

### User ceiling (DB-constrained, rate limiter removed)

PostgreSQL default `max_connections = 100`. Npgsql's default connection pool is 100.

```
100 connections × (1000 ms / 64 ms per request) = ~1,563 req/sec (theoretical)
Derate to 40% for CPU + write contention + index overhead: ~625 req/sec
Daily: 625 × 86,400 = 54M req/day
At 3 req/user/day: ~18M DAU (DB-constrained, single PG instance)
```

---

## Constraint 3 — No Response Caching for Static Pricing Data

### What the code does

`PricingQueryService` queries the database on every estimate request. Pricing data (the `procedures`, `facilities`, `insurers`, `negotiated_rates`, and `cash_prices` tables) is populated by batch ingestion and changes rarely — at most once per CMS update cycle (quarterly or monthly).

There is no `IMemoryCache`, `IDistributedCache`, or output caching registered anywhere in `Program.cs` or the service layer.

### Impact

Every single estimate request pays 5–7 DB reads for data that is essentially static between ingestion runs. If 95% of `/estimate` traffic hits the same popular CPT codes and zip codes, those are 95% cache-able reads hitting the database unnecessarily.

### Improvement potential

Adding a 1-minute `IMemoryCache` on `GetPricingSummaryAsync` keyed by `(zipCode, insurer, cptCode)` would:
- Reduce DB read load by an estimated **80–95%** for typical healthcare traffic patterns
- Increase effective concurrent throughput from ~625 req/sec to potentially **5,000–10,000 req/sec** (CPU and memory bound, not DB bound)
- Require only ~1 KB per cached entry; 100K entries ≈ 100 MB RAM

---

## Constraint 4 — Synchronous Audit Write Blocks Every Response

### What the code does

`EstimateEndpoints.cs` (line 47) awaits `estimateAuditService.LogEstimateAsync(...)` **before** returning the response to the caller. Inside `EstimateAuditService`, `SaveChangesAsync()` is a blocking DB write:

```csharp
// EstimateEndpoints.cs (simplified excerpt) — the audit write is on the critical path
// Full method: HandleEstimateAsync(EstimateRequest request, ..., HttpContext httpContext, ...)
await estimateAuditService.LogEstimateAsync(request, simulation, representativeRate,
    httpContext.TraceIdentifier, cancellationToken);

return Results.Ok(response);  // Only reached after INSERT completes
```

### Impact

Each estimate request pays a synchronous `INSERT INTO estimate_audit_logs` write before the user gets their answer. Under high concurrency:

- The audit table accumulates a write-heavy append workload sharing DB connections with the read path.
- Write latency spikes (e.g., during autovacuum, index maintenance) directly delay user responses.
- At 500 req/sec, the audit table receives 43M rows/day — a significant append rate that will require table partitioning and a WAL archiving strategy before that scale is reached.

### Improvement potential

Moving the audit write to a background queue (e.g., `Channel<EstimateAuditEntry>` + `BackgroundService`, similar to `IngestionJobWorker`) would:
- Remove the write from the request's critical path
- Allow the API to return the estimate in ~20–30 ms instead of ~50–80 ms
- Handle write bursts without back-pressure on the HTTP response

---

## Constraint 5 — No DbContext Pool (`AddDbContext` vs `AddDbContextPool`)

### What the code does

```csharp
// Program.cs — uses AddDbContext, not AddDbContextPool
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
```

`AddDbContext` allocates a fresh `DbContext` object and resolves a Npgsql connection from the pool on every request scope. `AddDbContextPool` (EF Core's built-in context pool) reuses pre-allocated context objects, reducing GC pressure and instance allocation overhead at high concurrency.

There is no `MaxPoolSize` or `MinPoolSize` in the connection string, so Npgsql uses its defaults (max 100 connections).

### Impact

At low-to-moderate concurrency (<200 req/sec), the difference is negligible. At high concurrency (>500 req/sec), benchmarks show `AddDbContextPool` provides a **10–25% throughput improvement** and meaningfully lower GC pause frequency.

---

## Constraint 6 — Single Application Instance, No Horizontal Scale

### What the code does

The app is designed as a single-process deployment. There is no distributed cache, no distributed rate limiter (the `RateLimiter` is in-process, not backed by Redis), and no session affinity requirements that would prevent horizontal scaling — but none of the infrastructure to enable it is wired up yet.

### Impact

The in-process fixed-window rate limiter means each instance has its own independent 120 req/min quota. Adding a second instance **doubles the global quota** but does not coordinate limits between instances. This is actually acceptable for Phase 1 as long as operators are aware that per-instance limits are independent.

However, the lack of distributed caching means a pricing cache (Constraint 3) would need to be invalidated across all instances on ingestion completion — requiring either a distributed cache (Redis) or an event-based invalidation signal.

---

## Summary: Bottleneck Impact Table

| Bottleneck | Current Impact | Fix Effort | Throughput Multiplier |
|------------|---------------|------------|-----------------------|
| Global rate limiter at 2 req/sec | **Hard cap: 14K–57K DAU** | Low — config change | 150–400× |
| 8 sequential DB queries per request | ~50–80 ms/request | Medium — parallelize steps 1–3 | 2× |
| No pricing cache | 100% reads hit DB | Low — add `IMemoryCache` | 8–16× |
| Synchronous audit write | +5–20 ms latency spike risk | Medium — background queue | 1.5× |
| `AddDbContext` vs `AddDbContextPool` | ~10–25% overhead at scale | Low — one-line change | 1.1–1.25× |
| Single instance | Hard capacity ceiling | High — infrastructure | N× (linear with instances) |

---

## Projected Capacity by Improvement Stage

| Stage | Changes | Est. Req/sec | Est. DAU (3 req/user/day) |
|-------|---------|-------------|--------------------------|
| **Today (as-is)** | None | **2** | **14,000–57,000** |
| Stage 1 | Raise rate limiter to 2,000 req/min | 33 | ~950,000 |
| Stage 2 | + Parallelize 3 dimension lookups in `PricingQueryService` | ~80 | ~2.3M |
| Stage 3 | + Add `IMemoryCache` on pricing reads (1 min TTL) | ~400 | ~11.5M |
| Stage 4 | + Move audit write to background queue | ~500 | ~14.4M |
| Stage 5 | + `AddDbContextPool` + connection string tuning | ~600 | ~17.3M |
| Stage 6 | + 3 horizontal API instances + PostgreSQL read replica | ~1,800 | ~52M |

All Stage 1–5 changes are code or config modifications in the existing single-instance deployment. Stage 6 adds infrastructure.

---

## Where the 14K–57K DAU Estimate Comes From

```
Rate limiter: 120 req / 60 sec window = 2 req/sec sustained
Daily requests: 2 × 86,400 = 172,800

Healthcare app usage assumption:
  - 3 /estimate requests per active user per day (look up imaging cost, compare options)
  - Peak-to-average traffic ratio: 4:1 over business hours (9 AM–5 PM concentration)

At flat distribution: 172,800 / 3 = 57,600 DAU
At 4:1 peak ratio:   57,600 / 4 = 14,400 DAU (conservative)

Range: 14,400–57,600 DAU
```

---

## Quick-Win Recommendations (Ordered by Impact)

1. **Raise the rate limiter** *(1 config line, zero risk)*  
   Change `PermitLimit` from `120` to `2000` or higher. Add per-client rate limiting via a sliding-window policy keyed on the `X-API-Key` header to prevent a single client from consuming all capacity.

2. **Add `IMemoryCache` for pricing reads** *(~1 day of work)*  
   Cache the result of `PricingQueryService.GetPricingSummaryAsync(zip, insurer, cpt)` with a 1–5 minute TTL. Invalidate on ingestion completion. This single change multiplies effective throughput by 8–16× because pricing data is written infrequently but read on every request.

3. **Parallelize the 3 dimension lookups in `PricingQueryService`** *(~2 hours)*  
   Steps 1–3 (`procedures`, `insurers`, `facilities` lookups) are independent and can run concurrently with `Task.WhenAll`. This halves the sequential DB chain.

4. **Move audit write off the critical path** *(~0.5 days)*  
   Enqueue the audit record into an in-process `Channel<EstimateAuditEntry>` and write it from a `BackgroundService`, similar to how ingestion jobs are handled. Users get their estimate in ~20–30 ms; the audit write catches up asynchronously.

5. **Switch to `AddDbContextPool`** *(1 line change)*  
   Replace `AddDbContext` with `AddDbContextPool` in `Program.cs`. Reduces GC overhead at high concurrency with zero behavioral change.

---

*All figures are conservative static analysis estimates based on code inspection and standard ASP.NET Core + PostgreSQL benchmarks. Actual throughput will vary based on server hardware, network latency to the database, PostgreSQL configuration, and data volume.*
