# Phase 2 Commercial Readiness Audit and Implementation Plan

This document expands Phase 2 into an execution-ready commercial plan for:
- Outpatient surgery support
- Employer pilot readiness
- Denial probability modeling

It is organized as repeated audit → implement → validate loops so gaps can be closed safely and incrementally.

## 1) Current-state audit summary

### 1.1 Capabilities already in place
- API security foundation: scoped API keys and endpoint scoping are already implemented.
- Operational foundation: ingestion jobs support async status tracking and replay workflows.
- Cost estimate foundation: deductible/coinsurance/copay simulation is implemented.
- Audit foundation: estimate logging and traceability infrastructure already exists.

### 1.2 Commercial gaps blocking Phase 2
- Outpatient surgery is not yet represented as a first-class commercial workflow with explicit acceptance targets.
- Employer pilot success metrics and launch gates are not yet codified in one place.
- Denial probability modeling lacks a defined MVP scope, evaluation criteria, and rollout guardrails.
- Compliance, disclaimers, and launch-readiness checks are not consolidated into a single operational checklist.

## 2) Target state definition (100% commercially ready for Phase 2)

Phase 2 is considered commercially ready only when all items below are true:
- A pilot employer can run outpatient surgery estimates in production with documented SLAs.
- Pilot stakeholders have agreed KPI targets, baseline values, and reporting cadence.
- Denial-risk outputs are available for targeted scenarios with clear intervention guidance.
- Compliance and audit controls are documented and tested in pre-launch rehearsal.
- Go/no-go decision can be made from objective launch-gate evidence.

## 3) Audit loop #1: Outpatient surgery

### 3.1 Scope to implement
- Prioritize top outpatient surgery CPTs for pilot launch (start with a narrow set).
- Require explicit facility context (ASC vs hospital outpatient) in estimate workflows.
- Support multi-component surgery pricing logic where available (facility/professional).

### 3.2 Readiness checklist
- [ ] CPT shortlist finalized with employer and clinical advisor input
- [ ] Source pricing files mapped to chosen CPT shortlist
- [ ] Coverage report generated (in-network, out-of-network, unknown rate scenarios)
- [ ] Manual benchmark review completed for representative ZIP + insurer samples
- [ ] Fallback behavior defined when data is incomplete

### 3.3 Exit criteria
- Estimate accuracy target and data-coverage thresholds are achieved for scoped CPTs.
- Error-handling behavior for missing/ambiguous pricing is tested and documented.

## 4) Audit loop #2: Employer pilot

### 4.1 Scope to implement
- Define pilot account onboarding flow (security, access scopes, tenant isolation).
- Define employer-facing reporting package for adoption and savings.
- Define pilot operating rhythm (weekly review, issue escalation, SLA ownership).

### 4.2 Readiness checklist
- [ ] Employer profile and pilot goals are documented
- [ ] Tenant-scoped key strategy and rotation plan are approved
- [ ] Pilot reporting schema is defined (adoption, savings, volatility, denial-risk)
- [ ] Stakeholder communications template and escalation path are documented
- [ ] Pilot contract assumptions are captured in one source of truth

### 4.3 Exit criteria
- Employer stakeholders can self-serve pilot reports and understand KPI movement.
- Security and tenant isolation controls are verified in pre-production rehearsal.

## 5) Audit loop #3: Denial probability modeling

### 5.1 MVP model scope
- Start with denial-risk classification for a small set of high-volume outpatient scenarios.
- Restrict output to interpretable risk bands and top contributing factors.
- Pair each high-risk score with recommended next actions (e.g., prior-auth verification).

### 5.2 Readiness checklist
- [ ] Training/evaluation dataset contract is defined and versioned
- [ ] Feature list for first model version is approved
- [ ] Baseline model quality target is set (precision/recall or calibration)
- [ ] Monitoring plan exists (drift, false-positive review, retrain cadence)
- [ ] Human-review override path is documented

### 5.3 Exit criteria
- Model meets baseline quality threshold on holdout evaluation.
- Risk output format and intervention recommendations are approved by operations.

## 6) Commercial KPI scorecard (pilot)

Track weekly from pilot start date:

- **Adoption**
  - Eligible members reached
  - Estimates generated
  - Repeat usage rate
- **Financial impact**
  - Estimated member savings
  - Estimated employer claims volatility reduction
  - Out-of-network avoidance rate
- **Care navigation quality**
  - Time-to-estimate
  - Incomplete-estimate rate
  - High-risk denial intervention completion rate
- **Reliability and operations**
  - API availability
  - P95 estimate latency
  - Incident count and mean time to recovery

## 7) Compliance and risk controls for Phase 2

Must be explicitly reviewed before launch:
- Clear estimator disclaimers for non-binding price estimates and coverage variance.
- Tenant isolation and least-privilege API scopes for employer pilot access.
- Auditable trace IDs on estimate events and operational actions.
- Documented data-retention approach and sensitive-field handling standards.
- Denial-model transparency notes for pilot users (what score means and limits).

## 8) Go/No-Go launch checklist

All boxes must be checked for launch:
- [ ] Outpatient surgery scoped data is loaded and validated
- [ ] Pilot employer onboarding and access controls are fully tested
- [ ] Denial-risk MVP meets agreed baseline quality threshold
- [ ] Runbook rehearsal completed (ingestion retry, replay, incident workflow)
- [ ] Compliance/disclaimer review is signed off
- [ ] KPI dashboard baseline snapshot is captured
- [ ] Exec sponsor and pilot owner approve launch packet

If any launch gate fails, remain in Phase 2 hardening mode and re-run the relevant audit loop.

## 9) Implementation cadence (repeat until launch-ready)

Use this cadence continuously:
1. Audit one track (surgery, employer, or denial model)
2. Implement the smallest high-impact fix
3. Validate with targeted checks
4. Update checklist status and remaining gaps
5. Repeat with next highest commercial risk

This loop should continue until all launch gates in section 8 are complete.
