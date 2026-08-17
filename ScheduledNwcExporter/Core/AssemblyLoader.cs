using System;
using System.IO;
using System.Reflection;

namespace ScheduledNwcExporter.Core
{
    /// <summary>
    /// Ensures that external dependencies like Autodesk.Forge are loaded correctly,
    /// even when running through Add-In Manager or other non-standard environments.
    /// </summary>
    public static class AssemblyLoader
    {
        private static bool _isRegistered = false;

        public static void Register()
        {
            if (_isRegistered) return;
            
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            _isRegistered = true;
        }

        public static void Unregister()
        {
            if (!_isRegistered) return;

            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
            _isRegistered = false;
        }

        private static Assembly? OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                // Get the simple name of the assembly (e.g., "Autodesk.Forge")
                string assemblyName = new AssemblyName(args.Name).Name;
                
                // Determine the directory where the main add-in DLL is located
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                
                // In some cases (like Add-In Manager), GetExecutingAssembly().Location might be empty or wrong.
                // Fallback to the directory of the assembly that is currently running this code.
                if (string.IsNullOrEmpty(assemblyDir))
                {
                    assemblyDir = Path.GetDirectoryName(typeof(AssemblyLoader).Assembly.Location);
                }

                if (string.IsNullOrEmpty(assemblyDir)) return null;

                string assemblyPath = Path.Combine(assemblyDir, assemblyName + ".dll");

                if (File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }
            }
            catch { /* Silent fail for assembly resolution */ }

            return null;
        }
    }
}
