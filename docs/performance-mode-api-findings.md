# Revit 2024 Performance Mode: API Findings

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

Transmission data contains top-level links. This is sufficient because nested links are not loaded when the parent Revit link is unloaded. Revit ignores desired transmission data unless `IsTransmitted` is true. For workshared models, the transmitted state also causes detached opening behavior, which is compatible with the export workflow.

This technique applies to local and network-drive models (including mapped drive paths such as `G:\...`). It must not be assumed to support cloud-hosted external references, because `TransmissionData` does not contain reference information from external servers.

## Current NWC export scope

The implementation currently sets:

```csharp
exportOptions.ExportScope = NavisworksExportScope.Model;
```

It does not set `NavisworksExportScope.View` and does not supply a view identifier. Therefore it exports the **entire opened host model**, not the active view and not a user-selected view. With `ExportLinks = false`, loaded Revit links are excluded from the host NWC.

## References

1. [Autodesk Revit API Developer Guide: Linked Files](https://help.autodesk.com/view/RVT/2013/ENU/caas.html?url=caas/vhelp/help-dev-autodesk-com/v/Revit/enu/2013/Help/00006-API-Developer-s-Guide/0135-Advanced135/Linked-Files.html)
2. [Revit 2024: TransmissionData.SetDesiredReferenceData](https://www.revitapidocs.com/2024/25aa4266-9f7f-0e5c-6cad-2e14eb00f984.htm)
3. [Revit 2024: TransmissionData Class](https://www.revitapidocs.com/2024/d78d1e9c-1cee-1336-88d5-b605dacd077d.htm)
