using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveBuildings;

/// <summary>
///     Re-applies the craft list layout one frame after a build window opens.
///     The vanilla window lays the list out while every row is still active and then hides the rows of the other tabs;
///     with a short list (the garden desk for example) the table can end up leaving rows on top of each other, which
///     shows up as all buildings squeezed into one line. Running the layout once more with the final set of visible rows
///     puts them back in order.
/// </summary>
internal static class CraftLayout
{
    internal const string RemoveCraftId = "_remove_";

    internal static void ScheduleRepair(CraftGUI craftGui)
    {
        if (craftGui == null)
        {
            return;
        }

        GJTimer.AddTimer(0.03f, delegate { Repair(craftGui); });
    }

    internal static void Repair(CraftGUI craftGui)
    {
        try
        {
            RepairUnsafe(craftGui);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Craft list repair failed: " + e.Message);
        }
    }

    private static void RepairUnsafe(CraftGUI craftGui)
    {
        if (craftGui == null || !craftGui.is_shown)
        {
            return;
        }

        var grid = craftGui.craft_list_grid;
        if (grid == null || craftGui.list_items == null || craftGui.list_items.Count == 0)
        {
            return;
        }

        var desk = GameAccess.GetCraftery(craftGui);
        var tabId = desk != null ? desk.last_opened_tab : string.Empty;
        var active = new List<CraftItemGUI>();

        for (var i = craftGui.list_items.Count - 1; i >= 0; i--)
        {
            var item = craftGui.list_items[i];
            if (item == null || item.gameObject == null)
            {
                craftGui.list_items.RemoveAt(i);
                continue;
            }

            var craft = item.craft_definition;
            var visible = craft != null &&
                          (craft.tab_id == tabId || craft.id == RemoveCraftId || craft.id == BuildMoveMode.MoveCraftId);

            item.SetActive(visible);
            if (visible)
            {
                active.Add(item);
            }
        }

        for (var i = 0; i < active.Count; i++)
        {
            var item = active[i];
            if (item.transform.localPosition != Vector3.zero)
            {
                return;
            }
        }

        // Every visible row still sits at the origin, so the table never laid them out: do it now.
        grid.Reposition();
        grid.repositionNow = true;
    }
}
