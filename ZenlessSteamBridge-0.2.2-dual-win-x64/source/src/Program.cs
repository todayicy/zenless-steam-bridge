using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ZenlessSteamBridge
{
    internal static class Arguments
    {
        public static string Quote(string s)
        {
            var b = new System.Text.StringBuilder("\""); int slashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { slashes++; continue; }
                b.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); b.Append(c); slashes = 0;
            }
            return b.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
#if BRIDGE_FRONTEND
                return BridgeRuntime.Run();
#else
                if (args.Length == 2 && args[0] == "--preview-selector")
                { using (var picker = new GameSelectionForm(new RuntimeConfig(), true)) picker.SavePreview(args[1]); return 0; }
                if (args.Length == 2 && args[0] == "--preview-selector-disabled")
                { using (var picker = new GameSelectionForm(new RuntimeConfig())) picker.SavePreview(args[1]); return 0; }
                if (args.Length == 2 && args[0] == "--preview")
                { using (var form = new MainForm()) form.SavePreview(args[1]); return 0; }
                if (args.Length == 2 && args[0] == "--probe") { App.Save(args[1], SteamAccount.Probe(Detection.FindSteam())); return 0; }
                if (args.Length == 2 && args[0] == "--diagnose") { App.Save(args[1], Detection.Scan(null)); return 0; }
                if (args.Length == 2 && args[0] == "--metadata") { App.Save(args[1], KeyValues.ReadAppInfo(Path.Combine(Detection.FindSteam(), "appcache", "appinfo.vdf"))); return 0; }
                if (args.Length == 1 && args[0] == "--assist") return BridgeRuntime.RunAssist();
                bool created;
                using (var mutex = new Mutex(true, @"Local\ZenlessSteamBridgeManager", out created))
                {
                    if (!created) return 0;
                    using (var form = new MainForm()) Application.Run(form);
                }
                return 0;
#endif
            }
            catch (Exception error) { MessageBox.Show(error.Message, "Zenless Steam Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
    }
}
