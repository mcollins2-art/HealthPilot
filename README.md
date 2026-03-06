# HealthPilot Backend (C#)

HealthPilot is an ASP.NET Core + EF Core + PostgreSQL healthcare pricing platform for out-of-pocket cost estimation.

## Production-MVP Architecture

- **Data ingestion layer**: streaming CSV/JSON ingestion (`/ingestion/import`) with checkpoint/resume support.
- **ETL pipeline**: parser + normalization + persistence (`Ingestion/`, `Services/PricingPersistenceService.cs`).
- **Procedure cost database**: normalized schema for procedures, providers, insurers, negotiated and cash rates.
- **Insurance estimation engine**: benefit simulation + representative pricing selection.
- **API layer**: estimate, ingestion, health, procedures, and providers endpoints.
- **Frontend-ready API contract**: stable JSON responses optimized for direct client consumption.

## Supported Procedures

- MRI, CT, X-ray, and lab/blood-test CPT workflows are supported through CPT-normalized ingestion and lookup.

## Run

```powershell
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
- `/estimate-cost` requires `estimate:read`
- `/procedures` requires `estimate:read`
- `/providers` requires `estimate:read`
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
- `POST /estimate-cost` estimate endpoint alias with same contract.
- `GET /procedures` procedure catalog lookup.
- `GET /providers` provider catalog lookup.
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

## Data Source Download Script

Use the streaming downloader for public transparency files (hospital machine-readable files, insurer files, and CMS datasets):

```bash
python scripts/data/download_transparency_data.py \
  --url "https://example.org/hospital-mrf.json" \
  --url "https://example.org/insurer-transparency.csv" \
  --out-dir data/raw \
  --manifest data/raw/manifest.json
```

## Deployment

Containerized deployment is included:

```bash
docker compose up --build
```

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
