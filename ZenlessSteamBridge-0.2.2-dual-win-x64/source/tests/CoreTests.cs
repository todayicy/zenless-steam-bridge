using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace ZenlessSteamBridge
{
    internal static class CoreTests
    {
        private static int count;
        private static void Check(bool ok, string message)
        { if (!ok) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
        private static void Throws(Action action, string message)
        { bool threw = false; try { action(); } catch (InvalidDataException) { threw = true; } catch (IOException) { threw = true; } catch (InvalidOperationException) { threw = true; } Check(threw, message); }
        private static string MakeGame(string folder)
        { return MakeGame(folder, GameTarget.Zenless); }
        private static string MakeGame(string folder, GameTarget target)
        {
            Directory.CreateDirectory(folder);
            byte[] pe = new byte[256]; pe[0] = 0x4d; pe[1] = 0x5a; pe[0x3c] = 0x80; pe[0x80] = 0x50; pe[0x81] = 0x45; pe[0x84] = 0x64; pe[0x85] = 0x86;
            string game = Path.Combine(folder, target.Executable); File.WriteAllBytes(game, pe);
            File.WriteAllText(Path.Combine(folder, "UnityPlayer.dll"), "fixture");
            File.WriteAllText(Path.Combine(folder, "GameAssembly.dll"), "fixture");
            Directory.CreateDirectory(Path.Combine(folder, target.DataFolder));
            return game;
        }
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--api-compat") return ApiCompatibilityTests.Run(args[1]);
                if (args.Length == 3 && args[0] == "--payload")
                {
                    var assembly = Assembly.LoadFile(Path.GetFullPath(args[1]));
                    using (var resource = assembly.GetManifestResourceStream("ZenlessSteamBridge.Frontend"))
                    using (var sha = SHA256.Create())
                    {
                        Check(resource != null, "frontend resource exists");
                        byte[] embedded = sha.ComputeHash(resource);
                        using (var file = File.OpenRead(args[2])) Check(embedded.SequenceEqual(sha.ComputeHash(file)), "embedded frontend matches compiled frontend exactly");
                    }
                    Check(assembly.GetManifestResourceNames().Contains("ZenlessSteamBridge.License"), "MIT license embedded");
                    var frontend = Assembly.LoadFile(Path.GetFullPath(args[2]));
                    foreach (string name in new[] { "ZenlessArt", "StarRailArt" })
                    {
                        using (var art = assembly.GetManifestResourceStream("ZenlessSteamBridge." + name))
                        using (var frontArt = frontend.GetManifestResourceStream("ZenlessSteamBridge." + name))
                        using (var sha = SHA256.Create())
                        {
                            Check(art != null && art.Length > 10000, name + " artwork embedded in manager");
                            Check(frontArt != null && sha.ComputeHash(art).SequenceEqual(sha.ComputeHash(frontArt)), name + " matches frontend artwork");
                        }
                    }
                    Console.WriteLine("ALL " + count + " PAYLOAD CHECKS PASSED"); return 0;
                }
                if (args.Length == 2 && args[0] == "--ui") return UiTests(args[1]);
                string root = Path.Combine(Path.GetTempPath(), "ZenlessBridge-tests-空 格-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try { Run(root); }
                finally { Directory.Delete(root, true); }
                Console.WriteLine("ALL " + count + " CORE CHECKS PASSED"); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        private static void Run(string root)
        {
            LaunchRouteTests.Run(root, Check, Throws);
            DualTargetTests(root);
            Check(App.AppId == 4162040 && App.ManifestName == "appmanifest_4162040.acf", "uses ZZZ's own Steam app and manifest");
            Check(!App.Home.Contains("Endfield"), "fork state is isolated from upstream");
            Check(BridgeRuntime.AcceptSteamLaunch("4162040", null, false), "accept ZZZ Steam identity");
            Check(BridgeRuntime.AcceptSteamLaunch(null, "4162040", false), "accept SteamGameId");
            Check(BridgeRuntime.AcceptSteamLaunch(null, null, true), "installed bridge permits missing environment");
            Check(!BridgeRuntime.AcceptSteamLaunch("4732690", null, false), "Endfield identity does not authorize ZZZ launch");
            Check(!BridgeRuntime.AcceptSteamLaunch(null, null, false), "reject missing installation and identity");
            string game = MakeGame(Path.Combine(root, "米哈游 启动器", "games", "ZenlessZoneZero Game"));
            Check(Detection.ValidGame(game), "detect ZZZ PE64 with Unicode and spaces");
            Check(Detection.DetectFromRoot(Path.Combine(root, "米哈游 启动器")) == game, "detect HoYoPlay nested game layout");
            Check(Detection.DetectFromRoot(Path.GetDirectoryName(game)) == game, "detect direct game root");
            Check(Detection.ResolveGame("invalid-selection.exe", game, () => null) == "invalid-selection.exe", "explicit invalid selection is not silently replaced");
            Check(Detection.ResolveGame(null, game, () => null) == game, "saved valid client wins");
            Check(Detection.ResolveGame(null, "missing.exe", () => game) == game, "missing saved client falls back to detection");
            string assembly = Path.Combine(Path.GetDirectoryName(game), "GameAssembly.dll"); File.Delete(assembly);
            Check(!Detection.ValidGame(game), "reject incomplete Unity client"); File.WriteAllText(assembly, "fixture");
            byte[] bytes = File.ReadAllBytes(game); bytes[0x84] = 0x4c; bytes[0x85] = 1; File.WriteAllBytes(game, bytes);
            Check(!Detection.ValidGame(game), "reject x86 executable"); File.WriteAllBytes(game, new byte[16]);
            Check(!Detection.ValidGame(game), "reject truncated executable");
            Check(!Detection.ValidGame(null), "missing game is not valid");
            foreach (string invalid in new[] { "../HYP.exe", @"..\HYP.exe", @"C:\HYP.exe", "/tmp/HYP.exe", "a//HYP.exe", "a:/HYP.exe", "HYP.exe --arg", "a/../HYP.exe", "a./HYP.exe", "HYP.exe\n" })
                Throws(() => InstallFiles.NormalizeExecutable(invalid), "reject unsafe launch path " + invalid.Replace('\n', ' '));
            Check(InstallFiles.NormalizeExecutable(@".\launcher\HYP.exe") == Path.Combine("launcher", "HYP.exe"), "normalize nested Windows launch executable");
            var info = KeyValues.Parse("\"config\" { \"launch\" { \"0\" { \"executable\" \"linux.sh\" \"config\" { \"oslist\" \"linux\" } } \"1\" { \"executable\" \"HYP.exe\" \"type\" \"default\" \"config\" { \"oslist\" \"windows\" } } } } \"depots\" { \"branches\" { \"public\" { \"buildid\" \"123\" } } \"4162041\" { \"manifests\" { \"public\" { \"gid\" \"987\" } } } }");
            var metadata = KeyValues.ReadMetadata(info);
            Check(metadata.Executable == "HYP.exe" && metadata.Build == "123" && metadata.Depots["4162041"] == "987", "read Windows Steam launch and depots without hardcoded Endfield executable");
            KeyValues.Child(KeyValues.Child(info, "config"), "launch").Clear();
            Throws(() => KeyValues.ReadMetadata(info), "reject metadata without Windows launch");
            var vdf = KeyValues.Parse("\"root\" { \"path\" \"D:\\\\游戏\\\\ZZZ\" \"text\" \"quoted\\\"text\" }");
            Check(KeyValues.Render(KeyValues.Parse(KeyValues.Render(vdf))) == KeyValues.Render(vdf), "VDF Unicode and escaping roundtrip");
            Check(Arguments.Quote(@"C:\a b\") == "\"C:\\a b\\\\\"", "Windows trailing slash argument escaping");
            string source = Path.Combine(root, "frontend.exe"); File.WriteAllBytes(source, new byte[] { 77, 90, 1, 2 });
            for (int mode = 0; mode < 4; mode++)
            {
                string steam = Path.Combine(root, "Steam " + mode), home = Path.Combine(root, "data " + mode), manifest = Path.Combine(steam, "steamapps", App.ManifestName);
                Directory.CreateDirectory(Path.GetDirectoryName(manifest));
                byte[] original = Encoding.UTF8.GetBytes("\ufeff\"AppState\"\r\n{\r\n\"installdir\" \"Original game\"\r\n}\r\n");
                string originalGame = Path.Combine(steam, "steamapps", "common", "Original game", "HYP.exe");
                if (mode > 0) { File.WriteAllBytes(manifest, original); Directory.CreateDirectory(Path.GetDirectoryName(originalGame)); File.WriteAllText(originalGame, "keep-original"); }
                var files = new InstallFiles(home);
                var m = new Metadata { Executable = mode == 3 ? Path.Combine("launcher", "HYP.exe") : "HYP.exe", Build = "123", Depots = new Dictionary<string, string> { { "4162041", "987" } } };
                var state = files.Prepare(steam, manifest, source, m, 1, false, "test-task");
                Check(File.Exists(files.StatePath), "record transaction before commit mode=" + mode);
                files.Commit(state);
                Check(File.ReadAllBytes(Path.Combine(state.BridgeDirectory, state.Executable)).SequenceEqual(File.ReadAllBytes(source)), "install bridge at Steam's actual executable path mode=" + mode);
                var installed = KeyValues.Child(KeyValues.Parse(File.ReadAllText(manifest)), "AppState");
                Check(KeyValues.String(installed, "appid") == "4162040", "write ZZZ app manifest mode=" + mode);
                if (mode == 2) { files.Rollback(state); Check(File.ReadAllBytes(manifest).SequenceEqual(original), "rollback restores exact original manifest"); continue; }
                if (mode == 1)
                {
                    Throws(() => files.Prepare(steam, manifest, source, m, 1, false, "test-task"), "repeat enable preserves original backup");
                    File.WriteAllText(Path.Combine(state.BridgeDirectory, "user-note.txt"), "keep");
                }
                files.Restore(state); files.FinishRestore();
                Check(!File.Exists(files.StatePath), "restore clears transaction state mode=" + mode);
                Check(state.WasInstalled ? File.ReadAllBytes(manifest).SequenceEqual(original) : !File.Exists(manifest), "restore original install state mode=" + mode);
                if (mode > 0) Check(File.ReadAllText(originalGame) == "keep-original", "original game file preserved mode=" + mode);
                if (mode == 1) Check(File.ReadAllText(Path.Combine(state.BridgeDirectory, "user-note.txt")) == "keep", "preserve unrelated bridge-directory files");
                if (mode == 3) Check(!Directory.Exists(state.BridgeDirectory), "remove empty nested bridge directories");
            }
            {
                string steam = Path.Combine(root, "failure"), home = Path.Combine(root, "failure-data"), manifest = Path.Combine(steam, "steamapps", App.ManifestName);
                Directory.CreateDirectory(Path.GetDirectoryName(manifest)); File.WriteAllText(manifest, "original");
                var files = new InstallFiles(home);
                Throws(() => files.Prepare(steam, manifest, source + ".missing", metadata, 1, false, "test-task"), "failed install rolls back");
                Check(File.ReadAllText(manifest) == "original" && !File.Exists(files.StatePath), "failed copy leaves original manifest and clears state");
            }
            {
                string steam = Path.Combine(root, "external"), home = Path.Combine(root, "external-data"), manifest = Path.Combine(steam, "steamapps", App.ManifestName);
                var files = new InstallFiles(home); var state = files.Prepare(steam, manifest, source, metadata, 1, false, "test-task"); files.Commit(state);
                File.WriteAllText(manifest, "\"AppState\" { \"installdir\" \"external-change\" }");
                Throws(() => files.Restore(state), "refuse to overwrite externally changed manifest");
                File.Copy(state.ManagedManifestBackup, manifest, true);
                string marker = Path.Combine(state.BridgeDirectory, App.MarkerName); File.WriteAllText(marker, "wrong-token");
                string before = File.ReadAllText(manifest);
                Throws(() => files.Restore(state), "refuse mismatched directory marker");
                Check(File.ReadAllText(manifest) == before, "marker mismatch leaves manifest untouched");
                File.WriteAllText(marker, state.Token); files.Restore(state); files.FinishRestore();
            }
            Check(ScheduledTasks.Describe("创建", new System.Runtime.InteropServices.COMException("service unavailable", unchecked((int)0x80041315))).Contains("Task Scheduler"), "task service failure has useful guidance");
            Check(ScheduledTasks.Describe("创建", new System.Runtime.InteropServices.COMException("unknown", unchecked((int)0x80004005))).Contains("0x80004005"), "task error retains exact HRESULT");
        }
        private static void DualTargetTests(string root)
        {
            string z = MakeGame(Path.Combine(root, "双游戏", "games", "ZenlessZoneZero Game"), GameTarget.Zenless);
            string s = MakeGame(Path.Combine(root, "双游戏", "games", "Star Rail Games"), GameTarget.StarRail);
            var config = new RuntimeConfig { Game = z, StarRailGame = s };
            Check(Detection.ValidGame(s, GameTarget.StarRail), "Star Rail PE64 and data folder accepted");
            Check(!Detection.ValidGame(s, GameTarget.Zenless), "Star Rail cannot occupy ZZZ slot");
            Check(!Detection.ValidGame(z, GameTarget.StarRail), "ZZZ cannot occupy Star Rail slot");
            Check(Detection.DetectFromRoot(Path.Combine(root, "双游戏"), GameTarget.StarRail) == s, "detect Star Rail Games launcher layout");
            Check(GameTarget.AnyReady(config), "both installed clients accepted");
            Check(GameTarget.AnyReady(new RuntimeConfig { StarRailGame = s }), "Star Rail-only setup is supported");
            Check(GameTarget.AnyReady(new RuntimeConfig { Game = z }), "ZZZ-only setup is supported");
            Check(!GameTarget.AnyReady(new RuntimeConfig()), "empty dual setup cannot install");
            foreach (var target in GameTarget.All)
            {
                var request = new BridgeRuntime.Request { GameId = target.Id, Pid = 42, StartTicks = 123, Id = "session" };
                var restored = App.Json.Deserialize<BridgeRuntime.Request>(App.Json.Serialize(request));
                Check(GameTarget.Get(restored.GameId).RequirePath(config) == (target == GameTarget.Zenless ? z : s), target.Id + " selection survives helper handoff and resolves correct EXE");
            }
            Throws(() => GameTarget.Get("endfield"), "reject obsolete target identifier");
            Throws(() => GameTarget.Get("unknown"), "reject unknown target identifier");
            Throws(() => GameTarget.StarRail.RequirePath(new RuntimeConfig { StarRailGame = z }), "helper rejects mismatched game path");
            var old = App.Json.Deserialize<RuntimeConfig>("{\"Game\":" + App.Json.Serialize(z) + "}");
            Check(GameTarget.AnyReady(old) && old.StarRailGame == null, "0.1.0 single-game config remains readable");
            Check(Detection.ResolveGame(null, s, () => null, GameTarget.StarRail) == s, "saved Star Rail path is preserved");
            File.Delete(Path.Combine(Path.GetDirectoryName(s), "GameAssembly.dll"));
            Check(!Detection.ValidGame(s, GameTarget.StarRail), "incomplete Star Rail client is disabled");
        }
        private static IEnumerable<System.Windows.Forms.Button> Buttons(System.Windows.Forms.Control root)
        {
            foreach (System.Windows.Forms.Control child in root.Controls)
            {
                var button = child as System.Windows.Forms.Button;
                if (button != null) yield return button;
                foreach (var nested in Buttons(child)) yield return nested;
            }
        }
        private static int UiTests(string manager)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            var assembly = Assembly.LoadFile(Path.GetFullPath(manager));
            Type configType = assembly.GetType("ZenlessSteamBridge.RuntimeConfig"), pickerType = assembly.GetType("ZenlessSteamBridge.GameSelectionForm");
            string root = Path.Combine(Path.GetTempPath(), "ZenlessBridge-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string z = MakeGame(Path.Combine(root, "ZZZ")), s = MakeGame(Path.Combine(root, "HSR"), GameTarget.StarRail);
                for (int mode = 0; mode < 4; mode++)
                {
                    object config = Activator.CreateInstance(configType);
                    configType.GetProperty("Game").SetValue(config, mode == 2 ? null : z, null);
                    configType.GetProperty("StarRailGame").SetValue(config, mode == 3 ? null : s, null);
                    using (var form = (System.Windows.Forms.Form)Activator.CreateInstance(pickerType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { config, false }, null))
                    {
                        form.Show(); System.Windows.Forms.Application.DoEvents();
                        var buttons = Buttons(form).ToList();
                        var zenless = buttons.Single(b => b.AccessibleName == "启动绝区零");
                        var starRail = buttons.Single(b => b.AccessibleName == "启动崩坏：星穹铁道");
                        Check(zenless.Enabled == (mode != 2), "ZZZ card availability mode=" + mode);
                        Check(starRail.Enabled == (mode != 3), "Star Rail card availability mode=" + mode);
                        if (mode < 2)
                        {
                            (mode == 0 ? zenless : starRail).PerformClick();
                            Check(form.DialogResult == System.Windows.Forms.DialogResult.OK, "game card confirms selection mode=" + mode);
                            Check((string)pickerType.GetProperty("SelectedGame", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form, null) == (mode == 0 ? "zenless" : "starrail"), "game card returns correct identifier mode=" + mode);
                        }
                        else { form.Close(); Check(pickerType.GetProperty("SelectedGame", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form, null) == null, "closing selector chooses no game mode=" + mode); }
                    }
                }
                ManagerLayoutTests(assembly, root);
                Console.WriteLine("ALL " + count + " UI CHECKS PASSED"); return 0;
            }
            finally { Directory.Delete(root, true); }
        }
        private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control root)
        {
            foreach (System.Windows.Forms.Control control in root.Controls)
            { yield return control; foreach (var child in Descendants(control)) yield return child; }
        }
        private static void ManagerLayoutTests(Assembly assembly, string root)
        {
            Type type = assembly.GetType("ZenlessSteamBridge.MainForm");
            foreach (float fontScale in new[] { 1f, 1.5f, 2f })
                using (var form = (System.Windows.Forms.Form)Activator.CreateInstance(type))
                {
                    type.GetMethod("SavePreview", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { Path.Combine(root, "manager-" + fontScale + ".png") });
                    var controls = Descendants(form).ToList();
                    var fonts = controls.ToDictionary(c => c, c => c.Font);
                    if (fontScale != 1)
                        foreach (var control in controls) control.Font = new System.Drawing.Font(fonts[control].FontFamily, fonts[control].SizeInPoints * fontScale, fonts[control].Style);
                    form.ClientSize = new System.Drawing.Size(860, 690); form.Show(); form.PerformLayout(); System.Windows.Forms.Application.DoEvents();
                    foreach (var label in controls.OfType<System.Windows.Forms.Label>().Where(c => !(c is System.Windows.Forms.LinkLabel)))
                        if (label.Height < label.GetPreferredSize(new System.Drawing.Size(label.Width, 0)).Height)
                            throw new Exception("Manager label clipped at font scale " + fontScale + ": " + label.Text);
                    Check(true, "manager labels fit content at font scale=" + fontScale);
                    foreach (var button in controls.OfType<System.Windows.Forms.Button>())
                        if (button.Height < button.GetPreferredSize(new System.Drawing.Size(button.Width, 0)).Height)
                            throw new Exception("Manager button clipped at font scale " + fontScale + ": " + button.Text);
                    Check(true, "manager buttons fit content at font scale=" + fontScale);
                    var viewport = controls.OfType<System.Windows.Forms.Panel>().First(c => c.AutoScroll);
                    Check(viewport.DisplayRectangle.Height >= controls.Max(c => c.Parent == viewport ? c.Bottom : 0), "manager content remains scrollable at font scale=" + fontScale);
                    form.Close();
                }
        }
    }
}
