# Revit 2024 API Compatibility Findings

## Verified API corrections

`OpenOptions` configures worksets for a document **before** it is opened through `SetOpenWorksetsConfiguration(WorksetConfiguration)`. Passing a `WorksetConfiguration` constructed with `WorksetConfigurationOption.OpenAllWorksets` is the correct explicit configuration for opening all user-created worksets. Passing `null` also opens all user-created worksets. There is no `GetWorksharingOpenOptions`, `SetWorksharingOpenOptions`, or `WorksharingOpenOptions` in the Revit 2024 API.

Worksets cannot be opened programmatically after an already-open document is loaded. Consequently, the add-in must establish the open-workset configuration before `Application.OpenDocumentFile`, then verify and log the resulting `Workset.IsOpen` values after the document opens.

The Revit 2024 overload `Document.Export(string folder, string name, NavisworksExportOptions options)` has a `void` return type. Export success must be determined by the absence of an API exception and by verifying that the expected `.nwc` file exists and is non-empty.

## References

1. [OpenOptions.SetOpenWorksetsConfiguration — Revit API Docs](https://www.revitapidocs.com/2024/88de72a4-cf23-c2e7-7b38-acadc45591e7.htm)
2. [Document.Export(String, String, NavisworksExportOptions) — Revit API Docs](https://www.revitapidocs.com/2024/1b9538a9-a76b-0a40-2aed-e02f6974a43a.htm)
3. [Autodesk Revit 2024 API Export documentation](https://help.autodesk.com/view/RVT/2024/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Export_html)
