using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveBuildings;

/// <summary>
///     Optional in-game smoke test (<c>SelfTest</c> config entry, disabled by default). After a save is loaded it walks
///     the real build-desk UI path: opens the desk window, verifies the extra "move building" entry, activates it,
///     picks an existing object (a chest with items when available), commits and verifies that the stored inventory
///     survived and that the save serializer sees the new position. The object is left on the build grid (vanilla
///     placement snaps), so enable this only for verification runs.
/// </summary>
internal static class SelfTest
{
    private static bool _done;
    private static float _startedAt = -1f;

    internal static void Tick()
    {
        if (_done || !Plugin.SelfTest.Value)
        {
            return;
        }

        if (!MainGame.game_started || MainGame.me == null || MainGame.me.player == null)
        {
            return;
        }

        if (_startedAt < 0f)
        {
            _startedAt = Time.realtimeSinceStartup;
        }

        if (Time.realtimeSinceStartup - _startedAt < 5f)
        {
            return;
        }

        if (MainGame.me.game_mode != MainGame.GameMode.Normal || !BaseGUI.all_guis_closed || MoveSession.Active)
        {
            if (Time.realtimeSinceStartup - _startedAt > 120f)
            {
                _done = true;
                Plugin.Log.LogWarning("[SELFTEST] SKIP: no clean gameplay window detected");
            }

            return;
        }

        _done = true;
        Run();
    }

    private static WorldGameObject _target;
    private static WorldGameObject _desk;

    private static void Run()
    {
        try
        {
            var movable = new List<WorldGameObject>();
            foreach (var wgo in WorldMap.objs)
            {
                if (MoveSession.IsMovableForTest(wgo))
                {
                    movable.Add(wgo);
                }
            }

            Plugin.Log.LogInfo($"[SELFTEST] WorldMap objs={WorldMap.objs.Count}, movable={movable.Count}");

            var withItems = movable.FindAll(w => CountItems(w.data) > 0);
            for (var i = 0; i < withItems.Count && i < 10; i++)
            {
                Plugin.Log.LogInfo(
                    $"[SELFTEST] candidate '{withItems[i].obj_id}' items={CountItems(withItems[i].data)} zone='{withItems[i].GetMyWorldZoneId()}'");
            }

            var target = withItems.Find(w => w.obj_id.Contains("chest"))
                         ?? (withItems.Count > 0 ? withItems[0] : movable.Find(w => !string.IsNullOrEmpty(w.GetMyWorldZoneId())))
                         ?? (movable.Count > 0 ? movable[0] : null);

            var desk = FindBuildDesk();
            if (target == null || desk == null)
            {
                Plugin.Log.LogWarning($"[SELFTEST] SKIP: target={Describe(target)} desk={Describe(desk)}");
                return;
            }

            Plugin.Log.LogInfo($"[SELFTEST] target '{target.obj_id}' items={CountItems(target.data)} desk='{desk.obj_id}'");

            // Open the build desk window exactly like the player interaction does.
            MainGame.me.OpenBuildObjectGUI(desk);
            var craftGui = GUIElements.me.craft;

            if (craftGui == null || !craftGui.is_shown)
            {
                Plugin.Log.LogError("[SELFTEST] FAIL: build window did not open");
                return;
            }

            var moveEntry = craftGui.list_items.Find(item =>
                item != null && item.craft_definition != null && item.craft_definition.id == BuildMoveMode.MoveCraftId);

            Plugin.Log.LogInfo(
                $"[SELFTEST] build window rows={craftGui.list_items.Count}, move entry present={moveEntry != null}");

            if (moveEntry == null)
            {
                Plugin.Log.LogError("[SELFTEST] FAIL: move entry missing from build window");
                craftGui.Hide();
                return;
            }

            Plugin.Log.LogInfo(
                $"[SELFTEST] move entry label='{moveEntry.label_name.text}' icon='{moveEntry.craft_definition.icon}' " +
                $"index={craftGui.list_items.IndexOf(moveEntry)}/{craftGui.list_items.Count} active={moveEntry.gameObject.activeSelf}");

            _target = target;
            _desk = desk;

            // Give the row a moment to be drawn before the click is simulated.
            GJTimer.AddTimer(2f, RunAfterWindowShown);
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("[SELFTEST] FAIL: " + e);
        }
    }

