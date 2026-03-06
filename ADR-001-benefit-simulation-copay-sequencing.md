# ADR-001: Benefit simulation copay sequencing

## Status
Accepted

## Context
Benefit simulation needs a deterministic sequence for applying copay, deductible, and coinsurance so estimate behavior is consistent across API calls and audits.

## Decision
`BenefitSimulationService` applies:

1. If `copayAppliesBeforeDeductible = true`:
   - Apply copay first (capped by negotiated rate)
   - Apply remaining deductible
   - Apply coinsurance on remaining allowed amount
2. If `copayAppliesBeforeDeductible = false`:
   - Apply deductible first
   - Apply coinsurance on remainder
   - Add copay after deductible path
3. Cap patient responsibility to `oopMaxRemaining`.
4. Round outputs using `MonetaryPolicy.Round`.

## Consequences
- Behavior is explicit and audit-friendly.
- API callers can control plan sequencing via `copayAppliesBeforeDeductible`.
- When pricing is unavailable, current pricing selection fallback uses `0` negotiated rate and still returns a deterministic estimate.
