# Product & Operations Phase 2 Plan

Branch: `product/operations-phase-2`

Base: `hardening/reliability-phase-1`

Scope selected for this phase: 1, 2, 3, 4, 5, 6, 7, 8, 10, and 12 from the product/operations review. Automated testing/CI and BIM export-scope profiles are intentionally excluded. The generic "health check" concept is also excluded from item 12; only concrete operational safeguards are implemented.

## A. Cloud Explorer search and filtering
- [x] Implement recursive filtering of the loaded ACC hierarchy.
- [x] Add Revit-file-only filtering and match-count feedback.
- [x] Auto-expand ancestors of matching loaded nodes and keep folders visible when they contain matches.

## B. Export session history
- [x] Give every batch a Session ID and persist recent session summaries.
- [x] Store trigger source, start/end time, duration, counts, per-model result, output path, freshness before/after, and errors.
- [x] Add a Session History window.

## C. Advanced diagnostics
- [x] Expand diagnostics with add-in/API version, scheduler state, queue state, config/log/report locations, NWC exporter availability, cloud sign-in state, and per-job source/output checks.

## D. Logging hardening
- [x] Add Session ID and Job ID correlation to log entries.
- [x] Add 30-day / 100-file / 100-MB bounded log retention cleanup.
- [x] Keep one shared logger per Revit session.

## E. Export summary/report
- [x] Generate a CSV report after every completed batch.
- [x] Add an Open Last Report action and report access from Session History.
- [x] Include per-model outcome, duration, output, freshness before/after, and error details.

## F. Windows notifications
- [x] Notify on scheduled completion and on any failed batch.
- [x] Avoid duplicate success notifications for normal manual runs.

## G. Main queue UX polish
- [x] Add Select All / Clear run-selection actions.
- [x] Show selected model count and include the count in Run Now.
- [x] Visually de-emphasize persistently disabled jobs without changing the workflow.

## H. Dashboard/statistics
- [x] Add a lightweight dashboard showing Current, Needs Export, Failed, Enabled, last batch, last scheduled batch, and average successful model export duration from recent history.

## I. Configuration versioning/migration
- [x] Add an explicit schema version (`v2`).
- [x] Migrate older configuration shape before `ConfigurationManager` loads it.
- [x] Persist an authoritative `config.version` sidecar so normal legacy-compatible config serialization cannot erase the schema state.
- [x] Stamp `config.json` with `ConfigVersion` opportunistically while keeping older JSON readers compatible.

## J. Operational safeguards (without generic health check)
- [x] Validate local source existence before starting a batch.
- [x] Validate output directory accessibility/writability before starting a batch.
- [x] Warn for low local disk free space.
- [x] Keep the existing Revit-context NWC exporter availability check before model processing.
- [x] Block scheduled runs on concrete preflight errors; log warnings without inventing generic health scoring.
- [x] Block selected ACC jobs before opening when there is no Autodesk sign-in session.

## Required Revit 2024 validation before merge
1. Existing config loads without losing jobs/settings and Diagnostics reports schema v2.
2. Cloud Explorer search visibly filters loaded nodes and can restrict to RVT files.
3. Manual and scheduled batches create history records and CSV reports.
4. Logs contain Session and Job correlation IDs during a batch.
5. Old logs are cleaned according to retention policy without touching the current log.
6. Dashboard values match the queue/history state.
7. Select All/Clear and Run count behave correctly without modifying persistent `IsEnabled`.
8. Inaccessible output paths or missing local sources block a batch before Revit opens a model.
9. Low disk space produces a warning, not an arbitrary failure.
10. Scheduled completion/failure notification is shown without blocking Revit.
11. The existing NWC-exporter-missing safeguard still stops a batch before any model is opened.
