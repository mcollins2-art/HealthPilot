# HealthPilot Backend (C#)

ASP.NET Core + EF Core + PostgreSQL backend for imaging pricing and benefit simulation.

This is the canonical backend for HealthPilot.

## Phase 1 Scope

- MRI and CT imaging pricing
- CPT-normalized schema
- CMS pricing ingestion pipeline structure
- Benefit simulation engine
- `POST /estimate` endpoint

## Run

```powershell
cd backend
dotnet restore
dotnet ef database update --project .\src\HealthPilot.Api\HealthPilot.Api.csproj --startup-project .\src\HealthPilot.Api\HealthPilot.Api.csproj
dotnet run --project .\src\HealthPilot.Api\HealthPilot.Api.csproj --urls "http://localhost:5050"
```

## Security

- API key header: `X-API-Key` (configurable via `Security:ApiKeyHeader`).
- Non-development startup requires either `Security:ApiKey` or `Security:ApiKeys`.
- Scoped API keys are supported via `Security:ApiKeys`.
- Endpoint scopes:
  - `/estimate` requires `estimate:read`
  - `/ingestion/import` requires `ingestion:write`
- Rate limiting is enabled and config-driven via `RateLimiting`.

Example scoped key config:

```json
"Security": {
  "ApiKeyHeader": "X-API-Key",
  "ApiKeys": [
    { "name": "estimate-client", "key": "replace-estimate-key", "scopes": ["estimate:read"] },
    { "name": "ingestion-worker", "key": "replace-ingestion-key", "scopes": ["ingestion:write"] }
  ]
}
```

## Endpoints

- `GET /health` liveness probe.
- `GET /health/ready` readiness probe (DB + pending migrations).
- `POST /estimate` estimate endpoint.
- `POST /ingestion/import` ingestion endpoint (sync or async job queue via `async: true`).
- `GET /ingestion/jobs/{jobId}` ingestion job lifecycle status.
- `POST /ingestion/jobs/{jobId}/replay` deterministic replay enqueue.

## Ingestion Scale

- Imports use streaming batch persistence (`Ingestion:BatchSize`, default `5000`) for both CSV and JSON files to reduce peak memory pressure.
- Checkpoint/resume is supported for batched imports with durable DB-backed checkpoints; use `resumeFromCheckpoint` in request payload.

## Load Testing

- Perf smoke script: `scripts/loadtest/Run-EstimatePerfSmoke.ps1`.

Example:

```powershell
cd backend
.\scripts\loadtest\Run-EstimatePerfSmoke.ps1 -BaseUrl "http://localhost:5000" -TotalRequests 500 -Concurrency 50 -OutFile ".\scripts\loadtest\last-estimate-perf.json"
```

## Operations Docs

- API reference: `API_DOCUMENTATION.md`
- Runbook: `RUNBOOK.md`

## Structure

```text
backend/
  src/
    HealthPilot.Api/
      Models/
      Data/
      Dtos/
      Services/
      Ingestion/
      Endpoints/
```
