using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveBuildings;

/// <summary>
///     Build-UI driven move mode. The extra craft-list entry added next to "remove" enters a pick state: the player
///     clicks a building in the world, it becomes the vanilla floating preview, and placing it ends up in
///     <see cref="MoveSession" />. Right click / back leaves the mode and reopens the build desk window.
/// </summary>
internal static class BuildMoveMode
{
    internal const string MoveCraftId = "_move_";

    private const string MoveIconName = "i_hand_one";

    private static bool _picking;

    internal static bool Active => _picking;

    /// <summary>Adds the "move building" entry to the craft list, right before the vanilla "remove" entry.</summary>
    internal static void AddCraftListEntry(CraftGUI craftGui)
    {
        // Never let a failure here escape: the caller still has to lay the vanilla rows out afterwards, and an exception
        // leaving this postfix would leave every row stacked at the origin.
        try
        {
            AddCraftListEntryUnsafe(craftGui);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Could not add the move entry: " + e);
        }
    }

    private static void AddCraftListEntryUnsafe(CraftGUI craftGui)
    {
        if (craftGui == null || craftGui.craft_item_prefab == null)
        {
            return;
        }

        RemoveStaleEntries(craftGui);

        var craft = new ObjectCraftDefinition
        {
            id = MoveCraftId,
            icon = MoveIconName,
            custom_name = Labels.MoveBuilding
        };

        var item = craftGui.craft_item_prefab.Copy();
        GJL.EnsureChildLabelsHasCorrectFont(item.gameObject, false);
        item.Draw(craft);

        // Same blue build tile as the neighbouring rows, but with a four-direction arrow.
        var icon = MoveIcon.Get();
        if (icon != null && item.item_gui != null && item.item_gui.x1 != null)
        {
            item.item_gui.x1.icon.DrawAndResize(icon);
        }

        // Sit directly in front of the "remove" row: appended last it would be scrolled out of view in short lists.
        // Both the list index and the transform index are taken from the remove row, because the hierarchy also holds
        // the inactive prefab and possibly rows that are being destroyed.
        var removeIndex = craftGui.list_items.FindIndex(i =>
            i != null && i.craft_definition != null && i.craft_definition.id == "_remove_");

        var listIndex = removeIndex >= 0 ? removeIndex : craftGui.list_items.Count;
        craftGui.list_items.Insert(listIndex, item);
        item.SetActive(true);

        var removeRow = removeIndex >= 0 ? craftGui.list_items[removeIndex + 1] : null;
        if (removeRow != null)
        {
            item.transform.SetSiblingIndex(removeRow.transform.GetSiblingIndex());
        }

        if (craftGui.craft_list_grid != null)
        {
            craftGui.craft_list_grid.Reposition();
            craftGui.craft_list_grid.repositionNow = true;
        }
    }

    /// <summary>Rows survive a GUI that was closed on the same frame (Unity destroys them a frame later), so drop them.</summary>
    private static void RemoveStaleEntries(CraftGUI craftGui)
    {
        for (var i = craftGui.list_items.Count - 1; i >= 0; i--)
        {
            var existing = craftGui.list_items[i];
            if (existing == null || existing.craft_definition == null ||
                existing.craft_definition.id != MoveCraftId)
            {
                continue;
            }

            craftGui.list_items.RemoveAt(i);
            if (existing.gameObject != null)
            {
                existing.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(existing.gameObject);
            }
        }
    }

    /// <summary>Called when the player picks the move entry in the build desk window.</summary>
    internal static void Enter()
    {
        if (_picking)
        {
            return;
        }

        var me = MainGame.me;
        if (me == null || !MainGame.game_started || me.build_mode_logics == null)
        {
            return;
        }

        // Same hand-off the vanilla "_remove_" entry performs.
        GUIElements.me.craft.Hide();
        BuildGridView.Hide();

        me.EnterBuildMode();

        var zone = me.build_mode_logics.cur_build_zone;
        if (zone != null)
        {
            BuildGridView.ShowAt(zone.center_tf.position);
        }

        GUIElements.me.hud.Hide();
        GUIElements.me.build_mode_gui.Open();

        _picking = true;
        RedrawHints();

        Plugin.Log.LogInfo($"Entered move mode (zone='{me.build_mode_logics.cur_build_zone_id}')");
    }

