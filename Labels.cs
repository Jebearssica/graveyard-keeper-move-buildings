using System;

namespace MoveBuildings;

/// <summary>Tiny runtime label lookup so the UI entry follows the game language without extra assets.</summary>
internal static class Labels
{
    private static bool IsChinese
    {
        get
        {
            var language = GameSettings._cur_lng;
            if (string.IsNullOrEmpty(language) && GameSettings.me != null)
            {
                language = GameSettings.me.language;
            }

            return !string.IsNullOrEmpty(language) &&
                   language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static string MoveBuilding => IsChinese ? "移动建筑" : "Move building";

    internal static string PickBuilding => IsChinese ? "选择要移动的建筑" : "pick a building";

    internal static string Back => IsChinese ? "返回" : "back";
}
