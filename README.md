# Scheduled NWC Export Manager for Revit 2024

**Scheduled NWC Export Manager** is a production-grade Autodesk Revit 2024 Add-in designed for BIM coordination managers, VDC engineers, and project architects. It automates the batch export of multiple Revit models (`.rvt`) to Navisworks cache files (`.nwc`) on a recurring daily schedule or via immediate execution, with robust failure isolation, worksharing preservation, and non-destructive background processing [1].

---

## 1. Core Architecture & Features

- **Revit 2024 & .NET Framework 4.8 Alignment**: Built specifically for Revit 2024 using the Revit 2024 API, .NET Framework 4.8, WPF MVVM architecture, and explicit Revit API contexts [2].
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

To deploy the **Scheduled NWC Export Manager** add-in for Revit 2024:

1. **Build the Solution**: Open `ScheduledNwcExporter.sln` in Visual Studio 2022. Install the **.NET Framework 4.8 Developer Pack**, ensure **.NET desktop development** is selected, and build in **Release** mode. The project references `C:\Program Files\Autodesk\Revit 2024\RevitAPI.dll` and `RevitAPIUI.dll` by default; pass `/p:RevitInstallPath="<your Revit 2024 folder>"` if Revit is installed elsewhere.
2. **Deploy Add-in Files**: Copy the compiled output files and manifest into the Revit add-ins directory:
   - **Manifest file (`ScheduledNwcExporter.addin`)**: Place into:
     ```text
     %appdata%\Autodesk\Revit\Addins\2024\
     ```
   - **Assembly folder (`ScheduledNwcExporter\` containing `ScheduledNwcExporter.dll`)**: Place into:
     ```text
     %appdata%\Autodesk\Revit\Addins\2024\ScheduledNwcExporter\
     ```
3. **Launch Revit 2024**: Upon starting Revit 2024, a new ribbon tab named **BIM Automation** will appear with the **Scheduled NWC Manager** button.

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

1. **User Launch**: User launches Revit 2024, opens the Export Manager from the Ribbon, and configures jobs and schedule [6].
2. **Trigger**: At the scheduled time (or via "Run Now"), an export session initializes.
3. **Preflight & Open**: Each model is validated, opened detached (`DetachAndPreserveWorksets`), and all user worksets are verified and opened [7].
4. **Export**: Navisworks export options (`ExportLinks = false`, shared coordinates) are applied, and `.nwc` files are generated in the target directory.
5. **Cleanup**: Documents are closed safely without saving changes to source models, releasing all memory resources before moving to the next model.
6. **Summary**: A detailed completion dialog summarizes successes, failures, and execution duration.

---

## 6. Requirements Compliance & Self-Review

| Requirement Category | Status | Notes & Implementation Details |
| :--- | :--- | :--- |
| **Revit 2024 & .NET Framework 4.8** | Implemented | Targets `.NET Framework 4.8` (`net48`) and Revit 2024 API assemblies. |
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


---

## 8. Performance Mode: Temporary Copy Without Revit Links

The **Performance Mode** option is enabled by default. For each job, the add-in copies the source `.rvt` to a unique local temporary directory, marks its top-level Revit links as unloaded through Revit `TransmissionData`, opens that temporary copy, exports the NWC, closes the document, and deletes the temporary directory. The original source RVT is read-only throughout this workflow and is never modified.[3]

| Setting | Behaviour |
| :--- | :--- |
| **Performance Mode enabled** | Opens a local temporary copy with top-level Revit links marked unloaded. This reduces the cost of loading large linked models while retaining all user worksets in the host model. |
| **Performance Mode disabled** | Opens the original model path as before. Links may load during opening, although `ExportLinks = false` still excludes them from the host NWC. |
| **Export Scope** | The current add-in uses `NavisworksExportScope.Model`; it exports the entire opened host model, not the active view and not a selected 3D view. |

This mode is intended for local or network-based RVT models. It should not be assumed to support cloud-hosted external references because `TransmissionData` does not expose external-server reference data.[3]

[3]: https://www.revitapidocs.com/2024/d78d1e9c-1cee-1336-88d5-b605dacd077d.htm "Revit 2024 TransmissionData Class"


---

## 9. Dedicated 3D View Export Scope

To ensure that the exported NWC file contains all model elements with proper visibility rules, the add-in automatically prepares or uses a dedicated 3D isometric view named **`NWC_AutoExport_3D_View`** inside the opened document:

| Rule | Action |
| :--- | :--- |
| **Export Scope** | Configured to `NavisworksExportScope.View` using the dedicated 3D view ID. |
| **Worksets** | Iterates all user worksets and explicitly ensures they are set to `WorksetVisibility.Visible` in the export view. |
| **Levels & Grids** | Explicitly hides `BuiltInCategory.OST_Levels` and `BuiltInCategory.OST_Grids` so construction datums do not clutter coordination models. |
| **Section Box & Detail** | Disables the section box to include all model geometry and sets detail level to **Fine**. |

This guarantees that your export matches a consistent, clean coordination view without modifying your source RVT or source views.
