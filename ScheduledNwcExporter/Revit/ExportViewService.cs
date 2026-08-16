using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using ScheduledNwcExporter.Logging;

namespace ScheduledNwcExporter.Revit
{
    /// <summary>
    /// Prepares a dedicated 3D view for NWC export where all user worksets and elements are visible,
    /// and Levels and Grids categories are explicitly hidden.
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
                // 1. Search for an existing 3D view with our export name
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

                // 2. If not found, create a new 3D isometric view
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

                // 3. Configure view visibility (Worksets, Levels, Grids, Detail Level) inside a transaction
                using (var t = new Transaction(doc, "Configure NWC Export 3D View"))
                {
                    t.Start();

                    // Ensure Fine detail level and shaded/consistent colors if supported
                    try
                    {
                        exportView.DetailLevel = ViewDetailLevel.Fine;
                    }
                    catch
                    {
                        // Ignore if unsupported in specific templates
                    }

                    // Turn off Section Box if active so the entire model geometry is included
                    try
                    {
                        if (exportView.IsSectionBoxActive)
                        {
                            exportView.IsSectionBoxActive = false;
                        }
                    }
                    catch
                    {
                        // Ignore
                    }

                    // Hide Levels and Grids categories
                    HideCategory(doc, exportView, BuiltInCategory.OST_Levels, modelName);
                    HideCategory(doc, exportView, BuiltInCategory.OST_Grids, modelName);

                    // Ensure all user worksets are visible in this view
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
