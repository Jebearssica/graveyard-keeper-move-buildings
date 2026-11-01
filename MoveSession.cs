using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoveBuildings;

/// <summary>
///     Picks up an existing <see cref="WorldGameObject" /> into the vanilla placement preview, then writes the new
///     position/variation back onto the *same* object. Keeping the original instance is what preserves stored
///     contents (chests, graves, zombie stations, craft queues, quality, ...) - it is never destroyed or re-created.
/// </summary>
internal static class MoveSession
{
    private const string PlaceSuffix = "_place";
    private const string GraveGroundId = "grave_ground";
    private const string GraveEmptyId = "grave_empty";
    private const float GridSize = 96f;
    private const float PickedUpAlpha = 0.35f;
    private const int BusyMask = 8389121; // layers 0/9/23, the mask BuildGrid.IsCellBusy uses
    private const int ZoneMask = 524288; // layer 19, WorldZone / WorldSubZone colliders

    private static readonly Dictionary<Collider2D, bool> ColliderStates = new();
    private static readonly List<SpriteColorState> SpriteStates = new();

    private static WorldGameObject _target;
    private static FloatingWorldGameObject _preview;
    private static WorldZone _zone;
    private static string _zoneId = string.Empty;
    private static Bounds? _originalBounds;
    private static int _previewVariationAtStart;

    // Set when the session is started from the build UI: build mode / grid / HUD stay alive between placements.
    private static bool _keepBuildMode;
    private static string _savedBuildZoneId = string.Empty;
    private static WorldZone _savedBuildZone;
    private static string _lastAvailabilityReport = string.Empty;

    internal static bool Active => _target != null;

    private struct SpriteColorState
    {
        public SpriteRenderer Renderer;
        public Color Color;
    }

    /// <summary>Called every frame from <see cref="MainGame.Update" />.</summary>
    internal static void Tick()
    {
        if (!Active)
        {
            return;
        }

        try
        {
            if (!CanContinue())
            {
                Cancel();
                return;
            }

            if (LazyInput.GetKeyDown(GameKey.Back) || LazyInput.GetKeyDown(GameKey.RightClick))
            {
                LazyInput.ClearKeyDown(GameKey.Back);
                Cancel();
                return;
            }

            MovePreviewToCursor();
            HandleRotation();

            if (IsConfirmPressed() && FloatingWorldGameObject.can_be_built)
            {
                Commit();
                ConsumeConfirmPress();
                return;
            }

            RefreshAvailability();
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Move session error, cancelling: " + e);
            Cancel();
        }
    }

    /// <summary>Starts a move while the build UI already put the game into build mode.</summary>
    internal static bool StartFromBuildMode(WorldGameObject target)
    {
        if (!MainGame.game_started || MainGame.me == null || MainGame.me.player == null)
        {
            return false;
        }

        if (MainGame.me.game_mode != MainGame.GameMode.Building || FloatingWorldGameObject.IsFloating())
        {
            return false;
        }

        if (!IsMovable(target))
        {
            return false;
        }

        Start(target, true);
        return Active;
    }

    internal static WorldGameObject TryFindTargetUnderCursor()
    {
        return FindTargetUnderCursor();
    }

    internal static bool IsMovableForTest(WorldGameObject wgo)
    {
        return IsMovable(wgo);
    }

    internal static bool HasPutCraftForTest(string objId)
    {
        return HasCraft(objId, ObjectCraftDefinition.BuildType.Put);
    }

    internal static bool HasRemoveCraftForTest(string objId)
    {
        return HasCraft(objId, ObjectCraftDefinition.BuildType.Remove);
    }

    internal static void CommitForTest()
    {
        if (Active)
        {
            Commit();
        }
    }

#if DEMO
    internal static void CancelForDemo()
    {
        if (Active)
        {
            Cancel();
        }
    }
#endif

    private static bool CanContinue()
    {
        if (!MainGame.game_started || MainGame.me == null || MainGame.me.player == null)
        {
            return false;
        }

        if (MainGame.me.game_mode != MainGame.GameMode.Building || _preview == null || _target == null)
        {
            return false;
        }

        if (_target.is_removed || _target.is_dead)
        {
            return false;
        }

        var buildGui = GUIElements.me != null ? GUIElements.me.build_mode_gui : null;
        return buildGui != null && buildGui.is_shown && !AnyOtherGuiOpened(buildGui);
    }

