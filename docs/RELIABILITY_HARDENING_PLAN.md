# Reliability Hardening Plan

Branch: `hardening/reliability-phase-1`

Base: `enhancements/unattended-export-safety`

This branch is intentionally limited to hardening items 1–5. Automated tests/CI and broader product polish remain a separate follow-up phase after this branch has passed Revit 2024 validation.

## Phase 1 — Reliability hardening

### 1. Correct scheduler and job-state semantics

- [x] Make modern schedule slots authoritative; use the legacy hour/minute only when no modern slots exist.
- [x] Distinguish a real NWC export from `OverwritePolicy = Skip`.
- [x] Never advance freshness when an existing NWC is merely retained.
- [x] Separate persistent job eligibility (`IsEnabled`) from transient current-run selection (`IsSelectedForRun`).
- [x] Scheduled runs ignore transient manual selection and evaluate enabled jobs from freshness directly.

Validation gate:
- A modern slot must not also trigger the legacy schedule.
- A skipped-existing output must remain stale when the source/settings require a new export.
- Smart selection must never re-enable a persistently disabled job.

### 2. Deterministic freshness evaluator

- [x] Add a verified export-state snapshot separate from editable job configuration.
- [x] Record a snapshot only after a non-empty NWC is actually written.
- [x] Compare local RVTs using the live source-file modification timestamp.
- [x] Compare ACC jobs using the latest known tip version ID where available.
- [x] Include export settings and export-scope policy in a SHA-256 fingerprint.
- [x] Verify that the recorded NWC still exists and is non-empty.
- [x] Refresh ACC metadata before a scheduled run when the manager window is closed.
- [x] Treat failed/unavailable ACC metadata as unverified rather than Current.

Migration behavior:
- Existing jobs from older builds have no verified export snapshot. They are intentionally classified as `Needs Export` until one successful export is performed by the hardening build.

Validation gate:
- Modifying the RVT, ACC tip version, relevant export settings, export-scope revision, or deleting the NWC must make the job require export.
- An unchanged source/settings/output combination must become Current after a verified export.

### 3. Non-blocking retry orchestration

- [x] Remove retry `Thread.Sleep` from `JobProcessor`.
- [x] Remove temporary-copy `Thread.Sleep` retries from the Revit execution path.
- [x] Perform one Revit attempt per `ExternalEvent` execution.
- [x] Use a one-shot `DispatcherTimer` between attempts, then raise the external event again.
- [x] Allow cancellation during retry waiting without waiting for the retry delay to finish.

Validation gate:
- During a retry delay Revit must remain responsive.
- Retry count/delay must still be respected.
- Cancel during retry wait must terminate cleanly at the next safe boundary.

### 4. Central unattended dialog/failure policy

- [x] Add a single unattended policy registry.
- [x] Evaluate exact Dialog IDs and Failure Definition IDs before text fallbacks.
- [x] Keep unknown dialogs untouched and logged.
- [x] Retain the narrow `DetachElements` / Remove Reference rule only when Revit explicitly exposes that resolution.
- [x] Log Failure Definition IDs so verified production cases can be promoted into the ID registry later.

Validation gate:
- The known broken-dimension `Remove Reference` case must continue to work.
- Unknown/error dialogs must never be blindly accepted.
- Each newly observed production dialog/failure should be added by verified ID when possible.

### 5. MVVM, persistence, and logging cleanup

- [x] Move the `Needs Export` action into XAML and bind it to a ViewModel command.
- [x] Remove runtime visual-tree button injection.
- [x] Make the DataGrid `Run` checkbox transient and independent of persistent job eligibility.
- [x] Make configuration writes temp-file/flush/replace based and return a real success result.
- [x] Show a configuration-save error instead of unconditional success.
- [x] Reuse one application logger across configuration, queue, scheduler, UI, and Cloud Explorer.
- [x] Show verified freshness state/reason in the queue UI.

Validation gate:
- `Needs Export` must be visible without runtime UI injection.
- Manual run selection must not rewrite persistent job eligibility.
- Abrupt/failed config writes must not silently report success.
- One normal Revit session should use the shared application log rather than creating separate Cloud Explorer/config logs.

## Required Revit 2024 validation before merge

1. Clean build against Revit 2024 API assemblies.
2. Open manager from the installed `.addin` manifest and from Add-In Manager.
3. Run at least one local RVT through a successful export, then confirm it becomes Current.
4. Modify the RVT and confirm it becomes Needs Export automatically.
5. Change a fingerprinted export setting and confirm the job becomes Needs Export even if the RVT did not change.
6. Test `OverwritePolicy = Skip` against a stale job and confirm it does not become Current.
7. Force a retryable failure and confirm Revit remains responsive during the delay.
8. Test the known broken-dimension Remove Reference model.
9. Test project-level CAD exclusion and preservation of CAD nested inside a family.
10. Test ACC freshness with a new model version if cloud access is available.
11. Test scheduler with one modern slot and confirm the legacy time does not trigger separately.

## Deferred Phase 2 — only after Phase 1 validation

### 6. Automated testing / CI

- Unit-test freshness, fingerprinting, schedule selection, output policy, config migration, and retry state transitions.
- Add a build workflow where practical; Revit-hosted integration tests remain a controlled Windows/Revit validation step.
- Add regression fixtures for old configuration formats.

### 7. Product/UX/operations polish

- Improve verified freshness sorting/priority presentation.
- Complete Cloud Explorer search/filter behavior.
- Improve session/job history, correlation IDs, diagnostics, and log retention.
- Review notifications/reporting and enterprise deployment/versioning.
- Review additional BIM export-scope controls such as phase/design-option policy as a separate product decision.
