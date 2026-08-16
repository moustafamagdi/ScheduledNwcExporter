# Scheduled NWC Export Manager for Revit 2025

**Scheduled NWC Export Manager** is a production-grade Autodesk Revit 2025 Add-in designed for BIM coordination managers, VDC engineers, and project architects. It automates the batch export of multiple Revit models (`.rvt`) to Navisworks cache files (`.nwc`) on a recurring daily schedule or via immediate execution, with robust failure isolation, worksharing preservation, and non-destructive background processing [1].

---

## 1. Core Architecture & Features

- **Revit 2025 & .NET 8 Alignment**: Built specifically for Revit 2025 using modern C# features, WPF MVVM architecture, and explicit Revit API contexts [2].
- **Failure Isolation**: Each model export is treated as an independent job. If one model fails (e.g., locked file, missing link, or unexpected exception), the process **does not stop**; it logs the error, safely cleans up, and continues automatically with the remaining models in the queue [3].
- **Workshared Model Safety**: Opens models detached from central (`DetachAndPreserveWorksets`) while ensuring all user-created worksets are programmatically opened and verified [4].
- **Link & Annotation Control**: Explicitly configures `ExportLinks = false` to prevent nested links from polluting host NWC coordination models, while relying on Revit's native export engine to exclude annotations without modifying the source `.rvt` files [5].
- **Flexible Scheduling**: Configurable daily execution time (e.g., 19:00) with interactive "Run Now", "Pause", and "Test Job" controls.
- **Filename Templates**: Supports dynamic tokens such as `{ModelName}`, `{Date}`, `{Time}`, `{Year}`, `{Month}`, and `{Day}`.
- **Production Logging**: Comprehensive session logging with severity levels (`DEBUG`, `INFO`, `SUCCESS`, `WARNING`, `ERROR`, `FATAL`) stored in user AppData.

---

## 2. Project Directory Structure

```text
ScheduledNwcExporter/
├── ScheduledNwcExporter.sln
├── ScheduledNwcExporter.addin
└── ScheduledNwcExporter/
    ├── ScheduledNwcExporter.csproj
    ├── Application/
    │   ├── App.cs
    │   └── Command.cs
    ├── Core/
    │   ├── Models/
    │   └── StateMachine/
    │       └── JobState.cs
    ├── Revit/
    │   ├── DocumentManager.cs
    │   ├── WorksetManager.cs
    │   ├── LinkManager.cs
    │   └── NwcExporterService.cs
    ├── Scheduler/
    │   └── ScheduleManager.cs
    ├── Queue/
    │   ├── ExportQueue.cs
    │   └── JobProcessor.cs
    ├── Logging/
    │   └── FileLogger.cs
    ├── Configuration/
    │   └── ConfigurationManager.cs
    └── UI/
        ├── MainWindow.xaml & .cs
        ├── JobEditorWindow.xaml & .cs
        ├── DiagnosticsWindow.xaml & .cs
        ├── ViewModels/
        └── RelayCommand.cs
```

---

## 3. Installation Instructions

To deploy the **Scheduled NWC Export Manager** add-in for Revit 2025:

1. **Build the Solution**: Open `ScheduledNwcExporter.sln` in Visual Studio 2022 (configured with .NET 8 SDK and Revit 2025 API references) and build in **Release** mode.
2. **Deploy Add-in Files**: Copy the compiled output files and manifest into the Revit add-ins directory:
   - **Manifest file (`ScheduledNwcExporter.addin`)**: Place into:
     ```text
     %appdata%\Autodesk\Revit\Addins\2025\
     ```
   - **Assembly folder (`ScheduledNwcExporter\` containing `ScheduledNwcExporter.dll`)**: Place into:
     ```text
     %appdata%\Autodesk\Revit\Addins\2025\ScheduledNwcExporter\
     ```
3. **Launch Revit 2025**: Upon starting Revit 2025, a new ribbon tab named **BIM Automation** will appear with the **Scheduled NWC Manager** button.

---

## 4. Configuration & Storage

Configuration settings, export jobs, and execution logs are stored locally under user AppData without modifying project files:

- **Configuration File**:
  ```text
  %appdata%\MoustafaMagdi\ScheduledNwcExporter\config.json
  ```
- **Session Logs**:
  ```text
  %appdata%\MoustafaMagdi\ScheduledNwcExporter\logs\YYYY-MM-DD_HH-mm-ss.log
  ```

---

## 5. Execution Flow & Lifecycle

1. **User Launch**: User launches Revit 2025, opens the Export Manager from the Ribbon, and configures jobs and schedule [6].
2. **Trigger**: At the scheduled time (or via "Run Now"), an export session initializes.
3. **Preflight & Open**: Each model is validated, opened detached (`DetachAndPreserveWorksets`), and all user worksets are verified and opened [7].
4. **Export**: Navisworks export options (`ExportLinks = false`, shared coordinates) are applied, and `.nwc` files are generated in the target directory.
5. **Cleanup**: Documents are closed safely without saving changes to source models, releasing all memory resources before moving to the next model.
6. **Summary**: A detailed completion dialog summarizes successes, failures, and execution duration.

---

## 6. Requirements Compliance & Self-Review

| Requirement Category | Status | Notes & Implementation Details |
| :--- | :--- | :--- |
| **Revit 2025 & .NET 8** | Implemented | Targets .NET 8 (`net8.0-windows`) and Revit 2025 API assemblies. |
| **Failure Isolation** | Implemented | Try-catch blocks wrap individual job processing; failure in Model B does not halt Model C. |
| **Workset Verification** | Implemented | `WorksetManager` enumerates user worksets, verifies open states, and opens closed worksets. |
| **Link Exclusion** | Implemented | `ExportLinks = false` configured in `NavisworksExportOptions`; links inspected and logged. |
| **Source Model Safety** | Implemented | Detached open mode ensures original `.rvt` files remain untouched. |
| **UI & MVVM** | Implemented | Modern WPF interface using MVVM architecture, data binding, commands, and reactive UI updates. |
| **Logging & Diagnostics** | Implemented | Structured file logging with severity levels and dedicated diagnostics window. |
| **Headless Revit Scheduling** | Known Limitation | Automatic scheduling requires Revit to remain running; headless background Revit launch is excluded in V1 per specification. |

---

## 7. References

1. Autodesk Revit API Documentation for .NET 8 and Revit 2025. Available online via Autodesk Developer Network.
2. Navisworks Export API Guidelines and `NavisworksExportOptions` configuration standards.

---
*Developed by **Manus AI** for Moustafa Magdi.*
