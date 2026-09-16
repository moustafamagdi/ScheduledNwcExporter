using System;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace ScheduledNwcExporter.Application
{
    /// <summary>
    /// Handles a small, conservative set of Revit dialogs that can otherwise block an
    /// unattended scheduled export. The handler is inactive outside an export session.
    /// Unknown dialogs are logged and left for the user instead of guessing a destructive answer.
    /// </summary>
    internal static class UnattendedDialogHandler
    {
        public static void OnDialogBoxShowing(object sender, DialogBoxShowingEventArgs args)
        {
            if (App.QueueHandler == null || !App.QueueHandler.IsSessionRunning)
                return;

            try
            {
                string dialogId = args.DialogId ?? string.Empty;
                string message = (args as TaskDialogShowingEventArgs)?.Message ?? string.Empty;
                string probe = (dialogId + " " + message).ToLowerInvariant();

                int? result = ResolveResult(probe, args);
                if (!result.HasValue)
                {
                    App.Logger?.Warning("DialogHandler",
                        $"Unattended export encountered an unrecognized Revit dialog and left it untouched. DialogId='{dialogId}', Message='{message}'.",
                        string.Empty, "DialogHandling");
                    return;
                }

                bool accepted = args.OverrideResult(result.Value);
                if (accepted)
                {
                    App.Logger?.Info("DialogHandler",
                        $"Auto-answered Revit dialog during export. DialogId='{dialogId}', Result={result.Value}, Message='{message}'.",
                        string.Empty, "DialogHandling");
                }
                else
                {
                    App.Logger?.Warning("DialogHandler",
                        $"Revit rejected the automatic dialog result. DialogId='{dialogId}', Result={result.Value}, Message='{message}'.",
                        string.Empty, "DialogHandling");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("DialogHandler", $"Could not process Revit dialog: {ex.Message}", string.Empty, "DialogHandling", ex);
            }
        }

        private static int? ResolveResult(string probe, DialogBoxShowingEventArgs args)
        {
            // Geometry/import warnings that are informational and safe to acknowledge.
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

            // Missing/orphaned annotations and references can stop an unattended open.
            // Revit task dialogs with custom choices use 1001 for the first command link.
            // We choose the first action only when the text explicitly describes missing,
            // invalid or lost references; unknown multi-choice dialogs are never guessed.
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

            // Plain error/warning task dialogs that only require acknowledgement.
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
