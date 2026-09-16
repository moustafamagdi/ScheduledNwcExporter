# Product & Operations Phase 2 Plan

Branch: `product/operations-phase-2`

Base: `hardening/reliability-phase-1`

Scope selected for this phase: 1, 2, 3, 4, 5, 6, 7, 8, 10, and 12 from the product/operations review. Automated testing/CI and BIM export-scope profiles are intentionally excluded. The generic "health check" concept is also excluded from item 12; only concrete operational safeguards are implemented.

## A. Cloud Explorer search and filtering
- Implement recursive filtering of the loaded ACC hierarchy.
- Add Revit-file-only filtering and match-count feedback.
- Auto-expand ancestors of matching nodes and keep folders visible when they contain matches.

## B. Export session history
- Give every batch a Session ID and persist recent session summaries.
- Store trigger source, start/end time, duration, counts, per-model result, output path, freshness before/after, and errors.
- Add a Session History window.

## C. Advanced diagnostics
- Expand diagnostics with add-in/API version, scheduler state, queue state, config/log/report locations, NWC exporter availability, cloud sign-in state, and per-job source/output checks.

## D. Logging hardening
- Add Session ID and Job ID correlation to log entries.
- Add log retention and bounded storage cleanup.
- Keep one shared logger per Revit session.

## E. Export summary/report
- Generate a CSV report after every completed batch.
- Add an Open Last Report action.
- Include per-model outcome, duration, output, freshness before/after, and error details.

## F. Windows notifications
- Notify on scheduled completion and on any failed batch.
- Avoid duplicate success notifications for normal manual runs.

## G. Main queue UX polish
- Add Select All / Clear run-selection actions.
- Show selected model count in the Run Now action.
- Improve disabled-job visibility and keep the existing workflow unchanged.

## H. Dashboard/statistics
- Add a lightweight dashboard window showing Current, Needs Export, Failed, Enabled, last batch, last scheduled batch, and average model export duration from recent history.

## I. Configuration versioning/migration
- Add an explicit configuration schema version.
- Migrate older configuration files in memory before use.
- Include schema version in exported settings packages.

## J. Operational safeguards (without generic health check)
- Validate local source existence before starting a batch.
- Validate output directory accessibility/writability before starting a batch.
- Warn for low local disk free space.
- Keep the existing Revit-context NWC exporter availability check.
- Block scheduled runs on concrete preflight errors; log warnings without inventing generic health scoring.

## Validation gate
1. Existing config loads without losing jobs/settings.
2. Cloud Explorer search visibly filters loaded nodes and can restrict to RVT files.
3. Manual and scheduled batches create history records and CSV reports.
4. Logs contain Session and Job correlation IDs during a batch.
5. Old logs are cleaned according to retention policy without touching current logs.
6. Dashboard values match the queue/history state.
7. Select All/Clear and Run count behave correctly without modifying persistent `IsEnabled`.
8. Inaccessible output paths or missing local sources block a batch before Revit opens a model.
9. Low disk space produces a warning, not an arbitrary failure.
10. Scheduled completion/failure notification is shown without blocking Revit.
