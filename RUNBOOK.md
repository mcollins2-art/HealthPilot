# HealthPilot Backend Runbook

## 1) Start service locally
```powershell
cd backend
dotnet restore
dotnet ef database update --project .\src\HealthPilot.Api\HealthPilot.Api.csproj --startup-project .\src\HealthPilot.Api\HealthPilot.Api.csproj
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project .\src\HealthPilot.Api\HealthPilot.Api.csproj --urls "http://localhost:5000"
```

## 2) Health checks
```powershell
Invoke-RestMethod -Uri "http://localhost:5000/health"
Invoke-RestMethod -Uri "http://localhost:5000/health/ready"
```

## 3) Common operations
### 3.1 Estimate request
```powershell
$headers = @{ "X-API-Key" = "<key>" }
$body = @{
  zipCode = "10001"
  insurer = "Aetna"
  cptCode = "70553"
  deductibleRemaining = 1200
  coinsurancePercent = 20
  copay = 50
  oopMaxRemaining = 3000
  copayAppliesBeforeDeductible = $true
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5000/estimate" -Method Post -Headers $headers -ContentType "application/json" -Body $body
```

### 3.2 Ingestion import
```powershell
$headers = @{ "X-API-Key" = "<ingestion-key>" }
$body = @{ filePath = "C:\\data\\cms-pricing.csv" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:5000/ingestion/import" -Method Post -Headers $headers -ContentType "application/json" -Body $body
```

Operational notes:
- `GET /ingestion/checkpoints` is read-only (it no longer deletes expired checkpoints).
- Use `POST /ingestion/checkpoints/cleanup` for explicit retention cleanup actions.
- Configure `Ingestion:MaxAttempts` to tune queued job retry attempts (minimum `1`, default `2`).

### 3.3 Pricing lifecycle cleanup (safe by default)
```powershell
$headers = @{ "X-API-Key" = "<ingestion-key>" }

# Preview only (default dry run)
Invoke-RestMethod -Uri "http://localhost:5000/ingestion/pricing/cleanup?retentionDays=365" -Method Post -Headers $headers

# Execute delete (explicit confirm required)
Invoke-RestMethod -Uri "http://localhost:5000/ingestion/pricing/cleanup?retentionDays=365&dryRun=false&confirm=true" -Method Post -Headers $headers
```

## 4) Troubleshooting
### 4.1 `500` with `relation "estimate_audit_logs" does not exist`
Cause: DB schema behind migrations.

Fix:
```powershell
cd backend
dotnet ef database update --project .\src\HealthPilot.Api\HealthPilot.Api.csproj --startup-project .\src\HealthPilot.Api\HealthPilot.Api.csproj
```

### 4.2 `429 Too Many Requests`
Cause: rate limiter threshold exceeded.

Fix options:
- Run in `Development` environment with higher `RateLimiting` settings.
- Reduce benchmark concurrency/request volume.

### 4.3 `403` on valid key
Cause: API key scope missing for endpoint.

Required scopes:
- `/estimate`: `estimate:read`
- `/ingestion/import`: `ingestion:write`

### 4.4 Async jobs appear stuck in `queued`
Cause: service restarted while jobs were queued in DB.

Behavior:
- On startup, HealthPilot re-enqueues persisted `queued` ingestion jobs automatically.
- If database is unavailable at startup, recovery is skipped and logged as a warning; jobs remain in DB and will recover on next successful startup.

## 5) Validation commands
### Unit tests
```powershell
cd backend
dotnet test .\tests\HealthPilot.Api.Tests\HealthPilot.Api.Tests.csproj
```

### Focused security/ingestion regressions
```powershell
cd backend
dotnet test .\tests\HealthPilot.Api.Tests\HealthPilot.Api.Tests.csproj --filter "FullyQualifiedName~(ApiKeyAuthenticationMiddlewareTests|IngestionImportEndpointsTests|IngestionCheckpointEndpointsTests|PricingIngestionPipelineTests)"
```

### Perf smoke
```powershell
cd backend
.\scripts\loadtest\Run-EstimatePerfSmoke.ps1 -BaseUrl "http://localhost:5000" -TotalRequests 500 -Concurrency 50 -OutFile ".\scripts\loadtest\last-estimate-perf.json"
```

For cold-start-safe smoke runs, add warm-up requests:

```powershell
.\scripts\loadtest\Run-EstimatePerfSmoke.ps1 -BaseUrl "http://localhost:5000" -WarmupRequests 5 -TotalRequests 200 -Concurrency 20 -OutFile ".\scripts\loadtest\last-estimate-perf.json"
```

### Staging migration rehearsal
```powershell
cd backend
.\scripts\Test-MigrationStaging.ps1
```

## 6) Security checklist (pilot)
- `Security:ApiKey` OR `Security:ApiKeys` configured in non-development.
- Distinct scoped keys per client/integration.
- No production secrets committed to source control.
- `Ingestion:AllowedRootPath` configured where ingestion endpoint is enabled.
- Swagger/OpenAPI auth bypass only allowed in development environment.
