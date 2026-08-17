using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ScheduledNwcExporter.Core
{
    /// <summary>
    /// Service to handle authentication with Autodesk Platform Services (APS)
    /// by leveraging the existing Revit session token.
    /// </summary>
    public class CloudAuthenticationService
    {
        private static string _cachedToken;
        private static DateTime _tokenExpiry;

        /// <summary>
        /// Gets the active OAuth2 access token from the Revit session.
        /// </summary>
        /// <returns>The access token string, or null if retrieval fails.</returns>
        public static string GetAccessToken()
        {
            // Simple caching to avoid repeated reflection calls
            if (!string.IsNullOrEmpty(_cachedToken) && DateTime.Now < _tokenExpiry)
            {
                return _cachedToken;
            }

            try
            {
                // 1. Locate Revit installation directory
                string revitPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
                string ssonetPath = Path.Combine(revitPath, "SSONET.dll");

                if (!File.Exists(ssonetPath))
                {
                    return null;
                }

                // 2. Load SSONET.dll via Reflection
                Assembly ssonetAssembly = Assembly.LoadFrom(ssonetPath);

                // 3. Get the AdWebServicesBase instance
                Type adWebServicesBaseType = ssonetAssembly.GetTypes()
                    .FirstOrDefault(t => t.FullName == "Autodesk.Revit.AdWebServicesBase");

                if (adWebServicesBaseType == null) return null;

                MethodInfo getInstanceMethod = adWebServicesBaseType.GetMethod("GetInstance", BindingFlags.Public | BindingFlags.Static);
                object adWebServicesInstance = getInstanceMethod?.Invoke(null, null);

                if (adWebServicesInstance == null) return null;

                // 4. Invoke GetOAuth2AccessToken
                MethodInfo getTokenMethod = adWebServicesInstance.GetType().GetMethod("GetOAuth2AccessToken", BindingFlags.Public | BindingFlags.Instance);
                string token = getTokenMethod?.Invoke(adWebServicesInstance, null) as string;

                if (!string.IsNullOrEmpty(token))
                {
                    _cachedToken = token;
                    // Typically these tokens last 60 minutes, we cache for 50 to be safe
                    _tokenExpiry = DateTime.Now.AddMinutes(50);
                    return token;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to retrieve cloud token: {ex.Message}");
            }

            return null;
        }
    }
}