    internal static void Tick()
    {
        if (!_picking)
        {
            return;
        }

        try
        {
            var me = MainGame.me;
            if (!MainGame.game_started || me == null || me.player == null)
            {
                Stop(false);
                return;
            }

            if (me.game_mode != MainGame.GameMode.Building)
            {
                _picking = false;
                return;
            }

            if (MoveSession.Active)
            {
                return;
            }

            var buildGui = GUIElements.me != null ? GUIElements.me.build_mode_gui : null;
            if (buildGui == null || !buildGui.is_shown)
            {
                _picking = false;
                return;
            }

            if (LazyInput.GetKeyDown(GameKey.Back) || LazyInput.GetKeyDown(GameKey.RightClick))
            {
                LazyInput.ClearKeyDown(GameKey.Back);
                Stop(true);
                return;
            }

            RedrawHints();

            if (!MoveSession.IsConfirmPressed())
            {
                return;
            }

            var target = MoveSession.TryFindTargetUnderCursor();
            if (target == null)
            {
                return;
            }

            if (!MoveSession.StartFromBuildMode(target))
            {
                Plugin.Log.LogInfo($"Cannot move '{target.obj_id}'");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Move mode error: " + e);
            Stop(false);
        }
    }

    /// <summary>Leaves the pick mode; optionally reopens the build desk window like vanilla does after removing.</summary>
    internal static void Stop(bool reopenBuildGui)
    {
        _picking = false;

        var me = MainGame.me;
        if (me == null)
        {
            return;
        }

        if (BuildGrid.me != null)
        {
            BuildGridView.Hide();
            BuildGrid.me.ClearPreviousTotemRadius(true);
        }

        if (me.gui_elements != null && me.gui_elements.build_mode_gui != null)
        {
            me.gui_elements.build_mode_gui.Hide();
        }

        if (me.game_mode == MainGame.GameMode.Building)
        {
            me.ExitBuildMode();
        }

        if (reopenBuildGui && BuildModeLogics.last_build_desk != null)
        {
            // Reopen one frame later: closing the window queues the old rows for destruction, and cloning new rows in
            // the same frame leaves the table laying out both sets, which ends up stacking every row on one line.
            var desk = BuildModeLogics.last_build_desk;
            GJTimer.AddTimer(0.02f, delegate
            {
                if (MainGame.me != null && MainGame.game_started && !BaseGUI.all_guis_closed)
                {
                    return;
                }

                MainGame.me.OpenBuildObjectGUI(desk);
            });

            return;
        }

        if (me.build_mode_logics != null)
        {
            me.build_mode_logics.SetCurrentBuildZone(string.Empty);
        }

        Plugin.Log.LogInfo("Left move mode");
    }

    /// <summary>Keeps the extra row visible on every craft tab, mirroring how the vanilla "remove" row behaves.</summary>
    internal static void KeepCraftListEntryVisible(CraftGUI craftGui)
    {
        try
        {
            KeepVisibleUnsafe(craftGui);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Could not keep the move entry visible: " + e.Message);
        }
    }

    private static void KeepVisibleUnsafe(CraftGUI craftGui)
    {
        if (craftGui == null)
        {
            return;
        }

        var changed = false;
        foreach (var item in craftGui.list_items)
        {
            if (item == null || item.craft_definition == null || item.craft_definition.id != MoveCraftId)
            {
                continue;
            }

            if (!item.gameObject.activeSelf)
            {
                item.SetActive(true);
                changed = true;
            }
        }

        if (changed && craftGui.craft_list_grid != null)
        {
            craftGui.craft_list_grid.Reposition();
        }
    }

    internal static void RedrawHints()
    {
        var buildGui = GUIElements.me != null ? GUIElements.me.build_mode_gui : null;
        if (buildGui == null)
        {
            return;
        }

        var tips = new List<GameKeyTip>
        {
            new(GameKey.LeftClick, Labels.PickBuilding, true, false, false),
            new(GameKey.Back, Labels.Back, true, false, false)
        };

        buildGui.button_tips.Print(tips);
    }
}
