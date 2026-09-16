using System;
using Autodesk.Revit.UI.Events;

namespace ScheduledNwcExporter.Application
{
    /// <summary>
    /// Handles a conservative set of Revit dialogs that can otherwise block unattended exports.
    /// The handler is inactive outside an export session. Exact-ID and fallback policies live in
    /// UnattendedPolicyRegistry so future cases are added in one auditable place.
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
                int? result = UnattendedPolicyRegistry.ResolveDialogResult(args, message);

                if (!result.HasValue)
                {
                    App.Logger?.Warning(
                        "DialogHandler",
                        $"Unrecognized Revit dialog left untouched. DialogId='{dialogId}', Message='{message}'.",
                        string.Empty,
                        "DialogHandling");
                    return;
                }

                bool accepted = args.OverrideResult(result.Value);
                if (accepted)
                {
                    App.Logger?.Info(
                        "DialogHandler",
                        $"Auto-answered Revit dialog. DialogId='{dialogId}', Result={result.Value}, Message='{message}'.",
                        string.Empty,
                        "DialogHandling");
                }
                else
                {
                    App.Logger?.Warning(
                        "DialogHandler",
                        $"Revit rejected automatic dialog result. DialogId='{dialogId}', Result={result.Value}, Message='{message}'.",
                        string.Empty,
                        "DialogHandling");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("DialogHandler", $"Could not process Revit dialog: {ex.Message}", string.Empty, "DialogHandling", ex);
            }
        }
    }
}
