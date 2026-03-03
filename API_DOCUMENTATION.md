# HealthPilot Backend API Documentation

## Base URL
- Local default: `http://localhost:5000`

## Authentication
- Header: `X-API-Key`
- Security modes:
  - Legacy single key: `Security:ApiKey`
  - Scoped key list: `Security:ApiKeys`

### Scoped key example
```json
"Security": {
  "ApiKeyHeader": "X-API-Key",
  "ApiKeys": [
    { "name": "estimate-client", "key": "replace-estimate-key", "scopes": ["estimate:read"] },
    { "name": "ingestion-worker", "key": "replace-ingestion-key", "scopes": ["ingestion:write"] }
  ]
}
```

## Health
### `GET /health`
- Purpose: liveness probe.
- Auth: not required.
- Success: `200 { "status": "ok" }`

### `GET /health/ready`
- Purpose: readiness probe (DB connectivity + pending migrations).
- Auth: not required.
- Success: `200 { "status": "ready" }`
- Failure: `503` when DB is unavailable or migrations are pending.

## Estimate
### `POST /estimate`
- Purpose: estimate patient out-of-pocket and insurer payment.
- Auth scope: `estimate:read`
- Rate limiting policy: `api`

Request body:
```json
{
  "zipCode": "10001",
  "insurer": "Aetna",
  "cptCode": "70553",
  "deductibleRemaining": 1200,
  "coinsurancePercent": 20,
  "copay": 50,
  "oopMaxRemaining": 3000,
  "copayAppliesBeforeDeductible": true
}
```

Response body:
```json
{
  "negotiatedRateRange": "$900.00 - $1400.00",
  "estimatedOutOfPocket": "$440.00",
  "cashPriceRange": "$700.00 - $1000.00",
  "insurerPaymentEstimate": "$560.00"
}
```

## Ingestion
### `POST /ingestion/import`
- Purpose: parse and persist pricing file records.
- Auth scope: `ingestion:write`
- Rate limiting policy: `api`
- Allowed extensions: `.csv`, `.json`
- Optional path guard: `Ingestion:AllowedRootPath`
- Batch processing: enabled with `Ingestion:BatchSize` (default `5000`)
- Streaming behavior: both CSV and JSON imports are processed as streaming batches to reduce peak memory usage
- Checkpoint/resume: import progress is checkpointed and can resume from last processed row
- Checkpoint config: `Ingestion:CheckpointDirectory` (optional)
- Max file size: 1 GB

Request body:
```json
{
  "filePath": "C:\\path\\to\\pricing-file.csv",
  "batchSize": 5000,
  "resumeFromCheckpoint": true
}
```

### `GET /ingestion/checkpoints/{checkpointKey}`
- Purpose: fetch current checkpoint status for resumable ingestion tracking.
- Auth scope: `ingestion:write`

Response body:
```json
{
  "checkpointKey": "...",
  "filePath": "C:\\path\\to\\file.json",
  "batchSize": 5000,
  "rowsProcessed": 10000,
  "status": "completed",
  "updatedAtUtc": "2026-03-03T00:00:00Z"
}
```

### `GET /ingestion/checkpoints?limit=20`
- Purpose: list recent checkpoints for operations visibility.
- Auth scope: `ingestion:write`
- Query param: `limit` (optional, default `20`, min `1`, max `200`)
- Retention: expired checkpoints are automatically cleaned based on `Ingestion:CheckpointRetentionHours`

Response body:
```json
{
  "count": 2,
  "items": [
    {
      "checkpointKey": "...",
      "filePath": "C:\\path\\to\\file.json",
      "batchSize": 5000,
      "rowsProcessed": 10000,
      "status": "completed",
      "updatedAtUtc": "2026-03-03T00:00:00Z"
    }
  ]
}
```

### `POST /ingestion/checkpoints/cleanup?retentionHours=168`
- Purpose: manually delete expired checkpoint files.
- Auth scope: `ingestion:write`
- Query param: `retentionHours` (optional, minimum `1`; defaults to `Ingestion:CheckpointRetentionHours`)

Response body:
```json
{
  "deleted": 3,
  "retentionHours": 168,
  "traceId": "..."
}
```

Success response includes persistence metrics:
```json
{
  "status": "completed",
  "recordsReceived": 100000,
  "recordsSkipped": 0,
  "proceduresCreated": 250,
  "facilitiesCreated": 1000,
  "insurersCreated": 40,
  "negotiatedRatesUpserted": 100000,
  "cashPricesUpserted": 100000,
  "checkpointKey": "...",
  "rowsResumedFrom": 0,
  "rowsProcessed": 100000,
  "completed": true,
  "traceId": "..."
}
```

## Error model
- `401`: missing or invalid API key.
- `403`: API key lacks endpoint scope.
- `429`: rate limiter rejection.
- `400`: validation/input errors.
- `500`: unhandled server error with trace id.
