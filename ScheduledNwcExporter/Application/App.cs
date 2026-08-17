using System;
using System.Reflection;
using Autodesk.Revit.UI;
using ScheduledNwcExporter.UI.Views;

namespace ScheduledNwcExporter.Application
{
    /// <summary>
    /// Revit external application entry point and owner of the modeless export manager window.
    /// </summary>
    public class App : IExternalApplication
    {
        internal static MainWindow? ExportManagerWindow { get; set; }

        public Result OnStartup(UIControlledApplication application)
        {
            // Register assembly resolver to handle dependencies like Autodesk.Forge
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            try
            {
                const string tabName = "Hatco";
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch
                {
                    // The tab may have been created by another add-in or an earlier load.
                }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, "Navisworks Export");
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                var buttonData = new PushButtonData(
                    "CmdHatcoNwcExport",
                    "Hatco NWC\nExporter",
                    assemblyPath,
                    "ScheduledNwcExporter.Application.Command")
                {
                    ToolTip = "Launch the Hatco NWC Exporter.",
                    LongDescription = "Configure automated batch NWC exports with advanced geometry, parameter, and schedule controls for Revit 2024."
                };

                panel.AddItem(buttonData);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Scheduled NWC Export Manager", $"Failed to initialize the add-in:\n{ex.Message}");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;

            if (ExportManagerWindow != null)
            {
                ExportManagerWindow.Close();
                ExportManagerWindow = null;
            }

            return Result.Succeeded;
        }

        private Assembly? OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string assemblyName = new AssemblyName(args.Name).Name;
                string assemblyDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string assemblyPath = System.IO.Path.Combine(assemblyDir, assemblyName + ".dll");

                if (System.IO.File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }
            }
            catch { /* Ignore resolution errors */ }

            return null;
        }
    }
}
