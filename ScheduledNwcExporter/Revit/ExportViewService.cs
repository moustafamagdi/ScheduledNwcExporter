using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    /// <summary>
    /// Prepares a dedicated 3D view for NWC export where all user worksets and elements are visible,
    /// Levels and Grids are hidden, and project-level CAD imports/links are excluded.
    /// CAD geometry embedded inside loadable families is intentionally preserved.
    /// </summary>
    public sealed class ExportViewService
    {
        private const string ExportViewName = "NWC_AutoExport_3D_View";
        private readonly ILogger _logger;

        public ExportViewService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ElementId? GetOrCreateExportView(Document doc, string modelName)
        {
            try
            {
                View3D? exportView = null;
                var collector = new FilteredElementCollector(doc).OfClass(typeof(View3D));
                foreach (View3D view in collector)
                {
                    if (!view.IsTemplate && string.Equals(view.Name, ExportViewName, StringComparison.OrdinalIgnoreCase))
                    {
                        exportView = view;
                        break;
                    }
                }

                if (exportView == null)
                {
                    ElementId viewFamilyTypeId = GetThreeDimensionalViewFamilyTypeId(doc);
                    if (viewFamilyTypeId == ElementId.InvalidElementId)
                    {
                        _logger.Error("ViewService", "Could not find a valid 3D ViewFamilyType to create export view.", modelName, "ExportView");
                        return null;
                    }

                    using (var t = new Transaction(doc, "Create NWC Export 3D View"))
                    {
                        t.Start();
                        exportView = View3D.CreateIsometric(doc, viewFamilyTypeId);
                        if (exportView != null)
                        {
                            exportView.Name = ExportViewName;
                        }
                        t.Commit();
                    }
                }

                if (exportView == null)
                {
                    _logger.Error("ViewService", "Failed to resolve or create export 3D view.", modelName, "ExportView");
                    return null;
                }

                using (var t = new Transaction(doc, "Configure NWC Export 3D View"))
                {
                    t.Start();

                    try
                    {
                        exportView.DetailLevel = ViewDetailLevel.Fine;
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (exportView.IsSectionBoxActive)
                        {
                            exportView.IsSectionBoxActive = false;
                        }
                    }
                    catch
                    {
                    }

                    HideCategory(doc, exportView, BuiltInCategory.OST_Levels, modelName);
                    HideCategory(doc, exportView, BuiltInCategory.OST_Grids, modelName);

                    // Hide project-level ImportInstance elements only. This covers imported and linked CAD
                    // without hiding the Import Object Styles category, so CAD geometry nested in families
                    // remains available to the NWC exporter.
                    HideProjectCadInstances(doc, exportView, modelName);

                    if (doc.IsWorkshared)
                    {
                        var worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset);
                        foreach (Workset workset in worksets)
                        {
                            try
                            {
                                exportView.SetWorksetVisibility(workset.Id, WorksetVisibility.Visible);
                            }
                            catch (Exception wsEx)
                            {
                                _logger.Warning("ViewService", $"Could not set workset '{workset.Name}' visibility: {wsEx.Message}", modelName, "ExportView");
                            }
                        }
                    }

                    t.Commit();
                }

                _logger.Info("ViewService", $"Export 3D view configured successfully: '{exportView.Name}' (ID: {exportView.Id.IntegerValue}).", modelName, "ExportView");
                return exportView.Id;
            }
            catch (Exception ex)
            {
                _logger.Error("ViewService", $"Error preparing export 3D view: {ex.Message}", modelName, "ExportView", ex);
                return null;
            }
        }

        private void HideProjectCadInstances(Document doc, View3D view, string modelName)
        {
            try
            {
                List<ImportInstance> cadInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .WhereElementIsNotElementType()
                    .Cast<ImportInstance>()
                    .ToList();

                if (cadInstances.Count == 0)
                {
                    _logger.Debug("ViewService", "No project-level CAD imports or CAD links found to hide.", modelName, "ExportView");
                    return;
                }

                // Revit 2024 exposes the hideability check on Element, not on View.
                List<ElementId> hideableIds = cadInstances
                    .Where(instance => instance.Id != ElementId.InvalidElementId && instance.CanBeHidden(view))
                    .Select(instance => instance.Id)
                    .ToList();

                if (hideableIds.Count > 0)
                {
                    view.HideElements(hideableIds);
                }

                _logger.Info("ViewService",
                    $"Excluded {hideableIds.Count} project-level CAD import/link instance(s) from the export view. Imports embedded in families are preserved.",
                    modelName, "ExportView");
            }
            catch (Exception ex)
            {
                _logger.Warning("ViewService", $"Could not exclude project-level CAD imports/links: {ex.Message}", modelName, "ExportView", ex);
            }
        }

        private static ElementId GetThreeDimensionalViewFamilyTypeId(Document doc)
        {
            var collector = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType));
            foreach (ViewFamilyType type in collector)
            {
                if (type.ViewFamily == ViewFamily.ThreeDimensional)
                {
                    return type.Id;
                }
            }
            return ElementId.InvalidElementId;
        }

        private void HideCategory(Document doc, View3D view, BuiltInCategory builtInCategory, string modelName)
        {
            try
            {
                Category? category = Category.GetCategory(doc, builtInCategory);
                if (category != null && view.CanCategoryBeHidden(category.Id))
                {
                    view.SetCategoryHidden(category.Id, true);
                    _logger.Debug("ViewService", $"Hidden category in export view: {builtInCategory}", modelName, "ExportView");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("ViewService", $"Could not hide category {builtInCategory}: {ex.Message}", modelName, "ExportView", ex);
            }
        }
    }
}
