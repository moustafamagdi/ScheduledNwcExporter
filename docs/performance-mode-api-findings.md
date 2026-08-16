# Revit 2024 Performance Mode & Export Scope: API Findings

## Temporary-copy link suppression

Revit 2024 supports modifying the desired load state of external file references without opening the model through `TransmissionData`. The correct Revit 2024 API flow is:

1. Create a **temporary copy** of the source RVT. Never write transmission data to the original source RVT.
2. Convert the temporary path to a `ModelPath`.
3. Read the transmission data using `TransmissionData.ReadTransmissionData(ModelPath)`.
4. Inspect `GetAllExternalFileReferenceIds()` and retrieve each reference through `GetLastSavedReferenceData(ElementId)`.
5. For each `ExternalFileReferenceType.RevitLink`, call:

```csharp
transmissionData.SetDesiredReferenceData(
    referenceId,
    externalReference.GetPath(),
    externalReference.PathType,
    shouldLoad: false);
```

6. Set `transmissionData.IsTransmitted = true` and call `TransmissionData.WriteTransmissionData` on the **temporary** model.
7. Open the temporary copy detached and with `WorksetConfigurationOption.OpenAllWorksets`, export it, close it, and delete it.

## Dedicated 3D View Export Scope

To ensure that the exported NWC file contains all model elements with proper visibility rules, the add-in automatically prepares or uses a dedicated 3D isometric view named **`NWC_AutoExport_3D_View`** inside the opened document:

- **Export Scope**: Configured to `NavisworksExportScope.View` using the dedicated 3D view ID.
- **Worksets**: Iterates all user worksets and explicitly ensures they are set to `WorksetVisibility.Visible` in the export view.
- **Levels & Grids**: Explicitly hides `BuiltInCategory.OST_Levels` and `BuiltInCategory.OST_Grids` so construction datums do not clutter coordination models.
- **Section Box & Detail**: Disables the section box to include all model geometry and sets detail level to **Fine**.

## References

1. [Autodesk Revit API Developer Guide: Linked Files](https://help.autodesk.com/view/RVT/2013/ENU/caas.html?url=caas/vhelp/help-dev-autodesk-com/v/Revit/enu/2013/Help/00006-API-Developer-s-Guide/0135-Advanced135/Linked-Files.html)
2. [Revit 2024: TransmissionData.SetDesiredReferenceData](https://www.revitapidocs.com/2024/25aa4266-9f7f-0e5c-6cad-2e14eb00f984.htm)
3. [Revit 2024: TransmissionData Class](https://www.revitapidocs.com/2024/d78d1e9c-1cee-1336-88d5-b605dacd077d.htm)
