using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ZenlessSteamBridge
{
    internal static class App
    {
        public const string Version = "0.2.2";
        public const uint AppId = 4162040;
        public const string GameExeName = "ZenlessZoneZero.exe";
        public const string BridgeFolder = "Zenless Steam CN Bridge";
        public const string MarkerName = ".zenless-steam-bridge";
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZenlessSteamBridge");
        public static readonly string StatePath = Path.Combine(Home, "installation.json");
        public static readonly string ConfigPath = Path.Combine(Home, "runtime.json");
        public static readonly string RuntimeExe = Path.Combine(Home, "app", "ZenlessSteamBridge.exe");
        public static readonly string FrontendExe = Path.Combine(Home, "app", "ZenlessBridge.exe");
        public static readonly string LaunchReceiptPath = Path.Combine(Home, "last-launch.json");
        public static string ManifestName { get { return "appmanifest_" + AppId + ".acf"; } }
        public static string TaskName { get { return "ZenlessSteamBridge-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value; } }
        public static void Save(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            File.WriteAllText(temp, Json.Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public static void ExtractFrontend(string target = null)
        {
            using (var resource = typeof(App).Assembly.GetManifestResourceStream("ZenlessSteamBridge.Frontend"))
            {
                if (resource == null) throw new InvalidDataException("程序缺少桥接组件，请重新解压完整版本。");
                string path = target ?? FrontendExe;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var output = File.Create(path)) resource.CopyTo(output);
            }
        }
        public static T Load<T>(string path) { return Json.Deserialize<T>(File.ReadAllText(path)); }
    }
    public sealed class RuntimeConfig
    {
        public string Game { get; set; }
        public string StarRailGame { get; set; }
        public string SteamRoot { get; set; }
        public string TaskName { get; set; }
    }
    public sealed class Installation
    {
        public string Version { get; set; }
        public string SteamRoot { get; set; }
        public string ManifestPath { get; set; }
        public string OriginalManifestBackup { get; set; }
        public string PreviousManifestBackup { get; set; }
        public string ManagedManifestBackup { get; set; }
        public string BridgeDirectory { get; set; }
        public string Token { get; set; }
        public string TaskName { get; set; }
        public bool WasInstalled { get; set; }
        public string Phase { get; set; }
        public string Executable { get; set; }
        public LaunchRoute LaunchRoute { get; set; }
        public string FrontendHash { get; set; }
        public string HelperHash { get; set; }
    }
    public sealed class LaunchRoute
    {
        public ulong SteamId { get; set; }
        public string ConfigPath { get; set; }
        public string BackupPath { get; set; }
        public bool BeforePresent { get; set; }
        public string BeforeValue { get; set; }
        public bool OriginalPresent { get; set; }
        public string OriginalValue { get; set; }
        public string AppliedValue { get; set; }
    }
    public sealed class Ownership
    {
        public bool Known { get; set; }
        public bool Owned { get; set; }
        public bool Installed { get; set; }
        public ulong SteamId { get; set; }
        public string Name { get; set; }
        public string Error { get; set; }
    }
    internal sealed class DetectionResult
    {
        public bool ClientReady;
        public bool StarRailReady;
        public string SteamRoot;
        public Ownership Account;
        public string Game;
        public string StarRailGame;
        public string GameStatus;
        public string StarRailStatus;
        public Installation Installation;
        public string RouteError;
        public string Error;
    }
    internal sealed class Metadata
    {
        public string Build;
        public string Executable;
        public Dictionary<string, string> Depots = new Dictionary<string, string>();
    }
}
