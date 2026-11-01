using HarmonyLib;

namespace MoveBuildings;

internal static class Patches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MainGame), nameof(MainGame.Update))]
    private static void MainGame_Update_Postfix()
    {
        MoveSession.Tick();
        BuildMoveMode.Tick();
        SelfTest.Tick();
#if DEMO
        Demo.Tick();
#endif
    }

    /// <summary>Adds the "move building" row to the build desk window, right after the vanilla "remove" row.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CraftGUI), "AddRemoveBuildingItem")]
    private static void CraftGUI_AddRemoveBuildingItem_Postfix(CraftGUI __instance)
    {
        BuildMoveMode.AddCraftListEntry(__instance);
    }

    /// <summary>Turns the extra craft-list row into the pick-a-building mode instead of a normal craft.</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CraftGUI), nameof(CraftGUI.OnCraft))]
    private static bool CraftGUI_OnCraft_Prefix(CraftDefinition craft, ref bool __result)
    {
        if (craft == null || craft.id != BuildMoveMode.MoveCraftId)
        {
            return true;
        }

        BuildMoveMode.Enter();
        __result = true;
        return false;
    }

    /// <summary>Tab switching only keeps "_remove_" active by default; keep the move row alive the same way.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CraftGUI), "SwitchTab")]
    private static void CraftGUI_SwitchTab_Postfix(CraftGUI __instance)
    {
        BuildMoveMode.KeepCraftListEntryVisible(__instance);
        CraftLayout.ScheduleRepair(__instance);
    }

    /// <summary>Re-runs the list layout once after the window has settled, so rows never stay stacked on one line.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CraftGUI), "ProceedOpen")]
    private static void CraftGUI_ProceedOpen_Postfix(CraftGUI __instance)
    {
        CraftLayout.ScheduleRepair(__instance);
    }
}