    private static void RunAfterWindowShown()
    {
        try
        {
            var target = _target;
            var craftGui = GUIElements.me.craft;
            if (target == null || craftGui == null)
            {
                Plugin.Log.LogError("[SELFTEST] FAIL: build window disappeared before the click");
                return;
            }

            var moveEntry = craftGui.list_items.Find(item =>
                item != null && item.craft_definition != null && item.craft_definition.id == BuildMoveMode.MoveCraftId);

            if (moveEntry == null)
            {
                Plugin.Log.LogError("[SELFTEST] FAIL: move entry disappeared");
                return;
            }

            Plugin.Log.LogInfo(
                $"[SELFTEST] before click: entry active={moveEntry.gameObject.activeSelf}");

            // Same call the row click performs.
            craftGui.OnCraft(moveEntry.craft_definition);
            Plugin.Log.LogInfo(
                $"[SELFTEST] after entry click: move_mode={BuildMoveMode.Active}, game_mode={MainGame.me.game_mode}");

            if (!BuildMoveMode.Active)
            {
                Plugin.Log.LogError("[SELFTEST] FAIL: move mode did not start");
                return;
            }

            var posBefore = target.transform.position;
            var itemsBefore = CountItems(target.data);

            var nearest = FindNearestMovableToPlayer();
            if (nearest != null)
            {
                var probe = GetProbePoint(nearest);
                var hoverPick = MoveSession.FindTargetAtTest(probe);
                Plugin.Log.LogInfo(
                    $"[SELFTEST] hover pick near player: '{nearest.obj_id}' at {probe} -> '{(hoverPick == null ? "null" : hoverPick.obj_id)}', match={hoverPick == nearest}");
            }

            if (!MoveSession.StartFromBuildMode(target))
            {
                Plugin.Log.LogError("[SELFTEST] FAIL: pick-up did not start");
                BuildMoveMode.Stop(false);
                return;
            }

            MoveSession.CommitForTest();

            var posAfter = target.transform.position;
            var itemsAfter = CountItems(target.data);
            var serialized = SerializableWGO.FromWGO(target);
            var serializedItems = CountItems(serialized.item);
            var serializedPosOk = ((Vector3)serialized.position - posAfter).sqrMagnitude < 0.01f;

            // Commit uses the vanilla placement code, which snaps to the 96px build grid.
            var snapDistance = Vector2.Distance(posBefore, posAfter);
            var positionOk = snapDistance <= 96.1f;
            var inventoryOk = itemsAfter == itemsBefore && serializedItems == itemsBefore;

            BuildMoveMode.Stop(true);
            var reopened = GUIElements.me.craft != null && GUIElements.me.craft.is_shown;
            if (reopened)
            {
                GUIElements.me.craft.Hide();
            }

            if (MainGame.me.build_mode_logics != null)
            {
                MainGame.me.build_mode_logics.SetCurrentBuildZone(string.Empty);
            }

            var cleanedUp = !BuildMoveMode.Active && !MoveSession.Active &&
                            MainGame.me.game_mode == MainGame.GameMode.Normal && reopened;

            Plugin.Log.LogInfo(
                $"[SELFTEST] obj='{target.obj_id}' desk='{(_desk == null ? "null" : _desk.obj_id)}' " +
                $"pos {posBefore} -> {posAfter} (snap {snapDistance:0.#}px) | " +
                $"items {itemsBefore} -> {itemsAfter} (serialized {serializedItems}) | serialized pos={serialized.position} | " +
                $"build window reopened={reopened} | cleanup={cleanedUp} | " +
                (positionOk && serializedPosOk && inventoryOk && cleanedUp ? "PASS" : "FAIL"));
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("[SELFTEST] FAIL: " + e);
        }
    }

    private static WorldGameObject FindBuildDesk()
    {
        WorldGameObject best = null;
        var bestDistance = float.MaxValue;
        var playerPos = MainGame.me.player.transform.position;

        foreach (var wgo in WorldMap.objs)
        {
            if (wgo == null || wgo.obj_def == null ||
                wgo.obj_def.interaction_type != ObjectDefinition.InteractionType.Builder)
            {
                continue;
            }

            var distance = Vector2.Distance(playerPos, wgo.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = wgo;
            }
        }

        return best;
    }

    private static WorldGameObject FindNearestMovableToPlayer()
    {
        WorldGameObject best = null;
        var bestDistance = float.MaxValue;
        var playerPos = MainGame.me.player.transform.position;

        foreach (var wgo in WorldMap.objs)
        {
            if (!MoveSession.IsMovableForTest(wgo))
            {
                continue;
            }

            var distance = Vector2.Distance(playerPos, wgo.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = wgo;
            }
        }

        return best;
    }

    private static Vector3 GetProbePoint(WorldGameObject wgo)
    {
        foreach (var collider in wgo.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider != null && collider.gameObject.layer == 0 && collider.enabled &&
                collider.bounds.size.sqrMagnitude > 0f)
            {
                return collider.bounds.center;
            }
        }

        return wgo.transform.position;
    }

    private static string Describe(WorldGameObject wgo)
    {
        return wgo == null ? "null" : wgo.obj_id;
    }

    private static int CountItems(Item item)
    {
        if (item == null)
        {
            return 0;
        }

        var count = 0;

        if (item.inventory != null)
        {
            foreach (var child in item.inventory)
            {
                count += 1 + CountItems(child);
            }
        }

        if (item.secondary_inventory != null)
        {
            foreach (var child in item.secondary_inventory)
            {
                count += 1 + CountItems(child);
            }
        }

        return count;
    }
}
