using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZenlessSteamBridge
{
    internal static class LaunchRouteTests
    {
        internal static void Run(string root, Action<bool, string> check, Action<Action, string> throws)
        {
            string steam = Path.Combine(root, "Steam 启动测试"), frontend = Path.Combine(root, "组件 空格", "ZenlessBridge.exe");
            const ulong owner = 76561197960265728UL + 12345;
            string file = SteamLaunchRoute.ConfigPath(steam, owner);
            Directory.CreateDirectory(Path.GetDirectoryName(file)); Directory.CreateDirectory(Path.GetDirectoryName(frontend)); File.WriteAllText(frontend, "compiled-frontend-fixture");
            string before = "\"UserLocalConfigStore\"\r\n{\r\n\t// keep comment and whitespace\r\n\t\"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"999\" { \"LaunchOptions\" \"unrelated %command%\" }\r\n\"4162040\" { \"LaunchOptions\" \"-old-option 中文\" \"LastPlayed\" \"123\" } } } } }\r\n\"Other\" \"keep\\\\path\"\r\n}\r\n";
            string backup = Path.Combine(root, "route-backup", "localconfig.vdf");
            File.WriteAllText(file, before, new UTF8Encoding(true));
            byte[] original = File.ReadAllBytes(file);
            var route = SteamLaunchRoute.Prepare(steam, owner, frontend, backup);
            check(file.Contains(Path.Combine("userdata", "12345", "config")), "launch route uses the logged-in Steam account ID");
            check(route.BeforePresent && route.OriginalValue == "-old-option 中文", "capture previous per-app launch options");
            check(File.ReadAllBytes(backup).SequenceEqual(original), "keep exact full config backup including BOM");
            check(route.AppliedValue == Arguments.Quote(Path.GetFullPath(frontend)) + " %command%", "Steam wrapper quotes selector path with Unicode and spaces");
            SteamLaunchRoute.Validate(steam, route, frontend); SteamLaunchRoute.Apply(route);
            check(SteamLaunchRoute.Matches(route), "read back exact selector command after write");
            check(File.ReadAllBytes(file).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "launch edit preserves UTF-8 BOM");
            string expected = before.Replace("\"-old-option 中文\"", VdfValue(route.AppliedValue));
            check(File.ReadAllText(file) == expected, "edit only target scalar; retain comments, casing, CRLF and other apps");
            File.WriteAllText(file, File.ReadAllText(file).Replace("\"LastPlayed\" \"123\"", "\"LastPlayed\" \"456\""), new UTF8Encoding(true));
            SteamLaunchRoute.Restore(route);
            check(File.ReadAllText(file) == before.Replace("\"LastPlayed\" \"123\"", "\"LastPlayed\" \"456\""), "uninstall restores previous options while retaining later unrelated Steam writes");
            SteamLaunchRoute.Apply(route);
            var upgraded = SteamLaunchRoute.Prepare(steam, owner, frontend, backup + ".upgrade", route);
            check(upgraded.OriginalValue == "-old-option 中文" && upgraded.BeforeValue == route.AppliedValue, "in-place upgrade keeps the original uninstall value");
            SteamLaunchRoute.Apply(upgraded); SteamLaunchRoute.Restore(upgraded, true);
            check(SteamLaunchRoute.Matches(route), "failed upgrade restores immediately preceding launch route");
            SteamLaunchRoute.Restore(upgraded);
            check(File.ReadAllText(file).Contains("-old-option 中文"), "successful upgrade can still uninstall to original options");
            SteamLaunchRoute.Apply(route);
            File.WriteAllText(file, new VdfEdit(File.ReadAllText(file)).Set(SteamLaunchRoute.Key, "user changed later"));
            SteamLaunchRoute.Restore(route);
            bool present;
            check(SteamLaunchRoute.Read(file, out present) == "user changed later", "uninstall preserves a user-edited launch option");
            var editRoute = SteamLaunchRoute.Prepare(steam, owner, frontend, backup + ".edited", route);
            check(editRoute.OriginalValue == "user changed later", "repair retains the most recent external launch option for uninstall");
            File.WriteAllText(file, new VdfEdit(File.ReadAllText(file)).Set(SteamLaunchRoute.Key, "racing edit"));
            throws(() => SteamLaunchRoute.Apply(editRoute), "refuse to overwrite a launch option changed since backup");
            foreach (Encoding encoding in new Encoding[] { new UTF8Encoding(false), new UnicodeEncoding(false, true) })
            {
                foreach (string initial in new[] { "\"UserLocalConfigStore\" { \"friends\" { \"nickname\" \"阿绳\" } }", "\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { } } } } }" })
                {
                    File.WriteAllText(file, initial, encoding);
                    var fresh = SteamLaunchRoute.Prepare(steam, owner, frontend, backup + ".fresh");
                    check(!fresh.OriginalPresent, "absent launch option is distinct from empty string " + encoding.WebName);
                    SteamLaunchRoute.Apply(fresh);
                    check(SteamLaunchRoute.Matches(fresh), "create missing nested Steam app setting " + encoding.WebName);
                    check(File.ReadAllBytes(file).Take(encoding.GetPreamble().Length).SequenceEqual(encoding.GetPreamble()), "retain config encoding " + encoding.WebName);
                    SteamLaunchRoute.Restore(fresh);
                    SteamLaunchRoute.Read(file, out present);
                    check(!present, "uninstall removes previously absent launch option " + encoding.WebName);
                    if (initial.Contains("nickname")) check(File.ReadAllText(file).Contains("阿绳"), "unrelated Unicode account settings survive nested insertion");
                }
            }
            File.WriteAllText(file, new VdfEdit(before).Set(SteamLaunchRoute.Key, ""));
            var empty = SteamLaunchRoute.Prepare(steam, owner, frontend, backup + ".empty");
            SteamLaunchRoute.Apply(empty); SteamLaunchRoute.Restore(empty);
            check(SteamLaunchRoute.Read(file, out present) == "" && present, "uninstall retains an originally present empty launch option");
            foreach (string bad in new[] { before.Replace("\"LaunchOptions\" \"-old-option 中文\"", "\"LaunchOptions\" \"a\" \"launchoptions\" \"b\""), before.Substring(0, before.LastIndexOf('}')), "\"UserLocalConfigStore\" \"wrong-type\"" })
            {
                File.WriteAllText(file, bad); byte[] bytes = File.ReadAllBytes(file);
                throws(() => SteamLaunchRoute.Prepare(steam, owner, frontend, backup + ".bad"), "reject ambiguous or malformed target config before mutation");
                check(File.ReadAllBytes(file).SequenceEqual(bytes), "malformed config remains byte-for-byte unchanged");
            }
            File.WriteAllText(file, before);
            var install = new InstallFiles(Path.Combine(root, "route-install"));
            var state = install.Prepare(steam, Path.Combine(steam, "steamapps", App.ManifestName), frontend, new Metadata { Executable = "HYP.exe", Build = "1" }, owner, false, "task");
            state.LaunchRoute = SteamLaunchRoute.Prepare(steam, owner, frontend, backup + ".install");
            state.FrontendHash = SteamLaunchRoute.Hash(frontend); state.HelperHash = state.FrontendHash;
            App.Save(install.StatePath, state); SteamLaunchRoute.Apply(state.LaunchRoute); install.Commit(state);
            check(SteamLaunchRoute.Check(state, owner, frontend, frontend) == null, "install verification covers selector route, payload hash, marker and manifest");
            check(SteamLaunchRoute.Check(state, owner + 1, frontend, frontend).Contains("账号已切换"), "detect account switch instead of declaring launch route ready");
            File.WriteAllText(Path.Combine(state.BridgeDirectory, state.Executable), "official-launcher-replaced-bridge");
            check(SteamLaunchRoute.Check(state, owner, frontend, frontend).Contains("组件缺失或被替换"), "detect a Steam update replacing the selector executable");
            throws(() => install.Commit(state), "commit refuses a replaced frontend instead of showing success");
            File.Copy(frontend, Path.Combine(state.BridgeDirectory, state.Executable), true);
            check(BridgeRuntime.OwnsFrontend(Path.Combine(state.BridgeDirectory, state.Executable), state), "authorize installed Steam compatibility frontend");
            check(BridgeRuntime.OwnsFrontend(App.FrontendExe, state), "authorize stable selector executable used by Steam launch options");
            check(!BridgeRuntime.OwnsFrontend(Path.Combine(root, "HYP.exe"), state), "reject unrelated official launcher as frontend owner");
            install.Rollback(state);
            check(SteamLaunchRoute.Read(file, out present) == "-old-option 中文" && !File.Exists(install.StatePath), "failed install rolls back both Steam manifest and per-account launch options");
            var legacy = new Installation { BridgeDirectory = Path.Combine(steam, "steamapps", "common", App.BridgeFolder), ManifestPath = Path.Combine(steam, "steamapps", App.ManifestName), SteamRoot = steam, Executable = "HYP.exe", Phase = "enabled" };
            check(SteamLaunchRoute.Check(legacy, owner, frontend, frontend).Contains("旧版没有设置"), "old enabled state is reported as needing route repair");
            string a = Path.Combine(root, "snapshot-existing"), b = Path.Combine(root, "snapshot-new"); File.WriteAllText(a, "old");
            var snapshot = new RepairSnapshot(Path.Combine(root, "snapshot"), new[] { a, b });
            File.WriteAllText(a, "new"); File.WriteAllText(b, "created"); snapshot.Restore();
            check(File.ReadAllText(a) == "old" && !File.Exists(b), "failed repair restores old components and removes newly created ones");
        }
        private static string VdfValue(string value)
        { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
    }
}
