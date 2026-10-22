using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ZenlessSteamBridge
{
    // Steam owns a medium-integrity frontend; Task Scheduler starts the elevated
    // helper. Only this session's game PIDs are stopped when Steam stops it.
    internal static class BridgeRuntime
    {
        private static RuntimeConfig config;
        private static Installation installation;
        private static GameTarget selectedTarget;
        private static string SelectedGame;
        private static readonly string RequestPath = Path.Combine(App.Home, "request.json");
        private static readonly string StatusPath = Path.Combine(App.Home, "status.json");
        internal sealed class Request
        {
            public string GameId { get; set; }
            public int Pid { get; set; }
            public long StartTicks { get; set; }
            public string Executable { get; set; }
            public string Id { get; set; }
            public int BrokerPid { get; set; }
            public long BrokerStartTicks { get; set; }
            public Dictionary<string, string> SteamEnvironment { get; set; }
        }
        internal static bool AcceptSteamLaunch(string appId, string gameId, bool installed)
        {
            string expected = App.AppId.ToString();
            return installed || String.Equals((appId ?? "").Trim(), expected, StringComparison.Ordinal) ||
                String.Equals((gameId ?? "").Trim(), expected, StringComparison.Ordinal);
        }
        private static void LoadConfiguration()
        {
            config = App.Load<RuntimeConfig>(App.ConfigPath);
            if (!GameTarget.AnyReady(config) || !File.Exists(Path.Combine(config.SteamRoot, "steam.exe")) || config.TaskName != App.TaskName)
                throw new InvalidDataException("客户端路径或辅助任务配置无效，请重新载入绝区零。");
            var state = installation = App.Load<Installation>(App.StatePath);
            InstallFiles.ValidateState(state);
            if (state.Phase != "enabled" || state.TaskName != App.TaskName || !Detection.Same(state.SteamRoot, config.SteamRoot))
                throw new InvalidDataException("没有完整的载入记录，请打开 Zenless Steam Bridge 重新载入。");
            string marker = Path.Combine(state.BridgeDirectory, App.MarkerName);
            if (!File.Exists(marker) || File.ReadAllText(marker) != state.Token) throw new InvalidDataException("桥接目录标记不匹配，请撤回后重新载入。");
            if (state.LaunchRoute != null)
            {
                SteamLaunchRoute.Validate(state.SteamRoot, state.LaunchRoute, App.FrontendExe);
                if (!File.Exists(App.FrontendExe) || SteamLaunchRoute.Hash(App.FrontendExe) != state.FrontendHash)
                    throw new InvalidDataException("选择界面组件校验失败，请打开管理程序修复启动入口。");
            }
        }
        internal static bool OwnsFrontend(string path, Installation state)
        {
            return state != null && (Detection.Same(path, Path.Combine(state.BridgeDirectory, state.Executable)) ||
                (state.LaunchRoute != null && Detection.Same(path, App.FrontendExe)));
        }
        public static int Run()
        {
            // A receipt distinguishes a routing failure from a failure after selection.
            App.Save(App.LaunchReceiptPath, new { version = App.Version, path = Application.ExecutablePath,
                utc = DateTime.UtcNow.ToString("o"), appId = Environment.GetEnvironmentVariable("SteamAppId") });
            LoadConfiguration();
            bool installed = OwnsFrontend(Application.ExecutablePath, installation);
            if (!installed || !AcceptSteamLaunch(Environment.GetEnvironmentVariable("SteamAppId"), Environment.GetEnvironmentVariable("SteamGameId"), installed))
                throw new InvalidOperationException("请从 Steam 游戏库的《绝区零》开始游戏。");
            bool created;
            using (var mutex = new Mutex(true, @"Local\ZenlessSteamBridgeRun", out created))
            {
                if (!created) return 0;
                string selectedId;
                using (var picker = new GameSelectionForm(config))
                {
                    if (picker.ShowDialog() != DialogResult.OK) return 0;
                    selectedId = picker.SelectedGame;
                }
                // Validate again after the user chooses; no helper runs on cancel.
                GameTarget.Get(selectedId).RequirePath(config);
                Request request;
                using (var self = Process.GetCurrentProcess())
                    request = new Request { GameId = selectedId, Pid = self.Id, StartTicks = self.StartTime.ToUniversalTime().Ticks,
                        Executable = Application.ExecutablePath, Id = Guid.NewGuid().ToString("N"),
                        SteamEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) };
                foreach (DictionaryEntry value in Environment.GetEnvironmentVariables())
                {
                    string name = Convert.ToString(value.Key);
                    if (SteamVariable(name)) request.SteamEnvironment[name] = Convert.ToString(value.Value);
                }
                App.Save(RequestPath, request);
                Status(request, "waiting", "等待管理员辅助程序启动。");
                ScheduledTasks.Run(config.TaskName);
                var deadline = DateTime.UtcNow.AddSeconds(30);
                bool brokerStarted = false;
                while (true)
                {
                    try
                    {
                        var status = App.Load<Dictionary<string, object>>(StatusPath);
                        if (Convert.ToString(status["id"]) == request.Id)
                        {
                            string state = Convert.ToString(status["state"]);
                            if (state == "finished") return 0;
                            if (state == "error") return 1;
                        }
                        var current = App.Load<Request>(RequestPath);
                        if (current.Id == request.Id && current.BrokerPid > 0)
                        {
                            brokerStarted = true;
                            if (CreationTicks(current.BrokerPid) != current.BrokerStartTicks || !Detection.Same(ImagePath(current.BrokerPid), App.RuntimeExe))
                                throw new InvalidOperationException("管理员辅助程序意外退出，请撤回载入后重试。");
                        }
                    }
                    catch (IOException) { }
                    if (!brokerStarted && DateTime.UtcNow >= deadline) throw new TimeoutException("辅助程序未能在 30 秒内启动，请检查任务计划程序服务，然后撤回并重新载入。");
                    Thread.Sleep(500);
                }
            }
        }
        public static int RunAssist()
        {
            LoadConfiguration();
            var request = App.Load<Request>(RequestPath);
            if (!BridgeAlive(request)) return 0;
            try
            {
                selectedTarget = GameTarget.Get(request.GameId ?? GameTarget.Zenless.Id);
                SelectedGame = selectedTarget.RequirePath(config);
            }
            catch (Exception error)
            {
                Status(request, "error", error.Message);
                if (BridgeAlive(request)) MessageBox.Show(error.Message, "客户端不可用", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            try
            {
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new InvalidOperationException("辅助程序未获得管理员权限，请以管理员身份重新载入。");
                using (var self = Process.GetCurrentProcess()) { request.BrokerPid = self.Id; request.BrokerStartTicks = self.StartTime.ToUniversalTime().Ticks; }
                App.Save(RequestPath, request);
                if (request.SteamEnvironment != null) foreach (var pair in request.SteamEnvironment)
                    if (SteamVariable(pair.Key)) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                foreach (string name in new[] { "SteamAppId", "SteamGameId", "SteamOverlayGameId" }) Environment.SetEnvironmentVariable(name, App.AppId.ToString());
                Environment.SetEnvironmentVariable("SteamClientLaunch", "1");
                // Use the user's installed Valve renderer, as in the upstream bridge.
                // No game DLLs are replaced, patched, or loaded for detection.
                string renderer = Path.Combine(config.SteamRoot, "GameOverlayRenderer64.dll");
                if (LoadLibraryEx(renderer, IntPtr.Zero, 8) == IntPtr.Zero)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法初始化本机 Steam 叠加层组件。");
                request.SteamEnvironment = null; App.Save(RequestPath, request);
                int result = Play(request);
                Status(request, "finished", "本次游戏已退出。");
                return result;
            }
            catch (Exception error)
            {
                Status(request, "error", error.Message);
                if (BridgeAlive(request)) MessageBox.Show(error.Message, "Zenless Steam Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        private static bool SteamVariable(string name)
        { return name.StartsWith("Steam", StringComparison.OrdinalIgnoreCase) || name.StartsWith("GameOverlay", StringComparison.OrdinalIgnoreCase); }
        private static int Play(Request owner)
        {
            if (!BridgeAlive(owner)) return 0;
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(selectedTarget.Executable))) using (process)
                if (process.Id != owner.Pid) throw new InvalidOperationException("所选游戏已在运行。请先退出游戏，再从 Steam 启动。");
            var owned = new Dictionary<int, long>();
            var gate = new object();
            using (var finished = new ManualResetEvent(false))
            {
                if (!BridgeAlive(owner)) return 0;
                DateTime launched = DateTime.UtcNow;
                using (var game = Process.Start(new ProcessStartInfo { FileName = SelectedGame, WorkingDirectory = Path.GetDirectoryName(SelectedGame), UseShellExecute = false }))
                {
                    if (game == null) throw new InvalidOperationException("Windows 未返回游戏进程，请先用米哈游启动器确认客户端可正常运行。");
                    try { owned[game.Id] = game.StartTime.ToUniversalTime().Ticks; }
                    catch (InvalidOperationException) { throw new InvalidOperationException("所选游戏启动后立即退出。请通过米哈游启动器更新并修复游戏，再重试。"); }
                }
                var watcher = new Thread(delegate()
                {
                    while (!finished.WaitOne(400))
                    {
                        if (BridgeAlive(owner)) continue;
                        lock (gate) { TrackChildren(owned); StopOwned(owned); }
                        return;
                    }
                });
                watcher.IsBackground = true; watcher.Start();
                try
                {
                    DateTime emptySince = DateTime.MaxValue;
                    while (BridgeAlive(owner))
                    {
                        bool alive = false;
                        lock (gate)
                        {
                            TrackChildren(owned);
                            foreach (var pair in owned) if (SameGameProcess(pair.Key, pair.Value)) { alive = true; break; }
                        }
                        if (alive) emptySince = DateTime.MaxValue;
                        else
                        {
                            if (emptySince == DateTime.MaxValue) emptySince = DateTime.UtcNow;
                            // Permit the game's bootstrap process to hand off to a child.
                            if (DateTime.UtcNow - emptySince > TimeSpan.FromSeconds(2))
                            {
                                if (DateTime.UtcNow - launched < TimeSpan.FromSeconds(5)) throw new InvalidOperationException("所选游戏启动后很快退出。请通过米哈游启动器更新或修复客户端后重试。");
                                return 0;
                            }
                        }
                        Thread.Sleep(400);
                    }
                    return 0;
                }
                finally
                {
                    finished.Set(); watcher.Join();
                    lock (gate) { TrackChildren(owned); StopOwned(owned); }
                }
            }
        }
        private static void TrackChildren(Dictionary<int, long> owned)
        {
            try
            {
                using (var search = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process WHERE Name='" + selectedTarget.Executable + "'"))
                using (var rows = search.Get())
                    foreach (ManagementObject row in rows) using (row)
                    {
                        int pid = Convert.ToInt32(row["ProcessId"]), parent = Convert.ToInt32(row["ParentProcessId"]);
                        long ticks = CreationTicks(pid);
                        if (!owned.ContainsKey(pid) && owned.ContainsKey(parent) && ticks >= owned[parent] && Detection.Same(ImagePath(pid), SelectedGame)) owned[pid] = ticks;
                    }
            }
            catch (ManagementException) { }
            catch (System.Runtime.InteropServices.COMException) { }
        }
        private static bool SameGameProcess(int pid, long ticks)
        { return ticks > 0 && CreationTicks(pid) == ticks && Detection.Same(ImagePath(pid), SelectedGame); }
        private static void StopOwned(Dictionary<int, long> owned)
        {
            foreach (var pair in owned)
            {
                try
                {
                    if (!SameGameProcess(pair.Key, pair.Value)) continue;
                    using (var process = Process.GetProcessById(pair.Key))
                    {
                        if (!SameGameProcess(process.Id, pair.Value)) continue;
                        process.Kill(); process.WaitForExit(3000);
                    }
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        private static bool BridgeAlive(Request request)
        {
            return request != null && request.StartTicks > 0 && CreationTicks(request.Pid) == request.StartTicks &&
                OwnsFrontend(request.Executable, installation) && Detection.Same(ImagePath(request.Pid), request.Executable);
        }
        private static void Status(Request request, string state, string message)
        { App.Save(StatusPath, new { id = request.Id, state = state, message = message }); }
        private static string ImagePath(int pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var value = new StringBuilder(32768); int length = value.Capacity;
                return QueryFullProcessImageName(handle, 0, value, ref length) ? value.ToString() : null;
            }
            finally { CloseHandle(handle); }
        }
        private static long CreationTicks(int pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero) return 0;
            try
            {
                long created, exited, kernel, user;
                return GetProcessTimes(handle, out created, out exited, out kernel, out user) ? DateTime.FromFileTimeUtc(created).Ticks : 0;
            }
            finally { CloseHandle(handle); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, int flags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
