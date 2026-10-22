using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace ZenlessSteamBridge
{
    internal static class Detection
    {
        public static string FindSteam()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                string path = key == null ? null : Convert.ToString(key.GetValue("SteamPath"));
                if (!String.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "steam.exe"))) return Path.GetFullPath(path);
            }
            throw new InvalidOperationException("未找到当前 Windows 用户的 Steam，请先安装并登录 Steam。");
        }
        public static List<string> Libraries(string steam)
        {
            var paths = new List<string> { steam };
            string file = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(file))
            {
                var folders = KeyValues.Child(KeyValues.Parse(File.ReadAllText(file)), "libraryfolders");
                if (folders != null) foreach (var folder in folders)
                {
                    uint id; if (!UInt32.TryParse(folder.Key, out id)) continue;
                    string path = KeyValues.String(folder.Value as Dictionary<string, object>, "path") ?? folder.Value as string;
                    if (!String.IsNullOrEmpty(path) && Directory.Exists(path) && !paths.Exists(p => Same(p, path))) paths.Add(Path.GetFullPath(path));
                }
            }
            return paths;
        }
        public static bool Same(string a, string b)
        { return a != null && b != null && String.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
        public static bool ValidGame(string path) { return ValidGame(path, GameTarget.Zenless); }
        public static bool ValidGame(string path, GameTarget target)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path) || !Path.GetFileName(path).Equals(target.Executable, StringComparison.OrdinalIgnoreCase)) return false;
                string root = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!File.Exists(Path.Combine(root, "UnityPlayer.dll")) || !File.Exists(Path.Combine(root, "GameAssembly.dll")) || !Directory.Exists(Path.Combine(root, target.DataFolder))) return false;
                using (var r = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
                {
                    if (r.BaseStream.Length < 64 || r.ReadUInt16() != 0x5a4d) return false;
                    r.BaseStream.Position = 0x3c; int offset = r.ReadInt32();
                    if (offset < 64 || (long)offset + 6 > r.BaseStream.Length) return false;
                    r.BaseStream.Position = offset; return r.ReadUInt32() == 0x4550 && r.ReadUInt16() == 0x8664;
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }
        internal static string DetectFromRoot(string root) { return DetectFromRoot(root, GameTarget.Zenless); }
        internal static string DetectFromRoot(string root, GameTarget target)
        {
            if (String.IsNullOrWhiteSpace(root)) return null;
            try
            {
                var relatives = new List<string> { "" };
                foreach (string folder in target.FolderNames) { relatives.Add(folder); relatives.Add(Path.Combine("games", folder)); }
                foreach (string relative in relatives)
                {
                    string candidate = Path.GetFullPath(Path.Combine(root, relative, target.Executable));
                    if (ValidGame(candidate, target)) return candidate;
                }
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }
        private static string FindGame(GameTarget target)
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (key == null) continue;
                        foreach (string name in key.GetSubKeyNames()) using (var item = key.OpenSubKey(name))
                        {
                            if (item == null) continue;
                            string display = Convert.ToString(item.GetValue("DisplayName"));
                            bool relevant = display.IndexOf("HoYoPlay", StringComparison.OrdinalIgnoreCase) >= 0 || display.Contains("米哈游启动器") || display.IndexOf(target.Name, StringComparison.OrdinalIgnoreCase) >= 0;
                            foreach (string namePart in target.FolderNames) relevant |= display.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!relevant) continue;
                            string game = DetectFromRoot(Convert.ToString(item.GetValue("InstallLocation")), target);
                            if (game != null) return game;
                        }
                    }
            var roots = new List<string>();
            foreach (string program in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                if (!String.IsNullOrEmpty(program)) foreach (string name in new[] { "miHoYo Launcher", "HoYoPlay" }) roots.Add(Path.Combine(program, name));
            foreach (var drive in DriveInfo.GetDrives())
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        foreach (string name in new[] { "miHoYo Launcher", "HoYoPlay" }) roots.Add(Path.Combine(drive.RootDirectory.FullName, name));
                        foreach (string name in target.FolderNames) { roots.Add(Path.Combine(drive.RootDirectory.FullName, name)); roots.Add(Path.Combine(drive.RootDirectory.FullName, "Games", name)); }
                    }
            foreach (string root in roots) { string game = DetectFromRoot(root, target); if (game != null) return game; }
            return null;
        }
        internal static string ResolveGame(string provided, string saved, Func<string> detect)
        { return ResolveGame(provided, saved, detect, GameTarget.Zenless); }
        internal static string ResolveGame(string provided, string saved, Func<string> detect, GameTarget target)
        {
            if (!String.IsNullOrWhiteSpace(provided)) return provided;
            if (ValidGame(saved, target)) return saved;
            return detect();
        }
        public static string ClientStatus(string game, out bool ready) { return ClientStatus(game, GameTarget.Zenless, out ready); }
        public static string ClientStatus(string game, GameTarget target, out bool ready)
        {
            ready = ValidGame(game, target);
            return ready ? "已识别 " + target.Name + " x64 客户端。" : "请选择 " + target.Executable + "；未设置时该卡片不可用。";
        }
        public static DetectionResult Scan(string game, string starRailGame = null)
        {
            var result = new DetectionResult();
            try
            {
                if (File.Exists(App.StatePath)) result.Installation = App.Load<Installation>(App.StatePath);
                RuntimeConfig config = File.Exists(App.ConfigPath) ? App.Load<RuntimeConfig>(App.ConfigPath) : null;
                result.Game = ResolveGame(game, config == null ? null : config.Game, () => FindGame(GameTarget.Zenless));
                result.StarRailGame = ResolveGame(starRailGame, config == null ? null : config.StarRailGame, () => FindGame(GameTarget.StarRail), GameTarget.StarRail);
                result.GameStatus = ClientStatus(result.Game, out result.ClientReady);
                result.StarRailStatus = ClientStatus(result.StarRailGame, GameTarget.StarRail, out result.StarRailReady);
                result.SteamRoot = FindSteam(); result.Account = SteamAccount.ProbeIsolated(result.SteamRoot);
                if (result.Installation != null) result.RouteError = SteamLaunchRoute.Check(result.Installation,
                    result.Account != null && result.Account.Known ? result.Account.SteamId : 0, App.FrontendExe, App.RuntimeExe);
            }
            catch (Exception e) { result.Error = e.Message; }
            return result;
        }
    }
}
