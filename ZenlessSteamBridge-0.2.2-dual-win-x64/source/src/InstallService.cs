using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;

namespace ZenlessSteamBridge
{
    // Filesystem transaction is separate from Steam/Task Scheduler for fixture tests.
    internal sealed class InstallFiles
    {
        private readonly string home;
        public string StatePath { get { return Path.Combine(home, "installation.json"); } }
        public InstallFiles(string dataDirectory) { home = dataDirectory; }
        public Installation Prepare(string steam, string manifest, string exe, Metadata metadata, ulong owner, bool legacy, string taskName)
        {
            if (File.Exists(StatePath)) throw new InvalidOperationException("已有载入记录，请先点击“撤回载入”。");
            metadata.Executable = NormalizeExecutable(metadata.Executable);
            string library = Path.GetDirectoryName(manifest);
            string directory = Path.Combine(library, "common", App.BridgeFolder);
            if (Directory.Exists(directory)) throw new IOException("桥接目录已存在，请先处理旧安装：" + directory);
            string backup = Path.Combine(home, "backups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            var state = new Installation { Version = App.Version, SteamRoot = steam, ManifestPath = manifest,
                OriginalManifestBackup = Path.Combine(backup, "original.acf"), PreviousManifestBackup = Path.Combine(backup, "previous.acf"),
                ManagedManifestBackup = Path.Combine(backup, "managed.acf"), BridgeDirectory = directory,
                Token = Guid.NewGuid().ToString("N"), TaskName = taskName, WasInstalled = File.Exists(manifest) && !legacy, Phase = "prepared", Executable = metadata.Executable };
            if (File.Exists(manifest)) File.Copy(manifest, state.PreviousManifestBackup);
            if (state.WasInstalled) File.Copy(manifest, state.OriginalManifestBackup);
            App.Save(StatePath, state);
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, App.MarkerName), state.Token);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(directory, state.Executable)));
                File.Copy(exe, Path.Combine(directory, state.Executable));
                var depots = new Dictionary<string, object>();
                foreach (var depot in metadata.Depots) depots[depot.Key] = new Dictionary<string, object> { { "manifest", depot.Value }, { "size", "0" } };
                var appState = new Dictionary<string, object> {
                    { "appid", App.AppId.ToString() }, { "Universe", "1" }, { "name", "Zenless Zone Zero" }, { "StateFlags", "4" },
                    { "installdir", Path.GetFileName(directory) }, { "LastUpdated", ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString() },
                    { "SizeOnDisk", new FileInfo(exe).Length.ToString() }, { "buildid", metadata.Build }, { "LastOwner", owner.ToString() },
                    { "UpdateResult", "0" }, { "BytesToDownload", "0" }, { "BytesDownloaded", "0" }, { "BytesToStage", "0" }, { "BytesStaged", "0" },
                    { "AutoUpdateBehavior", "1" }, { "AllowOtherDownloadsWhileRunning", "0" }, { "ScheduledAutoUpdate", "0" },
                    { "InstalledDepots", depots }, { "UserConfig", new Dictionary<string, object> { { "language", "schinese" } } },
                    { "MountedConfig", new Dictionary<string, object> { { "language", "schinese" } } }
                };
                File.WriteAllText(state.ManagedManifestBackup, KeyValues.Render(new Dictionary<string, object> { { "AppState", appState } }), new UTF8Encoding(false));
                return state;
            }
            catch { Rollback(state); throw; }
        }
        public void Commit(Installation state)
        {
            AtomicCopy(state.ManagedManifestBackup, state.ManifestPath);
            if (state.LaunchRoute != null)
            {
                if (!SteamLaunchRoute.Matches(state.LaunchRoute) || SteamLaunchRoute.Hash(Path.Combine(state.BridgeDirectory, state.Executable)) != state.FrontendHash)
                    throw new IOException("Steam 启动入口或选择界面组件校验失败，未完成载入。");
            }
            state.Phase = "enabled"; App.Save(StatePath, state);
        }
        public void Rollback(Installation state)
        {
            ValidateState(state);
            SteamLaunchRoute.Restore(state.LaunchRoute, true);
            if (File.Exists(state.PreviousManifestBackup)) AtomicCopy(state.PreviousManifestBackup, state.ManifestPath);
            else if (File.Exists(state.ManifestPath)) File.Delete(state.ManifestPath);
            RemoveBridge(state);
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
        public void Restore(Installation state)
        {
            ValidateState(state);
            ValidateMarker(state);
            if (File.Exists(state.ManifestPath) && state.Phase != "restoring")
            {
                var acf = KeyValues.Child(KeyValues.Parse(File.ReadAllText(state.ManifestPath)), "AppState");
                string install = KeyValues.String(acf, "installdir");
                if (state.Phase == "enabled" && !String.Equals(install, Path.GetFileName(state.BridgeDirectory), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Steam 安装记录已被其他程序更改，请保留备份并查看使用说明后处理。");
            }
            if (state.WasInstalled && !File.Exists(state.OriginalManifestBackup)) throw new IOException("找不到原始安装记录备份：" + state.OriginalManifestBackup);
            state.Phase = "restoring"; App.Save(StatePath, state);
            SteamLaunchRoute.Restore(state.LaunchRoute);
            if (state.WasInstalled) AtomicCopy(state.OriginalManifestBackup, state.ManifestPath);
            else if (File.Exists(state.ManifestPath)) File.Delete(state.ManifestPath);
            RemoveBridge(state);
            // Keep the state until Task Scheduler cleanup succeeds, so retry is possible.
        }
        public void FinishRestore() { if (File.Exists(StatePath)) File.Delete(StatePath); }
        private static void AtomicCopy(string source, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temp = target + ".zsb-tmp"; File.Copy(source, temp, true);
            if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
        }
        internal static string NormalizeExecutable(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Steam 元数据缺少启动文件。");
            string relative = value.Replace('\\', '/');
            while (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative.Substring(2);
            if (relative.StartsWith("/", StringComparison.Ordinal) || relative.IndexOfAny(new[] { ':', '"', '<', '>', '|', '*', '?', '\0', '\r', '\n' }) >= 0 || !relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Steam 启动文件不是安全的相对 EXE 路径。");
            foreach (string part in relative.Split(new[] { '/' }, StringSplitOptions.None))
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(" ") || part.EndsWith(".")) throw new InvalidDataException("Steam 启动文件路径无效。");
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }
        internal static void ValidateState(Installation state)
        {
            string expected = Path.Combine(Path.GetDirectoryName(state.ManifestPath), "common", App.BridgeFolder);
            if (!Detection.Same(state.BridgeDirectory, expected) || Path.GetFileName(state.ManifestPath) != App.ManifestName || state.Executable != NormalizeExecutable(state.Executable))
                throw new InvalidDataException("本地载入记录路径不正确，已停止修改。");
            if (state.LaunchRoute != null && !Detection.Same(state.LaunchRoute.ConfigPath, SteamLaunchRoute.ConfigPath(state.SteamRoot, state.LaunchRoute.SteamId)))
                throw new InvalidDataException("Steam 账号设置路径不正确，已停止修改。");
        }
        internal static void ValidateMarker(Installation state)
        {
            if (!Directory.Exists(state.BridgeDirectory)) return;
            string marker = Path.Combine(state.BridgeDirectory, App.MarkerName);
            if (!File.Exists(marker) || File.ReadAllText(marker) != state.Token) throw new IOException("桥接目录标记不匹配，已保留目录：" + state.BridgeDirectory);
        }
        private static void RemoveBridge(Installation state)
        {
            ValidateState(state);
            if (!Directory.Exists(state.BridgeDirectory)) return;
            ValidateMarker(state);
            string marker = Path.Combine(state.BridgeDirectory, App.MarkerName);
            string exe = Path.Combine(state.BridgeDirectory, state.Executable);
            if (File.Exists(exe)) File.Delete(exe);
            string parent = Path.GetDirectoryName(exe);
            while (!Detection.Same(parent, state.BridgeDirectory) && Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0)
            { Directory.Delete(parent); parent = Path.GetDirectoryName(parent); }
            File.Delete(marker);
            if (Directory.GetFileSystemEntries(state.BridgeDirectory).Length == 0) Directory.Delete(state.BridgeDirectory);
        }
    }
    internal static class InstallService
    {
        public static void Enable(DetectionResult scan, Action<string> progress)
        {
            // Recheck ownership immediately before changing Steam, including account switches.
            var account = SteamAccount.ProbeIsolated(scan.SteamRoot);
            if (!account.Known || !account.Owned) throw new InvalidOperationException(account.Error ?? "当前 Steam 账号尚未拥有《绝区零》，请先在商店免费添加入库。");
            var configured = new RuntimeConfig { Game = scan.Game, StarRailGame = scan.StarRailGame };
            if (!GameTarget.AnyReady(configured)) throw new InvalidOperationException("请至少设置一款有效的绝区零或星穹铁道客户端。");
            var metadata = KeyValues.ReadAppInfo(Path.Combine(scan.SteamRoot, "appcache", "appinfo.vdf"));
            string manifest = FindManifest(scan.SteamRoot);
            bool legacy = false;
            EnsureGameClosed();
            using (var restart = SteamSession.Capture(scan.SteamRoot))
            {
            progress("正在正常退出 Steam…"); StopSteam(scan.SteamRoot);
            var files = new InstallFiles(App.Home); Installation state = null; bool taskCreated = false;
            try
            {
                progress("备份原始状态并安装桥接…");
                Directory.CreateDirectory(Path.GetDirectoryName(App.RuntimeExe));
                if (!Detection.Same(System.Reflection.Assembly.GetExecutingAssembly().Location, App.RuntimeExe)) File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, App.RuntimeExe, true);
                App.ExtractFrontend();
                state = files.Prepare(scan.SteamRoot, manifest, App.FrontendExe, metadata, account.SteamId, legacy, App.TaskName);
                state.LaunchRoute = SteamLaunchRoute.Prepare(scan.SteamRoot, account.SteamId, App.FrontendExe, Path.Combine(Path.GetDirectoryName(state.ManagedManifestBackup), "localconfig-before.vdf"));
                state.FrontendHash = SteamLaunchRoute.Hash(App.FrontendExe); state.HelperHash = SteamLaunchRoute.Hash(App.RuntimeExe);
                App.Save(App.StatePath, state);
                App.Save(App.ConfigPath, new RuntimeConfig { Game = Detection.ValidGame(scan.Game) ? Path.GetFullPath(scan.Game) : null, StarRailGame = Detection.ValidGame(scan.StarRailGame, GameTarget.StarRail) ? Path.GetFullPath(scan.StarRailGame) : null, SteamRoot = scan.SteamRoot, TaskName = state.TaskName });
                progress("正在设置并校验 Steam 卡片启动入口…");
                SteamLaunchRoute.Apply(state.LaunchRoute);
                RegisterTask(state.TaskName); taskCreated = true;
                files.Commit(state);
                string routeError = SteamLaunchRoute.Check(state, account.SteamId, App.FrontendExe, App.RuntimeExe);
                if (routeError != null) throw new IOException(routeError);
            }
            catch
            {
                try { if (taskCreated) DeleteTask(state.TaskName); }
                finally { if (state != null) files.Rollback(state); }
                throw;
            }
            finally { restart.Start(); }
            progress("已载入双游戏桥接。请等待 Steam 启动完成，再从游戏库开始游戏。");
            }
        }
        public static void Repair(DetectionResult scan, Action<string> progress)
        {
            var state = App.Load<Installation>(App.StatePath);
            InstallFiles.ValidateState(state); InstallFiles.ValidateMarker(state);
            if (state.TaskName != App.TaskName || state.Phase != "enabled" || !Detection.Same(scan.SteamRoot, state.SteamRoot))
                throw new InvalidOperationException("载入记录不完整或属于其他用户，请先撤回载入。");
            var account = SteamAccount.ProbeIsolated(state.SteamRoot);
            if (!account.Known || !account.Owned) throw new InvalidOperationException(account.Error ?? "当前 Steam 账号需要先拥有绝区零。");
            if (state.LaunchRoute != null && state.LaunchRoute.SteamId != account.SteamId)
                throw new InvalidOperationException("请切回原来载入时的 Steam 账号，或先撤回载入。");
            if (!File.Exists(state.ManagedManifestBackup) || (state.WasInstalled && !File.Exists(state.OriginalManifestBackup)))
                throw new IOException("原始备份不完整，请保留现有状态文件并导出诊断。");
            var previousManifest = KeyValues.Child(KeyValues.Parse(File.ReadAllText(state.ManagedManifestBackup)), "AppState");
            ulong owner;
            if (UInt64.TryParse(KeyValues.String(previousManifest, "LastOwner"), out owner) && owner != 0 && owner != account.SteamId)
                throw new InvalidOperationException("请使用原来载入时的 Steam 账号修复。");
            if (!GameTarget.AnyReady(new RuntimeConfig { Game = scan.Game, StarRailGame = scan.StarRailGame }))
                throw new InvalidOperationException("请至少设置一款有效的游戏客户端。");
            EnsureGameClosed();
            using (var restart = SteamSession.Capture(state.SteamRoot))
            {
                progress("正在正常退出 Steam…"); StopSteam(state.SteamRoot);
                string backup = Path.Combine(App.Home, "backups", "repair-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                RepairSnapshot snapshot = null;
                LaunchRoute route = null;
                try
                {
                    snapshot = new RepairSnapshot(backup, new[] { App.ConfigPath, App.RuntimeExe, App.FrontendExe, state.ManifestPath, Path.Combine(state.BridgeDirectory, state.Executable), App.StatePath });
                    progress("正在升级组件并修复 Steam 启动项…");
                    route = SteamLaunchRoute.Prepare(state.SteamRoot, account.SteamId, App.FrontendExe, Path.Combine(backup, "localconfig-before.vdf"), state.LaunchRoute);
                    state.LaunchRoute = route; state.Phase = "repairing"; state.Version = App.Version;
                    App.Save(App.StatePath, state);
                    string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    if (!Detection.Same(self, App.RuntimeExe)) File.Copy(self, App.RuntimeExe, true);
                    App.ExtractFrontend();
                    string bridge = Path.Combine(state.BridgeDirectory, state.Executable);
                    Directory.CreateDirectory(Path.GetDirectoryName(bridge)); File.Copy(App.FrontendExe, bridge, true);
                    state.FrontendHash = SteamLaunchRoute.Hash(App.FrontendExe); state.HelperHash = SteamLaunchRoute.Hash(App.RuntimeExe);
                    App.Save(App.StatePath, state);
                    App.Save(App.ConfigPath, new RuntimeConfig { Game = Detection.ValidGame(scan.Game) ? Path.GetFullPath(scan.Game) : null, StarRailGame = Detection.ValidGame(scan.StarRailGame, GameTarget.StarRail) ? Path.GetFullPath(scan.StarRailGame) : null, SteamRoot = state.SteamRoot, TaskName = state.TaskName });
                    SteamLaunchRoute.Apply(route); RegisterTask(state.TaskName);
                    new InstallFiles(App.Home).Commit(state);
                    string error = SteamLaunchRoute.Check(state, account.SteamId, App.FrontendExe, App.RuntimeExe);
                    if (error != null) throw new IOException(error);
                }
                catch
                {
                    try { SteamLaunchRoute.Restore(route, true); }
                    finally { if (snapshot != null) snapshot.Restore(); }
                    throw;
                }
                finally { restart.Start(); }
                progress("启动入口已修复并通过文件校验。请等待 Steam 重启后，从绝区零开始游戏。");
            }
        }
        public static void Restore(Action<string> progress)
        {
            var state = App.Load<Installation>(App.StatePath);
            if (state.TaskName != App.TaskName) throw new InvalidOperationException("该载入记录属于其他 Windows 用户，请用原用户卸载。");
            using (var restart = SteamSession.Capture(state.SteamRoot))
            {
            EnsureGameClosed(); progress("正在正常退出 Steam…"); StopSteam(state.SteamRoot);
            try
            {
                var files = new InstallFiles(App.Home);
                progress("正在还原 Steam 安装状态…"); files.Restore(state);
                DeleteTask(state.TaskName);
                files.FinishRestore();
                if (File.Exists(App.ConfigPath)) File.Delete(App.ConfigPath);
            }
            finally { restart.Start(); }
            progress(state.WasInstalled ? "已撤回载入，恢复原来的 Steam 客户端。" : "已撤回载入，Steam 将恢复为“安装”。");
            }
        }
        public static string FindManifest(string steam)
        {
            var matches = Detection.Libraries(steam).Select(p => Path.Combine(p, "steamapps", App.ManifestName)).Where(File.Exists).ToArray();
            if (matches.Length > 1) throw new IOException("多个 Steam 库中都有绝区零安装记录，请先在 Steam 中处理重复安装。");
            return matches.Length == 1 ? matches[0] : Path.Combine(steam, "steamapps", App.ManifestName);
        }
        private static void EnsureGameClosed()
        {
            // Steam's registry Running flags can remain set after a game exits.
            // Check actual game processes here; StopSteam must still
            // complete normally before any Steam installation files are changed.
            foreach (string name in new[] { "ZenlessZoneZero", "StarRail" }) foreach (var p in Process.GetProcessesByName(name)) using (p)
                throw new InvalidOperationException("请先完全退出绝区零和星穹铁道，再操作。");
        }
        private static bool SteamRunning()
        {
            bool found = false; foreach (var p in Process.GetProcessesByName("steam")) using (p) found = true; return found;
        }
        private static void StopSteam(string steam)
        {
            if (!SteamRunning()) return;
            using (var p = Process.Start(new ProcessStartInfo(Path.Combine(steam, "steam.exe"), "-shutdown") { UseShellExecute = false })) { }
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (SteamRunning() && DateTime.UtcNow < deadline) Thread.Sleep(400);
            if (SteamRunning()) throw new InvalidOperationException("Steam 尚未正常退出。请处理 Steam 的提示，完全退出后重试。");
            Thread.Sleep(700);
        }
        private static void RegisterTask(string name)
        { ScheduledTasks.Register(name, AssistantTaskXml(App.RuntimeExe, App.Home)); }
        internal static string AssistantTaskXml(string executable, string workingDirectory)
        {
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?><Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><RegistrationInfo><Description>Zenless Steam Bridge: game startup and session cleanup.</Description></RegistrationInfo><Triggers/><Principals><Principal id=\"Author\"><UserId>" + sid + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals><Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings><Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(executable) + "</Command><Arguments>--assist</Arguments><WorkingDirectory>" + SecurityElement.Escape(workingDirectory) + "</WorkingDirectory></Exec></Actions></Task>";
            return xml;
        }
        private static void DeleteTask(string name) { ScheduledTasks.Delete(name); }
    }
}
