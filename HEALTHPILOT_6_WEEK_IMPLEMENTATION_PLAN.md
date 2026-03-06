# HealthPilot: 6-Week Implementation Plan

**Goal:** Ship complete, shippable product using synthetic data. No hospital data needed.

**Timeline:** 6 weeks solo, ~225 hours, ship by Week 6 Friday.

---

## WEEK 1: Synthetic Data Generation (40 hours)

### Monday-Tuesday: Insurer + Negotiated Rates (16 hours)

**File:** `src/HealthPilot.Api/Scripts/GenerateSyntheticData.cs`

```csharp
public class SyntheticDataGenerator
{
    public async Task GenerateAllAsync(AppDbContext db)
    {
        // Create 3 insurers
        var insurers = new[] {
            new Insurer { Name = "Aetna" },
            new Insurer { Name = "United Health" },
            new Insurer { Name = "Cigna" }
        };
        db.Insurers.AddRange(insurers);
        await db.SaveChangesAsync();

        // Generate negotiated rates: facility × procedure × insurer
        var random = new Random(42);
        var procedures = db.Procedures.Where(p => p.CptCode.StartsWith("70")).ToList();
        var facilities = db.Facilities.Take(500).ToList();

        foreach (var insurer in insurers)
        foreach (var facility in facilities)
        foreach (var proc in procedures)
        {
            var medicareRate = GetMedicareRate(proc.CptCode);
            var variance = random.Next(80, 120) / 100m;

            db.NegotiatedRates.Add(new NegotiatedRate
            {
                InsurerId = insurer.Id,
                FacilityId = facility.Id,
                ProcedureId = proc.Id,
                Rate = medicareRate * variance,
                LastUpdated = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }
}
```

**Deliverable:** 3 insurers, 15,000 negotiated rates

---

### Wednesday-Thursday: CMS Data (16 hours)

Load 1,000 facilities + cash prices from public CMS MRF data.

```bash
# Download from CMS
# https://www.cms.gov/priorities/key-initiatives/promoting-interoperability-in-healthcare

# Load into database via CmsDataLoader
# Result: 1,000 facilities, 100,000+ cash prices
```

**Deliverable:** Database ready for API calls

---

### Friday: Testing (8 hours)

```csharp
[Fact]
public async Task EstimateEndpoint_WithSyntheticData_ReturnsValidEstimate()
{
    var request = new EstimateRequest
    {
        ZipCode = "10001",
        Insurer = "Aetna",
        CptCode = "70553",
        DeductibleRemaining = 1200,
        CoinsurancePercent = 20,
        Copay = 50,
        OopMaxRemaining = 3000,
        CopayAppliesBeforeDeductible = true
    };

    var response = await client.PostAsJsonAsync("/estimate", request);
    response.StatusCode.Should().Be(200);

    var result = await response.Content.ReadAsAsync<EstimateResponse>();
    result.NegotiatedRateMin.Should().BeGreaterThan(0);
}
```

**End of Week 1:** ✅ Database loaded, API works with data

---

## WEEK 2: API Documentation (35 hours)

### Monday-Tuesday: Enhanced EstimateResponse (12 hours)

```csharp
public class EstimateResponse
{
    // Existing fields
    public decimal? NegotiatedRateMin { get; set; }
    public decimal EstimatedOutOfPocket { get; set; }

    // NEW: Metadata
    public int MatchedFacilityCount { get; set; }
    public string FacilityName { get; set; }
    public string InsurerName { get; set; }
    public DateTime DataAsOfDate { get; set; }
    public bool IsEstimateValid { get; set; }
    public string ValidationMessage { get; set; }
    public string SummaryText { get; set; }
}
```

**Deliverable:** Richer response with metadata

---

### Wednesday-Thursday: Documentation (16 hours)

**Files to create:**

1. **API_INTEGRATION_GUIDE.md** (2000 words)
   - Request/response examples
   - Field definitions
   - Error codes
   - cURL, Python, C# examples

2. **HOSPITAL_DATA_ONBOARDING.md** (1500 words)
   - Step 1: Export your data
   - Step 2: Validate
   - Step 3: Upload via API
   - Step 4: Test
   - Troubleshooting

