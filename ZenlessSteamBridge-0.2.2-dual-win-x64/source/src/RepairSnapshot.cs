using System;
using System.Collections.Generic;
using System.IO;

namespace ZenlessSteamBridge
{
    // Keep the original uninstall backups intact when updating an enabled bridge.
    internal sealed class RepairSnapshot
    {
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal RepairSnapshot(string backup, IEnumerable<string> paths)
        {
            Directory.CreateDirectory(backup);
            foreach (string path in paths)
            {
                string copy = null;
                if (File.Exists(path)) { copy = Path.Combine(backup, files.Count.ToString() + ".bak"); File.Copy(path, copy); }
                files.Add(path, copy);
            }
            App.Save(Path.Combine(backup, "files.json"), files);
        }
        internal void Restore()
        {
            foreach (var pair in files)
            {
                if (pair.Value != null)
                {
                    // An unchanged executable can be the currently running manager.
                    if (File.Exists(pair.Key) && SteamLaunchRoute.Hash(pair.Key) == SteamLaunchRoute.Hash(pair.Value)) continue;
                    File.Copy(pair.Value, pair.Key, true);
                }
                else if (File.Exists(pair.Key)) File.Delete(pair.Key);
            }
        }
    }
}
