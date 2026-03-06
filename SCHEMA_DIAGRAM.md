# Pricing schema diagram

```text
Procedure (Id, CptCode, ...)
   | 1
   |----< NegotiatedRate >----| 1
   |                          |
Facility (Id, Zip, ...)    Insurer (Id, Name, ...)

Procedure (Id, CptCode, ...)
   | 1
   |----< CashPrice >----| 1
                      Facility (Id, Zip, ...)
```

Primary relationships:
- `NegotiatedRate` joins `Procedure`, `Facility`, and `Insurer` (`ProcedureId`, `FacilityId`, `InsurerId`).
- `CashPrice` joins `Procedure` and `Facility` (`ProcedureId`, `FacilityId`).
