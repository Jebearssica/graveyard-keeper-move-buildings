using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MoveBuildings;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; }

    internal static ConfigEntry<bool> AllowNonBuildable { get; private set; }

    internal static ConfigEntry<bool> ShowBuildGrid { get; private set; }

    internal static ConfigEntry<bool> SelfTest { get; private set; }

    private void Awake()
    {
        Log = Logger;

        AllowNonBuildable = Config.Bind(
            "General",
            "AllowNonBuildableObjects",
            false,
            "Also allow moving world objects that only have a removal craft (trees, rocks, quest props).\n" +
            "同时允许移动只有拆除配方的世界物件（树木、石头、任务道具）。");

        ShowBuildGrid = Config.Bind(
            "General",
            "ShowBuildGrid",
            true,
            "Show the building grid while an object is being moved.\n" +
            "移动建筑时显示建造网格。");

        SelfTest = Config.Bind(
            "Debug",
            "SelfTest",
            false,
            "Runs a one-shot build-UI pick-up/commit cycle on an existing object after loading a save and logs PASS/FAIL.\n" +
            "读取存档后自动走一遍建造界面里的“移动建筑”流程并输出 PASS/FAIL。仅用于验证。");

        Harmony.CreateAndPatchAll(typeof(Patches), MyPluginInfo.PLUGIN_GUID);

        Logger.LogInfo("Move Buildings loaded.");
    }
}
