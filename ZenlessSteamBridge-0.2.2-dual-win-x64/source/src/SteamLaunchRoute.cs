using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ZenlessSteamBridge
{
    // Replace one scalar in Steam's text VDF, retaining other settings, comments,
    // escaping, line endings and the original encoding. Never render the whole file.
    internal sealed class VdfEdit
    {
        private sealed class Node
        {
            public string Key, Value;
            public int Start, End, ValueStart, Close;
            public List<Node> Children;
        }
        private readonly string text;
        private int position;
        private readonly Node root;
        internal VdfEdit(string source)
        {
            text = source;
            root = new Node { Children = ReadChildren(false, 0), Close = text.Length };
        }
        private void Space()
        {
            while (position < text.Length)
            {
                if (Char.IsWhiteSpace(text[position]) || text[position] == '\ufeff') { position++; continue; }
                if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '/')
                { while (position < text.Length && text[position] != '\n') position++; continue; }
                if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
                {
                    int end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                    if (end < 0) throw new InvalidDataException("Steam 配置注释不完整。");
                    position = end + 2; continue;
                }
                break;
            }
        }
        private string Token()
        {
            Space();
            if (position == text.Length || text[position] == '{' || text[position] == '}') throw new InvalidDataException("Steam 配置结构不完整。");
            var result = new StringBuilder();
            if (text[position] != '"')
            {
                while (position < text.Length && !Char.IsWhiteSpace(text[position]) && text[position] != '{' && text[position] != '}') result.Append(text[position++]);
                if (result.Length == 0 || result[0] == '[') throw new InvalidDataException("Steam 配置包含不支持的条件项，未修改文件。");
                return result.ToString();
            }
            position++;
            while (position < text.Length)
            {
                char c = text[position++];
                if (c == '"') return result.ToString();
                if (c == '\\')
                {
                    if (position == text.Length) break;
                    c = text[position++];
                    if (c == 'n') c = '\n'; else if (c == 'r') c = '\r'; else if (c == 't') c = '\t';
                    else if (c != '\\' && c != '"') result.Append('\\');
                }
                result.Append(c);
            }
            throw new InvalidDataException("Steam 配置字符串不完整。");
        }
        private List<Node> ReadChildren(bool nested, int depth)
        {
            if (depth > 64) throw new InvalidDataException("Steam 配置层级过深。");
            var result = new List<Node>();
            while (true)
            {
                Space();
                if (position == text.Length)
                {
                    if (nested) throw new InvalidDataException("Steam 配置缺少右括号。");
                    return result;
                }
                if (text[position] == '}')
                {
                    if (!nested) throw new InvalidDataException("Steam 配置多出右括号。");
                    return result;
                }
                var node = new Node { Start = position, Key = Token() };
                Space(); node.ValueStart = position;
                if (position < text.Length && text[position] == '{')
                {
                    position++; node.Children = ReadChildren(true, depth + 1); node.Close = position++;
                }
                else node.Value = Token();
                node.End = position; result.Add(node);
            }
        }
        private static Node Child(Node parent, string key)
        {
            if (parent.Children == null) throw new InvalidDataException("Steam 配置中应为分组的位置出现了字符串。");
            var matches = parent.Children.Where(n => String.Equals(n.Key, key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("Steam 配置中存在重复设置项：" + key + "。未修改文件。");
            return matches.SingleOrDefault();
        }
        internal string Get(string[] path, out bool present)
        {
            Node node = root;
            foreach (string key in path) { node = Child(node, key); if (node == null) { present = false; return null; } }
            if (node.Children != null) throw new InvalidDataException("Steam 启动项应为字符串。");
            present = true; return node.Value;
        }
        internal string Set(string[] path, string value)
        {
            Node parent = root;
            for (int i = 0; i < path.Length; i++)
            {
                Node child = Child(parent, path[i]);
                if (child == null)
                {
                    if (value == null) return text;
                    string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                    string inserted = new string('\t', path.Length) + Quote(path[path.Length - 1]) + "\t\t" + Quote(value) + newline;
                    for (int j = path.Length - 2; j >= i; j--)
                    {
                        string indent = new string('\t', j + 1);
                        inserted = indent + Quote(path[j]) + newline + indent + "{" + newline + inserted + indent + "}" + newline;
                    }
                    return text.Insert(parent.Close, newline + inserted + new string('\t', i));
                }
                if (i == path.Length - 1)
                {
                    if (child.Children != null) throw new InvalidDataException("Steam 启动项应为字符串。");
                    return value == null ? text.Remove(child.Start, child.End - child.Start) : text.Remove(child.ValueStart, child.End - child.ValueStart).Insert(child.ValueStart, Quote(value));
                }
                parent = child;
            }
            throw new InvalidOperationException();
        }
        private static string Quote(string value)
        { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\""; }
    }

    internal static class SteamLaunchRoute
    {
        internal static readonly string[] Key = { "UserLocalConfigStore", "Software", "Valve", "Steam", "apps", "4162040", "LaunchOptions" };
        internal static string ConfigPath(string steam, ulong steamId)
        {
            uint account = (uint)(steamId & UInt32.MaxValue);
            if (account == 0) throw new InvalidDataException("Steam 账号编号无效。");
            return Path.Combine(steam, "userdata", account.ToString(System.Globalization.CultureInfo.InvariantCulture), "config", "localconfig.vdf");
        }
        internal static string Command(string frontend)
        {
            string path = Path.GetFullPath(frontend);
            if (path.IndexOfAny(new[] { '"', '\r', '\n', '\0' }) >= 0 || path.IndexOf("%command%", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidDataException("桥接程序所在路径无法用作 Steam 启动项。");
            return Arguments.Quote(path) + " %command%";
        }
        internal static string Hash(string path)
        {
            using (var input = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        private sealed class Document
        {
            public byte[] Bytes, Preamble;
            public Encoding Encoding;
            public string Text;
            public Document(string path)
            {
                if (!File.Exists(path)) throw new IOException("未找到当前账号的 Steam 本地设置。请先登录 Steam 并打开一次绝区零属性，再重试。");
                Bytes = File.ReadAllBytes(path);
                if (Bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("Steam 本地设置文件过大，未修改。");
                int offset = 0; Encoding = new UTF8Encoding(false, true);
                if (Bytes.Length >= 3 && Bytes[0] == 0xef && Bytes[1] == 0xbb && Bytes[2] == 0xbf) offset = 3;
                else if (Bytes.Length >= 2 && Bytes[0] == 0xff && Bytes[1] == 0xfe) { offset = 2; Encoding = new UnicodeEncoding(false, false, true); }
                else if (Bytes.Length >= 2 && Bytes[0] == 0xfe && Bytes[1] == 0xff) { offset = 2; Encoding = new UnicodeEncoding(true, false, true); }
                Preamble = Bytes.Take(offset).ToArray(); Text = Encoding.GetString(Bytes, offset, Bytes.Length - offset);
                if (Text.IndexOf('\0') >= 0) throw new InvalidDataException("Steam 本地设置包含无效字符，未修改。");
            }
            public void Write(string path, string value)
            {
                if (value == Text) return;
                if (!File.ReadAllBytes(path).SequenceEqual(Bytes)) throw new IOException("Steam 设置同时被其他程序更改，请重试。");
                byte[] bytes = Preamble.Concat(Encoding.GetBytes(value)).ToArray();
                string temp = path + ".zsb-" + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temp, bytes); File.Replace(temp, path, null); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        internal static string Read(string path, out bool present)
        { var document = new Document(path); return new VdfEdit(document.Text).Get(Key, out present); }
        internal static LaunchRoute Prepare(string steam, ulong steamId, string frontend, string backup, LaunchRoute previous = null)
        {
            string path = ConfigPath(steam, steamId);
            var document = new Document(path); bool present;
            string value = new VdfEdit(document.Text).Get(Key, out present);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.WriteAllBytes(backup, document.Bytes);
            var route = new LaunchRoute { SteamId = steamId, ConfigPath = path, BackupPath = backup, BeforePresent = present, BeforeValue = value,
                OriginalPresent = present, OriginalValue = value, AppliedValue = Command(frontend) };
            if (previous != null && Detection.Same(previous.ConfigPath, path) && present && value == previous.AppliedValue)
            { route.OriginalPresent = previous.OriginalPresent; route.OriginalValue = previous.OriginalValue; }
            return route;
        }
        internal static void Validate(string steam, LaunchRoute route, string frontend)
        {
            if (route == null || !Detection.Same(route.ConfigPath, ConfigPath(steam, route.SteamId)) || route.AppliedValue != Command(frontend))
                throw new InvalidDataException("Steam 启动入口记录不正确，已停止修改。");
        }
        internal static void Apply(LaunchRoute route)
        {
            var document = new Document(route.ConfigPath); var edit = new VdfEdit(document.Text); bool present;
            string value = edit.Get(Key, out present);
            if (present && value == route.AppliedValue) return;
            if (present != route.BeforePresent || value != route.BeforeValue) throw new IOException("Steam 启动项在备份后发生了变化，请重试。");
            document.Write(route.ConfigPath, edit.Set(Key, route.AppliedValue));
            if (!Matches(route)) throw new IOException("Steam 启动入口写入后校验失败。");
        }
        internal static bool Matches(LaunchRoute route)
        { bool present; return Read(route.ConfigPath, out present) == route.AppliedValue && present; }
        // Restore just our value. Preserve settings changed by the user afterwards.
        internal static void Restore(LaunchRoute route, bool rollback = false)
        {
            if (route == null) return;
            var document = new Document(route.ConfigPath); var edit = new VdfEdit(document.Text); bool present;
            string value = edit.Get(Key, out present);
            if (!present || value != route.AppliedValue) return;
            document.Write(route.ConfigPath, edit.Set(Key, rollback ? (route.BeforePresent ? route.BeforeValue : null) : (route.OriginalPresent ? route.OriginalValue : null)));
        }
        internal static string Check(Installation state, ulong currentSteamId, string frontend, string helper)
        {
            try
            {
                if (state == null) return null;
                InstallFiles.ValidateState(state);
                if (state.Phase != "enabled") return "上次操作尚未完成，请先撤回载入。";
                if (state.LaunchRoute == null) return "旧版没有设置 Steam 启动项，请点击“修复启动入口”。";
                if (state.Version != App.Version) return "已安装组件需要升级，请点击“修复启动入口”。";
                Validate(state.SteamRoot, state.LaunchRoute, frontend);
                if (currentSteamId != 0 && currentSteamId != state.LaunchRoute.SteamId) return "Steam 账号已切换，请切回载入时的账号，或撤回后重新载入。";
                if (!Matches(state.LaunchRoute)) return "Steam 启动项未指向选择界面，请点击“修复启动入口”。";
                string marker = Path.Combine(state.BridgeDirectory, App.MarkerName);
                if (!File.Exists(marker) || File.ReadAllText(marker) != state.Token) return "桥接目录标记不匹配，请保留备份并撤回载入。";
                foreach (string path in new[] { frontend, Path.Combine(state.BridgeDirectory, state.Executable) })
                    if (!File.Exists(path) || String.IsNullOrEmpty(state.FrontendHash) || Hash(path) != state.FrontendHash) return "选择界面组件缺失或被替换，请点击“修复启动入口”。";
                if (!File.Exists(helper) || String.IsNullOrEmpty(state.HelperHash) || Hash(helper) != state.HelperHash) return "辅助组件缺失或被替换，请点击“修复启动入口”。";
                if (!File.Exists(state.ManifestPath)) return "Steam 安装记录已缺失，请点击“修复启动入口”。";
                var manifest = KeyValues.Child(KeyValues.Parse(File.ReadAllText(state.ManifestPath)), "AppState");
                if (KeyValues.String(manifest, "installdir") != App.BridgeFolder) return "Steam 安装目录已改变，请点击“修复启动入口”。";
                return null;
            }
            catch (Exception e) { return "启动入口检查失败：" + e.Message; }
        }
    }
}
