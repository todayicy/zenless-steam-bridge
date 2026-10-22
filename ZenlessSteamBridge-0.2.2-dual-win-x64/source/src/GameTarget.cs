using System;
using System.IO;

namespace ZenlessSteamBridge
{
    internal sealed class GameTarget
    {
        internal static readonly GameTarget Zenless = new GameTarget("zenless", "绝区零", "ZENLESS ZONE ZERO", "ZenlessZoneZero.exe", "ZenlessZoneZero_Data", "ZenlessArt", new[] { "ZenlessZoneZero Game", "ZenlessZoneZero" });
        internal static readonly GameTarget StarRail = new GameTarget("starrail", "崩坏：星穹铁道", "HONKAI: STAR RAIL", "StarRail.exe", "StarRail_Data", "StarRailArt", new[] { "Star Rail Games", "Star Rail", "Honkai Star Rail", "崩坏：星穹铁道" });
        internal static readonly GameTarget[] All = new[] { Zenless, StarRail };
        internal readonly string Id, Name, EnglishName, Executable, DataFolder, ArtResource;
        internal readonly string[] FolderNames;
        private GameTarget(string id, string name, string english, string executable, string dataFolder, string art, string[] folders)
        { Id = id; Name = name; EnglishName = english; Executable = executable; DataFolder = dataFolder; ArtResource = art; FolderNames = folders; }
        internal string FilePath(RuntimeConfig config)
        { return config == null ? null : Id == StarRail.Id ? config.StarRailGame : config.Game; }
        internal static GameTarget Get(string id)
        {
            foreach (var target in All) if (target.Id == id) return target;
            throw new InvalidDataException("无法识别所选游戏：" + (id ?? "<未选择>"));
        }
        internal static bool AnyReady(RuntimeConfig config)
        {
            foreach (var target in All) if (Detection.ValidGame(target.FilePath(config), target)) return true;
            return false;
        }
        internal string RequirePath(RuntimeConfig config)
        {
            string path = FilePath(config);
            if (!Detection.ValidGame(path, this)) throw new InvalidDataException(Name + "客户端不可用，请在管理界面选择正确的 " + Executable + "，再重新载入。");
            return Path.GetFullPath(path);
        }
    }
}
