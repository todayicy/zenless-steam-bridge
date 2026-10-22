using System;
using System.Collections.Generic;
using System.IO;

namespace ZenlessSteamBridge
{
    internal static class Diagnostics
    {
        internal static object Capture(DetectionResult scan)
        {
            var report = new Dictionary<string, object> { { "version", App.Version }, { "utc", DateTime.UtcNow.ToString("o") },
                { "steamRoot", scan.SteamRoot }, { "scanError", scan.Error }, { "routeError", scan.RouteError },
                { "zenless", scan.Game }, { "starRail", scan.StarRailGame } };
            try
            {
                var state = File.Exists(App.StatePath) ? App.Load<Installation>(App.StatePath) : null;
                report["installation"] = state;
                report["routeError"] = SteamLaunchRoute.Check(state, scan.Account == null ? 0 : scan.Account.SteamId, App.FrontendExe, App.RuntimeExe);
                if (state != null)
                {
                    if (File.Exists(state.ManifestPath)) report["manifest"] = KeyValues.Parse(File.ReadAllText(state.ManifestPath));
                    string local = state.LaunchRoute != null ? state.LaunchRoute.ConfigPath : scan.Account != null && scan.Account.SteamId != 0 ? SteamLaunchRoute.ConfigPath(state.SteamRoot, scan.Account.SteamId) : null;
                    if (local != null) { bool present; report["actualLaunchOptions"] = SteamLaunchRoute.Read(local, out present); report["launchOptionsPresent"] = present; }
                }
                if (!String.IsNullOrEmpty(scan.SteamRoot)) report["steamMetadata"] = KeyValues.ReadAppInfo(Path.Combine(scan.SteamRoot, "appcache", "appinfo.vdf"));
            }
            catch (Exception error) { report["diagnosticError"] = error.Message; }
            try { if (File.Exists(App.LaunchReceiptPath)) report["lastFrontendLaunch"] = App.Json.DeserializeObject(File.ReadAllText(App.LaunchReceiptPath)); }
            catch (Exception error) { report["receiptError"] = error.Message; }
            // Never include the rest of localconfig.vdf: it contains unrelated account data.
            return report;
        }
    }
}