    private static bool AnyOtherGuiOpened(BaseGUI buildGui)
    {
        foreach (var gui in BaseGUI.opened_windows)
        {
            if (gui != null && gui != buildGui)
            {
                return true;
            }
        }

        return false;
    }

    private static WorldGameObject FindTargetUnderCursor()
    {
        var cam = Camera.main;
        if (cam == null || UICamera.hoveredObject != null)
        {
            return null;
        }

        return FindTargetAt(cam.ScreenToWorldPoint(Input.mousePosition));
    }

    internal static WorldGameObject FindTargetAtTest(Vector3 worldPos)
    {
        return FindTargetAt(worldPos);
    }

    private static WorldGameObject FindTargetAt(Vector3 worldPos)
    {
        var hits = Physics2D.OverlapPointAll(worldPos, 1);

        WorldGameObject best = null;
        var bestOrder = float.NegativeInfinity;

        foreach (var hit in hits)
        {
            if (hit == null)
            {
                continue;
            }

            var wgo = hit.GetComponentInParent<WorldGameObject>();
            if (!IsMovable(wgo))
            {
                continue;
            }

            var sprite = hit.GetComponent<SpriteRenderer>();
            var order = sprite != null ? sprite.sortingOrder : 0f;
            if (best == null || order > bestOrder)
            {
                best = wgo;
                bestOrder = order;
            }
        }

        return best;
    }

    private static bool IsMovable(WorldGameObject wgo)
    {
        if (wgo == null || wgo.obj_def == null || wgo.is_player || wgo.is_removed || wgo.is_dead)
        {
            return false;
        }

        if (wgo.obj_def.type == ObjectDefinition.ObjType.NPC || wgo.obj_def.type == ObjectDefinition.ObjType.Mob)
        {
            return false;
        }

        if (GameBalance.me == null || GameBalance.me.GetDataOrNull<ObjectDefinition>(wgo.obj_id) == null)
        {
            return false;
        }

        // Graves are built from the graveyard desk but have no Put craft of their own (the body lives in the object's
        // inventory), so they are matched by their interaction type instead.
        if (wgo.obj_def.interaction_type == ObjectDefinition.InteractionType.Grave ||
            wgo.obj_id == GraveGroundId || wgo.obj_id == GraveEmptyId)
        {
            return true;
        }

        if (HasCraft(wgo.obj_id, ObjectCraftDefinition.BuildType.Put))
        {
            return true;
        }

        return Plugin.AllowNonBuildable.Value && HasCraft(wgo.obj_id, ObjectCraftDefinition.BuildType.Remove);
    }

