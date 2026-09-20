using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace ScheduledNwcExporter.Application
{
    /// <summary>
    /// Central safety policy for unattended Revit UI/failure handling.
    /// Exact Revit IDs are always evaluated before conservative text fallbacks. New real-world
    /// cases can be added here without scattering automation decisions across the application.
    /// </summary>
    internal static class UnattendedPolicyRegistry
    {
        private static readonly Dictionary<string, int> DialogResultById =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                // Add exact DialogId -> result mappings only after observing and validating the
                // dialog in the target Revit version.
            };

        private static readonly HashSet<Guid> DetachReferenceFailureIds = new HashSet<Guid>
        {
            // Additional verified FailureDefinitionId GUIDs can be registered here as they are
            // observed in production. Known Autodesk built-ins should preferably be matched by
            // their strongly typed BuiltInFailures property below.
        };

        public static int? ResolveDialogResult(DialogBoxShowingEventArgs args, string message)
        {
            if (args == null) return null;

            string dialogId = args.DialogId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(dialogId) && DialogResultById.TryGetValue(dialogId, out int exactResult))
                return exactResult;

            string probe = (dialogId + " " + (message ?? string.Empty)).ToLowerInvariant();

            // Revit import prompt shown when the selected DWG has no usable Paper Space
            // elements and asks whether to continue from Model Space. During an unattended
            // export/open session the safe continuation is Yes; choosing No only aborts that
            // import path and can leave the model-open sequence blocked for automation.
            if (args is TaskDialogShowingEventArgs &&
                probe.Contains("import detected no valid elements in the file's paper space") &&
                probe.Contains("import from the model space"))
            {
                return (int)TaskDialogResult.Yes;
            }

            if (ContainsAny(probe,
                "far from the origin",
                "large coordinate",
                "extents are greater",
                "geometry extents",
                "imported geometry",
                "slightly off axis"))
            {
                return (int)TaskDialogResult.Ok;
            }

            if (args is TaskDialogShowingEventArgs && ContainsAny(probe,
                "missing reference",
                "references are missing",
                "reference is missing",
                "lost references",
                "invalid references",
                "dimension references",
                "tag references",
                "references to elements have been lost"))
            {
                return (int)TaskDialogResult.CommandLink1;
            }

            if (args is TaskDialogShowingEventArgs && ContainsAny(probe,
                "cannot be displayed",
                "could not be loaded",
                "failed to load",
                "some elements were deleted"))
            {
                return (int)TaskDialogResult.Ok;
            }

            return null;
        }


        public static bool ShouldDeleteBrokenDimension(FailureMessageAccessor failure, out string policyMatch)
        {
            policyMatch = string.Empty;
            if (failure == null) return false;

            try
            {
                FailureDefinitionId definitionId = failure.GetFailureDefinitionId();
                if (definitionId != null &&
                    definitionId.Equals(BuiltInFailures.DimensionFailures.RadialDimensionCannotProjectToArc))
                {
                    policyMatch = "BuiltInFailures.DimensionFailures.RadialDimensionCannotProjectToArc";
                    return true;
                }
            }
            catch
            {
                // Fall back to the exact failure text if the definition id is unavailable.
            }

            string description = failure.GetDescriptionText() ?? string.Empty;
            if (description.IndexOf("cannot form radial dimension", StringComparison.OrdinalIgnoreCase) >= 0 ||
                description.IndexOf("can't form radial dimension", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                policyMatch = "RadialDimensionDescriptionFallback";
                return true;
            }

            return false;
        }


        public static bool ShouldDetachBrokenReference(FailureMessageAccessor failure, out string policyMatch)
        {
            policyMatch = string.Empty;
            if (failure == null) return false;

            try
            {
                FailureDefinitionId definitionId = failure.GetFailureDefinitionId();
                if (definitionId != null)
                {
                    // This is the exact built-in failure shown by Revit as:
                    // "The References of the highlighted Dimension are no longer parallel."
                    if (definitionId.Equals(BuiltInFailures.DimensionFailures.LinearConstraintNotParallel))
                    {
                        policyMatch = "BuiltInFailures.DimensionFailures.LinearConstraintNotParallel";
                        return true;
                    }

                    if (DetachReferenceFailureIds.Contains(definitionId.Guid))
                    {
                        policyMatch = "FailureDefinitionId:" + definitionId.Guid;
                        return true;
                    }
                }
            }
            catch
            {
                // Fall back to the narrow text classifier when an accessor cannot expose an ID.
            }

            string description = failure.GetDescriptionText() ?? string.Empty;
            string text = description.ToLowerInvariant();
            bool annotation = text.Contains("dimension") || text.Contains("tag") || text.Contains("reference");
            bool brokenRelationship =
                text.Contains("no longer parallel") ||
                text.Contains("reference is no longer") ||
                text.Contains("references are no longer") ||
                text.Contains("reference is missing") ||
                text.Contains("references are missing") ||
                text.Contains("missing reference") ||
                text.Contains("lost reference") ||
                text.Contains("invalid reference") ||
                text.Contains("references to elements have been lost") ||
                text.Contains("references are or have become invalid");

            if (annotation && brokenRelationship)
            {
                policyMatch = "ConservativeDescriptionFallback";
                return true;
            }

            return false;
        }

        public static string GetFailureDefinitionIdText(FailureMessageAccessor failure)
        {
            if (failure == null) return string.Empty;
            try
            {
                FailureDefinitionId definitionId = failure.GetFailureDefinitionId();
                return definitionId == null ? string.Empty : definitionId.Guid.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