3. **CODE_EXAMPLES/** folder
   - estimate_curl.sh
   - estimate.py
   - EstimateClient.cs

**Deliverable:** Hospitals can integrate in <1 day

---

### Friday: Swagger & Examples (7 hours)

- Update Swagger docs with example requests/responses
- Create 20 test scenarios (different insurers, ages, deductibles)
- Verify all code examples run

**End of Week 2:** ✅ 100+ pages documentation, 3 code examples

---

## WEEK 3: Data Validation (40 hours)

### Monday-Wednesday: Validation Framework (24 hours)

```csharp
// File: src/HealthPilot.Api/Services/PricingDataValidator.cs

public class PricingDataValidator
{
    public ValidationResult Validate(StructuredPricingRecord record)
    {
        var errors = new List<string>();

        if (record.Price <= 0)
            errors.Add("Price must be > 0");
        if (record.Price > 1_000_000)
            errors.Add("Price unreasonably high");
        if (string.IsNullOrWhiteSpace(record.CptCode))
            errors.Add("CPT code required");
        if (!IsValidCpt(record.CptCode))
            errors.Add($"Invalid CPT: {record.CptCode}");

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}
```

Integrate into `PricingPersistenceService`:

```csharp
foreach (var record in records)
{
    var validation = validator.Validate(record);
    if (!validation.IsValid)
    {
        result.RecordsRejected++;
        result.RejectionReasons.AddOrUpdate(validation.Errors[0], 1, (k, v) => v + 1);
        continue;
    }
    // Process record
}
```

**Deliverable:** Hospital uploads messy data, gets clear error report

---

### Thursday-Friday: Error Reporting (16 hours)

Hospital gets JSON response:

```json
{
  "status": "completed_with_errors",
  "recordsProcessed": 5234,
  "recordsAccepted": 5222,
  "recordsRejected": 12,
  "rejectionReasons": {
    "invalid_price": 8,
    "missing_cpt": 3
  }
}
```

Create Python validation script for hospitals:

```python
# scripts/validate_hospital_data.py
def validate_file(filepath):
    with open(filepath) as f:
        reader = csv.DictReader(f)
        for i, row in enumerate(reader):
            if not is_valid_cpt(row['cpt_code']):
                print(f"Row {i}: Invalid CPT")
            if float(row['price']) <= 0:
                print(f"Row {i}: Price must be > 0")
```

**End of Week 3:** ✅ Validation framework, error reporting, Python script

---

## WEEK 4: Compliance & SLA (35 hours)

### Monday-Tuesday: Legal Templates (16 hours)

Create 3 files in `legal/` folder:

1. **HIPAA_BAA_Template.md** (500 words)
   - Data types, permitted uses, safeguards, breach notification
   - 2-3 pages, simple language

2. **DPA_Template.md** (300 words)
   - Data processing terms, retention, subprocessors
   - GDPR/CCPA compliance

3. **ESTIMATE_DISCLAIMER.md** (200 words)
   - Pricing estimates are informational only
   - Actual costs may differ due to...
   - No liability clause

**Deliverable:** Legal templates ready to customize

---

### Wednesday: SLA & Runbooks (12 hours)

**SLA_DRAFT.md:**
```
API Availability: 99.5%
Response Time (p95): <500ms
Data Backup: Daily
Support: 24-hour email, 4-hour critical
```

**HOSPITAL_SUPPORT_RUNBOOK.md:**
- Q: Estimates are off by 20%? → Debug checklist
- Q: Upload failed? → Check CPT codes
- Q: API is slow? → Check rate limits
- Escalation: support@healthpilot.com

**Deliverable:** Professional SLA + support framework

---

### Thursday-Friday: Monitoring (7 hours)

```csharp
public class OperationalMetricsService
{
    public class Metrics
    {
        public DateTime Timestamp { get; set; }
        public double UptimePercent { get; set; }
        public double AvgLatencyMs { get; set; }
        public double P95LatencyMs { get; set; }
        public double ErrorRate { get; set; }
    }
}
```

Log metrics hourly to `metrics.jsonl`. Create `scripts/show_metrics.py` to display.

**End of Week 4:** ✅ HIPAA BAA, DPA, SLA, hospital support docs, metrics

---

## WEEK 5: Testing & Validation (40 hours)

### Monday-Tuesday: Accuracy Testing (16 hours)

```csharp
public class EstimateAccuracyTests
{
    private readonly (EstimateRequest, decimal Expected)[] _testCases = new[]
    {
        (new EstimateRequest { ZipCode = "10001", Insurer = "Aetna", CptCode = "70553", ... }, 450m),
        (new EstimateRequest { ZipCode = "60601", Insurer = "United", CptCode = "70480", ... }, 600m),
        // ... 98 more scenarios
    };

    [Theory]
    [MemberData(nameof(GetTestCases))]
    public async Task Estimate_AllScenarios_Accurate(EstimateRequest req, decimal expected)
    {
        var response = await client.PostAsJsonAsync("/estimate", req);
        var result = await response.Content.ReadAsAsync<EstimateResponse>();

        var error = Math.Abs(result.EstimatedOutOfPocket - expected) / expected;
        error.Should().BeLessThan(0.05m); // Within 5%
    }
}
```

**Goal:** 100 scenarios, 98% accuracy

---

### Wednesday-Thursday: Load Testing (16 hours)

```bash
# Run existing perf smoke test with higher concurrency
.\scripts\loadtest\Run-EstimatePerfSmoke.ps1 `
    -WarmupRequests 100 `
    -TotalRequests 10000 `
    -Concurrency 200 `
    -OutFile "loadtest_results.json"
```

**Target results:**
- Error rate: <1%
- P95 latency: <500ms
- Throughput: 1,000+ req/s

Create `PERFORMANCE.md` report.

---

### Friday: Security Audit (8 hours)

**SECURITY_CHECKLIST.md:**
- [ ] API key authentication ✓
- [ ] Scoped keys (estimate:read, ingestion:write) ✓
- [ ] Rate limiting ✓
- [ ] Request/response logging ✓
- [ ] Input validation ✓
- [ ] SQL injection prevention (EF Core) ✓

**HIPAA_READINESS.md:**
- [ ] BAA template ✓
- [ ] Data encryption (TLS + AES-256) ✓
- [ ] Access control ✓
- [ ] Audit logging ✓
- [ ] Breach notification process ✓

**End of Week 5:** ✅ 100 test scenarios pass, 10,000 load test passes, security audit complete

---

## WEEK 6: Packaging & Pitch (35 hours)

### Monday-Tuesday: API Playground (12 hours)

Create simple HTML at `static/playground.html`:

```html
<input id="zipCode" placeholder="10001">
<select id="insurer">
  <option>Aetna</option>
  <option>United Health</option>
  <option>Cigna</option>
</select>
<button onclick="submitRequest()">Get Estimate</button>
<pre id="response"></pre>

<script>
async function submitRequest() {
  const response = await fetch('/estimate', {
    method: 'POST',
    headers: { 'X-API-Key': 'demo-key' },
    body: JSON.stringify({
      zipCode: document.getElementById('zipCode').value,
      insurer: document.getElementById('insurer').value,
      ...
    })
  });
  document.getElementById('response').textContent =
    JSON.stringify(await response.json(), null, 2);
}
</script>
```

**Deliverable:** Hospitals click link, try API, see it works in 30 seconds

---

### Wednesday: Pitch Materials (12 hours)

**PITCH_DECK.md** (5 slides to convert to PDF):
1. **Problem:** Patients don't know costs
2. **Solution:** Real-time estimates via API
3. **How:** 3-step integration
4. **Proof:** 98% accuracy, 1,000 req/s, <500ms latency
5. **Let's go:** 2–3 weeks to production

**PRODUCT_ONE_PAGER.md** (1 page):
- What we do
- Why it matters
- Key metrics (accuracy, scale, uptime)
- Integration timeline
- Support & pricing

**HOSPITAL_ROI.md:**
```
Current: 150 billing disputes/month × $500 = $75,000/month
With HealthPilot: 75 disputes × $500 = $37,500/month
Savings: $450,000/year
Payback: <1 month
```

---

### Thursday-Friday: Hospital Package (11 hours)

```
HealthPilot-Hospital-Package/
├── README.md (start here)
├── QUICK_START.md
├── API_INTEGRATION_GUIDE.md
├── HOSPITAL_DATA_ONBOARDING.md
├── SLA.md
├── HIPAA_BAA_Template.md
├── ESTIMATE_DISCLAIMER.md
├── PERFORMANCE.md
├── CODE_EXAMPLES/ (cURL, Python, C#)
├── SAMPLE_DATA/ (test requests)
└── FAQ.md
```

**Hospital-Facing README:**
```markdown
# Welcome to HealthPilot

## In 5 Minutes
1. Try API playground: http://demo.healthpilot.com/playground
2. Read QUICK_START.md
3. Email: support@healthpilot.com

## Integration Timeline
- Week 1: Review docs, sign agreement, upload data
- Week 2: Test with sample patients
- Week 3: Debug, go live

## Key Metrics
✓ Accuracy: 98%
✓ Uptime: 99.5%
✓ Speed: <500ms p95
✓ Scale: 1,000+ req/s
```

**Deliverable:** Professional, complete, ready to send to hospitals

---

## End of Week 6: Ship-Ready Product

✅ **API**
- 3 insurers × 1,000 facilities × realistic pricing
- Accurate benefit simulation
- Data quality validation
- Async ingestion pipeline

✅ **Performance**
- 98% accuracy (100 test scenarios)
- 1,000+ req/s throughput
- <500ms latency (p95)
- 99.5% uptime SLA

✅ **Documentation**
- 100+ pages
- Integration guide + data onboarding
- 3 code examples
- Troubleshooting + FAQ

✅ **Compliance**
- HIPAA BAA template
- DPA template
- Estimate disclaimer
- Security checklist + HIPAA readiness

✅ **Pitch Materials**
- API Playground (try it live)
- Pitch deck (5 slides)
- 1-page summary
- Hospital ROI calculator

---

## Week 7+: Go Pitch Hospitals

With this package:
- Hospitals click one link, try API, see it works
- They read integration guide, understand timeline (2–3 weeks)
- They see your SLA & uptime guarantees
- They say "Yes, let's do a pilot"

**Result:** You don't ask hospitals to be a pilot. They ask to join you.

---

## Getting Started Now

**Monday, Week 1:**
1. Copy `SyntheticDataGenerator.cs` code above
2. Create script to load 3 insurers
3. Generate negotiated rates (facility × procedure × insurer)
4. Validate database has data
5. Test `/estimate` endpoint

**Each day:** Follow the plan, ship deliverable by EOD

**Week 6 Friday:** Have complete, shippable product

**Total effort:** ~225 hours solo development

Go. 🚀
