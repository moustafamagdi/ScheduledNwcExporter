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

## 3. Installation

1. Copy the compiled build output (`ScheduledNwcExporter.dll`, `ScheduledNwcExporter.pdb`, `Newtonsoft.Json.dll`, and `ScheduledNwcExporter.addin`) to your Revit add-ins folder:
   ```text
   %appdata%\Autodesk\Revit\Addins\2024\
   ```
2. Launch **Autodesk Revit 2024**.
3. Locate the **Hatco** tab on the Revit ribbon to launch the exporter.

---

## 4. License

Released under the MIT License. Developed for Hatco BIM workflows.
