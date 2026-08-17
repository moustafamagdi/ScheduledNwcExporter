# Hatco NWC Exporter (Revit 2024 Add-in)

Production-grade automated batch NWC export manager for **Autodesk Revit 2024**, built for the **Hatco BIM Tools** suite. It enables unattended background queue processing, scheduled daily exports, high-performance temporary-copy link suppression, and granular control over Revit geometry, parameters, and coordination views.

---

## 1. Key Features

- **Hatco Ribbon Integration**: Automatically registers under the **Hatco** ribbon tab and **Navisworks Export** panel in Revit 2024.
- **Advanced Navisworks Export Controls**: Granular UI toggles for:
  - *Divide File Into Levels*
  - *Export Parts*
  - *Export URLs*
  - *Export Room As Attribute*
  - *Convert Lights*
  - *Find Missing Materials*
  - *Parameter Export Modes (All, Elements, None)*
  - *Faceting Factor (Curve smoothness)*
- **Dedicated 3D View Export Scope**: Automatically prepares or uses **`NWC_AutoExport_3D_View`**, ensuring all user worksets are visible while explicitly hiding **Levels** and **Grids** and disabling section boxes.
- **Performance Mode (Temporary No-Link Copy)**: Copies the source `.rvt` to a local temporary directory and marks top-level Revit links as unloaded via `TransmissionData` before opening. The original RVT remains completely untouched.
- **Safe Revit API Integration**: Implements Autodesk's `IExternalEventHandler` pattern. The modeless WPF interface communicates exclusively through `ExternalEvent.Raise()`, keeping all Revit API calls (`OpenDocumentFile`, `Document.Export`, `Close`) strictly within valid Revit execution cycles.
- **Robust Batch Queue**: Independent per-model try/catch boundary with configurable retries. A failure in one model logs the error and automatically continues with the rest of the batch.
- **Cloud Integration (ACC/BIM 360)**: Integrated **Hatco Cloud Explorer** with Zero-Login authentication (leverages active Revit session). Supports browsing Hubs, Projects, and Folders to export cloud-hosted models directly.
- **Unattended Background Scheduling**: High-reliability scheduler that persists even when the tool window is closed. Automatically suppresses blocking UI dialogs during scheduled runs for 100% unattended overnight processing.
- **Local Persistence & Structured Logging**: Saves settings and job queues in `%appdata%\MoustafaMagdi\ScheduledNwcExporter\config.json` and records session diagnostics in structured log files.

---

## 2. Advanced Export Options Reference

| Option | Description |
| :--- | :--- |
| **Divide File Into Levels** | Splits exported geometry by Revit levels inside Navisworks for easier navigation and filtering. |
| **Export Parts** | Exports Revit parts (e.g. divided layers of walls/slabs) instead of or alongside host elements. |
| **Parameters** | Choose whether to export `All` parameters, `Elements` parameters only, or `None`. |
| **Faceting Factor** | Controls curve and cylinder smoothness in the exported NWC (default: 1.0). |
| **Export URLs** | Includes hyperlinks associated with model elements. |
| **Export Room As Attribute** | Attaches room bounding data as attributes to elements inside rooms. |
| **Convert Lights / Materials** | Converts rendering light sources and attempts to locate missing material textures. |

---

## 3. Deployment & Installation

To ensure stability and prevent library conflicts, the tool uses a subfolder-based deployment structure.

1. Create a subfolder named `HatcoNwcExporter` inside your Revit add-ins directory:
   ```text
   %appdata%\Autodesk\Revit\Addins\2024\HatcoNwcExporter\
   ```
2. Copy **all** DLL files from the build output into this subfolder. Required files include:
   - `ScheduledNwcExporter.dll`
   - `Autodesk.Forge.dll`
   - `Newtonsoft.Json.dll`
   - `RestSharp.dll`
   - `Microsoft.CSharp.dll` (and other System/Microsoft compatibility libraries)
3. Copy the `ScheduledNwcExporter.addin` manifest file directly into the parent folder:
   ```text
   %appdata%\Autodesk\Revit\Addins\2024\
   ```
4. Launch **Autodesk Revit 2024**.
5. Locate the **Hatco** tab on the Revit ribbon to launch the exporter.

*Note: The tool is designed to work even from the Revit Home screen (no project open).*

---

## 4. License

Released under the MIT License. Developed for Hatco BIM workflows.
