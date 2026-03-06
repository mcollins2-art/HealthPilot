# Phased Execution Plan

## Phase 0 (1–2 months)
- Build CPT normalization DB
- Parse 10 hospital machine files
- Build a basic deductible simulator
- No frontend needed yet

## Phase 1 (3–5 months)
- Add 3 insurers’ imaging policies
- Launch MRI + CT estimator in 1–2 states
- Test pricing accuracy manually

## Phase 2
- Add outpatient surgery
- Add employer pilot
- Start denial probability modeling

### Phase 2 commercial outcome target
Deliver a production-safe pilot in which at least one employer can use outpatient surgery cost estimates with measurable reduction in avoidable spend and denial risk.

### Phase 2 delivery tracks
1. **Outpatient surgery track**
   - Ingest ASC/outpatient surgery pricing files by effective date
   - Add surgery-oriented estimate workflows (facility + professional fee components)
   - Validate pricing quality against manually reviewed benchmark cases
2. **Employer pilot track**
   - Onboard pilot employer with tenant-scoped access controls
   - Enable employer-level reporting for estimate adoption and savings
   - Define pilot success criteria and operating cadence
3. **Denial probability track**
   - Capture denial-relevant features at estimate time
   - Build first denial-risk baseline model with explainable risk factors
   - Route high-risk estimates into intervention recommendations

### Phase 2 commercial-readiness gates (must pass before launch)
- Data quality gate: surgery estimate error rate and coverage meet target thresholds
- Compliance gate: pilot disclaimers, audit logging, and access controls are verified
- Operations gate: runbook, on-call process, and replay/recovery steps are tested
- Value gate: pilot KPI baseline and target deltas are agreed with employer stakeholders

See `/PHASE_2_COMMERCIAL_READINESS.md` for the full audit, implementation backlog, KPI scorecard, and go/no-go checklist.

## Phase 3
- Expand nationwide
- Add specialty drugs
- Sell to self-insured employers

## 💰 Where This Becomes Very Valuable
Self-insured employers lose money due to:
- Poor navigation
- Out-of-network surprises
- Unnecessary delays
- Denial cycles

If your model reduces claims volatility by even 3–5%, that’s millions at scale.

## ⚠️ What Will Kill This
- Trying to cover every procedure immediately
- Underestimating data cleaning complexity
- Skipping compliance and disclaimers
- Building frontend before infrastructure

## 🧱 Your Moat Strategy
Over time you accumulate:
- National structured pricing dataset
- Insurer policy database
- Denial pattern data
- Out-of-pocket prediction model

That’s hard to replicate.
