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
- Legacy `Security:ApiKey` defaults to `estimate:read` only. To grant additional scopes during migration, set `Security:LegacyKeyScopes`.
- Endpoint scopes:
  - `/estimate` requires `estimate:read`
  - `/ingestion/import` requires `ingestion:write`
- Rate limiting is enabled and config-driven via `RateLimiting`.

Example scoped key config:

```json
"Security": {
  "ApiKeyHeader": "X-API-Key",
  "LegacyKeyScopes": ["estimate:read"],
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

## Data Pipeline Components (C#)

The repository includes C# data pipeline components under `src/HealthPilot.Api/data_pipeline/`:

- `downloader.cs` - resilient HTTP downloader with streaming writes, progress logging, and retry handling.
- `link_discoverer.cs` - finds machine-readable `.csv`/`.json` transparency file links from hospital transparency pages.
- `hospital_transparency_ingestion.cs` - orchestrates discovery + download into `data/` and parses CPT-coded negotiated/cash prices.
- `parser.cs` - streaming CSV/JSON parser that extracts:
  - `hospital_name`
  - `payer`
  - `procedure_code` (CPT)
  - `procedure_description`
  - `negotiated_rate`
  - `cash_price`
  - `location`
- `normalizer.cs` - CPT normalization and procedure categorization helpers.
- `loader.cs` - batched loader that creates `providers`, `procedures`, and `rates` tables, handles duplicate upserts, and logs ingestion errors.

Sample hospital transparency data files are committed under `data/`:

- `mount_sinai_sample.csv`
- `nyu_langone_sample.csv`
- `hackensack_meridian_sample.csv`

Run focused tests for these components with:

```powershell
dotnet test tests/HealthPilot.Api.Tests/HealthPilot.Api.Tests.csproj --filter "DataPipelineComponentsTests"
```

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