    private static bool HasCraft(string objId, ObjectCraftDefinition.BuildType buildType)
    {
        // Built objects and their construction sites differ by a trailing "_place"
        // (crafts output "chest_place" while the finished object is "chest"), so compare normalized ids.
        var normalizedId = NormalizeId(objId);
        var crafts = GameBalance.me.craft_obj_data;

        for (var i = 0; i < crafts.Count; i++)
        {
            var craft = crafts[i];
            if (craft != null && craft.build_type == buildType && NormalizeId(craft.out_obj) == normalizedId)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeId(string id)
    {
        return !string.IsNullOrEmpty(id) && id.EndsWith(PlaceSuffix, StringComparison.Ordinal)
            ? id.Substring(0, id.Length - PlaceSuffix.Length)
            : id;
    }

    private static void Start(WorldGameObject target, bool keepBuildMode)
    {
        _keepBuildMode = keepBuildMode;
        _target = target;
        _zoneId = ResolveZoneId(target);
        _zone = string.IsNullOrEmpty(_zoneId) ? null : WorldZone.GetZoneByID(_zoneId, false);
        _originalBounds = ComputeBounds(target);
        _lastAvailabilityReport = string.Empty;

        SaveAndDisableColliders(target);
        SaveAndDimSprites(target);

        var logics = MainGame.me.build_mode_logics;
        _savedBuildZoneId = logics._cur_build_zone_id;
        _savedBuildZone = logics._cur_build_zone;

        if (!keepBuildMode)
        {
            MainGame.me.EnterBuildMode();
        }

        logics._cur_build_zone_id = _zoneId;
        // BuildModeGUI.Open() dereferences cur_build_zone, so keep a valid zone around even for objects
        // that live outside of any zone (the placement check itself only uses cur_build_zone_id).
        logics._cur_build_zone = _zone ?? MainGame.me.player.GetMyWorldZone();
        logics._multi_inventory = null;

        if (Plugin.ShowBuildGrid.Value)
        {
            BuildGridView.ShowAt(target.transform.position);
        }

        if (!keepBuildMode)
        {
            GUIElements.me.build_mode_gui.Open();
        }

        _preview = FloatingWorldGameObject.CreateFloatingWorldObjectById(target.obj_id);
        if (_preview == null)
        {
            throw new InvalidOperationException("could not create placement preview for " + target.obj_id);
        }

        _preview.wobj.variation = target.variation;
        _preview.wobj.variation_2 = target.variation_2;
        _preview.wobj.RedrawVariation();
        _previewVariationAtStart = _preview.wobj.variation;
        _preview.RecalculateObjectMask();

        FloatingWorldGameObject.MoveCurrentFloatingObject(target.transform.position);
        RefreshAvailability();

        RedrawInteractionHint();

        Plugin.Log.LogInfo($"Picked up '{target.obj_id}' (unique_id={target.unique_id}, zone='{_zoneId}', zone_found={_zone != null})");
    }

    private static void RedrawInteractionHint()
    {
        var interaction = MainGame.me.player != null && MainGame.me.player.components != null
            ? MainGame.me.player.components.interaction
            : null;

        if (interaction != null)
        {
            interaction.RedrawCurrentInteractiveHint();
        }
    }

    private static void MovePreviewToCursor()
    {
        var cam = Camera.main;
        if (cam == null || _preview == null)
        {
            return;
        }

#if DEMO
        if (Demo.PreviewTarget.HasValue)
        {
            FloatingWorldGameObject.MoveCurrentFloatingObject(Demo.PreviewTarget.Value, false);
            return;
        }
#endif

        var gridPos = (Vector2)cam.ScreenToWorldPoint(Input.mousePosition) / GridSize;
        FloatingWorldGameObject.MoveCurrentFloatingObject(gridPos, false);
    }

    private static void HandleRotation()
    {
        if (LazyInput.GetKeyDown(GameKey.RotateLeft))
        {
            FloatingWorldGameObject.RotateCurrentFloatingObject(false);
            LazyInput.ClearKeyDown(GameKey.RotateLeft);
        }
        else if (LazyInput.GetKeyDown(GameKey.RotateRight))
        {
            FloatingWorldGameObject.RotateCurrentFloatingObject();
            LazyInput.ClearKeyDown(GameKey.RotateRight);
        }
    }

    internal static bool IsConfirmPressed()
    {
        if (UICamera.hoveredObject != null)
        {
            return false;
        }

        return LazyInput.gamepad_active
            ? LazyInput.GetKeyDown(GameKey.Interaction)
            : LazyInput.GetKeyDown(GameKey.LeftClick);
    }

    /// <summary>
    ///     Swallows the click that placed a building, so the pick step that resumes right after it in the same frame does
    ///     not grab the building that was just put down.
    /// </summary>
    internal static void ConsumeConfirmPress()
    {
        if (LazyInput.gamepad_active)
        {
            LazyInput.ClearKeyDown(GameKey.Interaction);
        }
        else
        {
            LazyInput.ClearKeyDown(GameKey.LeftClick);
        }
    }

    /// <summary>
    ///     Same rules vanilla uses in <see cref="FloatingWorldGameObject.RecalculateAvailability" />, with two
    ///     deliberate differences: the object that is being moved never blocks its own destination, and the zone
    ///     containment test is a point test so half of a cell sticking out of the buildable area is rejected.
    /// </summary>
    private static bool ComputeAvailability()
    {
        if (_preview == null)
        {
            return false;
        }

        var cells = FloatingWorldGameObject._cells;
        var subZone = BuildGrid.me != null ? BuildGrid.GetCurrentSubZoneID() : string.Empty;

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (cell == null || cell.gameObject == null || !cell.gameObject.activeSelf)
            {
                continue;
            }

            if (cell.cell_type == FlowGridCell.CellType.TotemArea)
            {
                continue;
            }

            var position = cell.transform.position;

            // Cells the object already occupied before the move are always allowed: moving a building may overlap its
            // own former footprint (including whatever it already overlapped), otherwise some buildings could never be
            // nudged by a single cell.
            if (IsInsideOriginalFootprint(position))
            {
                continue;
            }

            if (IsCellBusy(position, out var blocker))
            {
                ReportAvailability($"cell {Describe(position)} is blocked by '{blocker}'");
                return false;
            }

            if (!IsInsideBuildZone(position, subZone))
            {
                ReportAvailability(
                    $"cell {Describe(position)} is outside the buildable area (zone '{_zoneId}', sub zone '{subZone}')");
                return false;
            }
        }

        ClearAvailabilityReport();
        return true;
    }

    private static bool IsInsideOriginalFootprint(Vector3 position)
    {
        if (_originalBounds == null)
        {
            return false;
        }

        var bounds = _originalBounds.Value;
        return position.x >= bounds.min.x && position.x <= bounds.max.x &&
               position.y >= bounds.min.y && position.y <= bounds.max.y;
    }

    /// <summary>
    ///     Overlap test of <see cref="BuildGrid.IsCellBusy" />, except that the object being moved and everything it
    ///     already overlapped are ignored, so its own footprint may overlap the destination.
    /// </summary>
    private static bool IsCellBusy(Vector3 position, out string blocker)
    {
        blocker = null;

        var hits = Physics2D.OverlapBoxAll(position, BuildGrid.GRID_CHECK_BOX_SIZE, 0f, BusyMask);
        foreach (var hit in hits)
        {
            if (hit == null || BuildGrid.SkipCollider(hit))
            {
                continue;
            }

            // The placement preview itself never blocks its own cells.
            if (hit.GetComponentInParent<FloatingWorldGameObject>() != null)
            {
                continue;
            }

            var owner = hit.GetComponentInParent<WorldGameObject>();
            if (owner == _target)
            {
                continue;
            }

            // Whatever the building already overlapped before the move keeps overlapping it.
            if (IsInsideOriginalFootprint(hit.bounds.center))
            {
                continue;
            }

            blocker = owner != null
                ? $"{owner.obj_id} at {Describe(owner.transform.position)}"
                : $"{hit.gameObject.name} at {Describe(hit.bounds.center)}";
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Zone containment using half of the box the build grid samples with: a cell the player sees as buildable is
    ///     usable, while a cell that only grazes the zone edge is still rejected so nothing hangs far outside the
    ///     buildable area.
    /// </summary>
    private static bool IsInsideBuildZone(Vector3 position, string subZone)
    {
        if (string.IsNullOrEmpty(_zoneId))
        {
            return true;
        }

        var hits = Physics2D.OverlapBoxAll(position, BuildGrid.GRID_CHECK_BOX_SIZE * 0.5f, 0f, ZoneMask);
        foreach (var hit in hits)
        {
            if (hit == null)
            {
                continue;
            }

            var zone = hit.GetComponent<WorldZone>();
            if (zone != null && zone.id == _zoneId)
            {
                return true;
            }

            if (string.IsNullOrEmpty(subZone))
            {
                continue;
            }

            var sub = hit.GetComponent<WorldSubZone>();
            if (sub != null && sub.sub_zone_id == subZone)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Zone of an object: its own zone, or - when the pivot sits outside of every zone - the zone found under its
    ///     footprint. Objects that really are outside of all zones keep an empty id and may move freely.
    /// </summary>
    private static string ResolveZoneId(WorldGameObject wgo)
    {
        var direct = wgo.GetMyWorldZoneId();
        if (!string.IsNullOrEmpty(direct))
        {
            return direct;
        }

        var footprint = ComputeFootprint(wgo);
        var center = footprint.center;
        var extents = footprint.extents;
        var samples = new[]
        {
            center,
            center + new Vector3(-extents.x, -extents.y),
            center + new Vector3(extents.x, extents.y),
            center + new Vector3(-extents.x, extents.y),
            center + new Vector3(extents.x, -extents.y)
        };

        foreach (var sample in samples)
        {
            var hits = Physics2D.OverlapPointAll(sample, ZoneMask);
            foreach (var hit in hits)
            {
                if (hit == null)
                {
                    continue;
                }

                var zone = hit.GetComponent<WorldZone>();
                if (zone == null || string.IsNullOrEmpty(zone.id))
                {
                    continue;
                }

                Plugin.Log.LogInfo($"Zone of '{wgo.obj_id}' resolved from its footprint: '{zone.id}'");
                return zone.id;
            }
        }

        return string.Empty;
    }

    private static Bounds ComputeFootprint(WorldGameObject wgo)
    {
        var bounds = new Bounds(wgo.transform.position, Vector3.one * (GridSize * 0.5f));
        var hasBounds = false;

        foreach (var collider in wgo.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return bounds;
    }

    private static void ReportAvailability(string reason)
    {
        if (_lastAvailabilityReport == reason)
        {
            return;
        }

        _lastAvailabilityReport = reason;
        Plugin.Log.LogInfo("[MOVE] Cannot place here: " + reason);
    }

    private static void ClearAvailabilityReport()
    {
        if (_lastAvailabilityReport.Length == 0)
        {
            return;
        }

        _lastAvailabilityReport = string.Empty;
        Plugin.Log.LogInfo("[MOVE] Placement available again.");
    }

    private static void RefreshAvailability()
    {
        if (_preview == null)
        {
            return;
        }

        var canPlace = ComputeAvailability();
        FloatingWorldGameObject.can_be_built = canPlace;
        _preview.UpdateGridColor();

        var buildGui = GUIElements.me != null ? GUIElements.me.build_mode_gui : null;
        if (buildGui != null)
        {
            buildGui.RedrawPlacing(canPlace, FloatingWorldGameObject.IsObjectRotatable());
        }
    }

    private static void Commit()
    {
        var target = _target;
        var preview = _preview;
        if (target == null || preview == null || !FloatingWorldGameObject.can_be_built)
        {
            return;
        }

        var newPos = preview.transform.position;
        var newVariation = preview.wobj.variation;
        var variationChanged = newVariation != _previewVariationAtStart;
        var oldZone = target.GetMyWorldZone();
        var oldBounds = _originalBounds;

        // The vanilla MoveWhenPlacingGlobalPos() rounds the position through RoundAndSortComponent, and a finished
        // building snaps to a full 96px cell while the placement preview only snaps to a third of it. That made the
        // object land up to half a cell away from the preview, so the final position is written as-is instead and only
        // z/sorting are refreshed.
        var targetPosition = target.tf.position;
        target.tf.position = new Vector3(newPos.x, newPos.y, targetPosition.z);
        target.round_and_sort.SetZ();
        if (variationChanged)
        {
            target.variation = newVariation;
            target.RedrawVariation();
        }

        // Colliders must be live again before any bounds based refresh below.
        RestoreColliders();

        target.RefreshPositionCache();
        target.ForceInitOptimizedColliders();
        target.RecalculateGridShape();

        // The old cells have to be released from the nav mesh too, not just the new ones blocked.
        if (oldBounds.HasValue)
        {
            ChunkManager.RecalcAStarBounds(oldBounds.Value);
        }

        target.UpdatePathCell();

        var chunked = target.GetComponent<ChunkedGameObject>();
        if (chunked != null)
        {
            chunked.RecalculateChunk();
        }

        target.RecalculateZoneBelonging();

        var newZone = target.GetMyWorldZone();
        if (newZone != null)
        {
            newZone.Recalculate();
        }

        if (oldZone != null && oldZone != newZone)
        {
            oldZone.Recalculate();
        }

        target.round_and_sort.SetZ();
        foreach (var sort in target.GetComponentsInChildren<RoundAndSortComponent>(true))
        {
            if (sort != null)
            {
                sort.DoUpdateStuff(true);
            }
        }

        target.Redraw();
        target.RedrawBubble();
        RedrawInteractionHint();

        Plugin.Log.LogInfo(
            $"Moved '{target.obj_id}' from {Describe(oldBounds)} to {Describe(newPos)}, variation changed={variationChanged}");

        End(true);
    }

    private static void Cancel()
    {
        End(false);
    }

    private static void End(bool committed)
    {
        try
        {
            var target = _target;

            if (_preview != null)
            {
                try
                {
                    FloatingWorldGameObject.StopCurrentFloating(false);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Failed to stop floating preview: " + e.Message);
                }

                _preview = null;
            }

            if (target != null)
            {
                RestoreColliders();
                RestoreSprites();
            }

            if (_keepBuildMode && MainGame.me != null)
            {
                var logics = MainGame.me.build_mode_logics;
                if (logics != null)
                {
                    logics._cur_build_zone_id = _savedBuildZoneId;
                    logics._cur_build_zone = _savedBuildZone;
                }

                if (_zone != null)
                {
                    _zone.Recalculate();
                }

                if (BuildGrid.me != null && _savedBuildZone != null)
                {
                    // Move first, then refresh: RefreshGrid() samples the world around the grid's current position, so
                    // moving the grid without refreshing would leave the coloured cells offset from the terrain.
                    BuildGridView.ShowAt(_savedBuildZone.center_tf.position);
                }
            }
            else
            {
                ResetBuildMode();
            }

            Plugin.Log.LogInfo(committed ? "Move finished." : "Move cancelled.");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("Failed to finish move session: " + e);
        }
        finally
        {
            _target = null;
            _preview = null;
            _zone = null;
            _zoneId = string.Empty;
            _originalBounds = null;
            _previewVariationAtStart = 0;
            _keepBuildMode = false;
            _savedBuildZoneId = string.Empty;
            _savedBuildZone = null;
            ColliderStates.Clear();
            SpriteStates.Clear();
        }
    }

    private static void ResetBuildMode()
    {
        var me = MainGame.me;
        if (me == null)
        {
            return;
        }

        var logics = me.build_mode_logics;
        if (logics != null)
        {
            logics._cur_build_zone_id = string.Empty;
            logics._cur_build_zone = null;
            logics._multi_inventory = null;
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

        if (_zone != null)
        {
            _zone.Recalculate();
        }
    }

    private static void SaveAndDisableColliders(WorldGameObject wgo)
    {
        ColliderStates.Clear();
        foreach (var collider in wgo.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider == null)
            {
                continue;
            }

            ColliderStates[collider] = collider.enabled;
            collider.enabled = false;
        }
    }

    private static void RestoreColliders()
    {
        foreach (var pair in ColliderStates)
        {
            if (pair.Key != null)
            {
                pair.Key.enabled = pair.Value;
            }
        }

        ColliderStates.Clear();
    }

    private static void SaveAndDimSprites(WorldGameObject wgo)
    {
        SpriteStates.Clear();
        foreach (var sprite in wgo.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sprite == null || sprite.GetComponent<DynamicSprite>() != null)
            {
                continue;
            }

            if (sprite.color != Color.white && sprite.color != Color.red)
            {
                continue;
            }

            SpriteStates.Add(new SpriteColorState { Renderer = sprite, Color = sprite.color });
            sprite.color = new Color(sprite.color.r, sprite.color.g, sprite.color.b, PickedUpAlpha);
        }
    }

    private static void RestoreSprites()
    {
        foreach (var state in SpriteStates)
        {
            if (state.Renderer != null)
            {
                state.Renderer.color = state.Color;
            }
        }

        SpriteStates.Clear();
    }

    private static Bounds? ComputeBounds(WorldGameObject wgo)
    {
        var hasBounds = false;
        var bounds = default(Bounds);

        foreach (var collider in wgo.GetComponentsInChildren<Collider2D>(true))
        {
            if (collider == null || !collider.enabled || collider.gameObject.layer != 0)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (!hasBounds)
        {
            return null;
        }

        bounds.Expand(Vector2.one / 6f);
        return bounds;
    }

    private static string Describe(Vector3 pos)
    {
        return $"({pos.x:0.#}, {pos.y:0.#})";
    }

    private static string Describe(Bounds? bounds)
    {
        return bounds.HasValue ? Describe(bounds.Value.center) : "(unknown)";
    }
}

