#if DEMO
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoveBuildings;

/// <summary>
///     Recording helper, compiled only with <c>-p:Demo=true</c>. Loads the newest save and replays a scripted move
///     sequence so the README GIF can be captured without touching the mouse. Never part of a release build.
/// </summary>
internal static class Demo
{
    private const float GridSize = 96f;

    // Candidate destinations in preview steps (32px); the preview probes them with the real placement rules.
    private static readonly Vector2[] Steps =
    {
        new(3f, 0f), new(-3f, 0f), new(0f, 3f), new(0f, -3f), new(3f, 3f), new(-3f, -3f),
        new(3f, -3f), new(-3f, 3f), new(2f, 0f), new(-2f, 0f), new(0f, 2f), new(0f, -2f),
        new(2f, 2f), new(-2f, -2f), new(1f, 0f), new(-1f, 0f), new(0f, 1f), new(0f, -1f)
    };

    private const float StepSize = GridSize / 3f;

    private static int _phase;
    private static float _phaseTime;
    private static float _phaseDelay;
    private static float _waitLogged;
    private static bool _loadRequested;
    private static bool _menuWaitStarted;
    private static float _menuWaitTime;
    private static WorldGameObject _first;
    private static WorldGameObject _second;
    private static Vector2 _from;
    private static Vector2 _to;
    private static int _probeIndex;
    private static int _probeFrames;

    /// <summary>Grid-space position the preview is forced to, or null to follow the mouse.</summary>
    internal static Vector2? PreviewTarget { get; private set; }

    internal static void Tick()
    {
        try
        {
            _phaseTime += Time.unscaledDeltaTime;

            switch (_phase)
            {
                case 0:
                    if (!_loadRequested && CanStartLoad())
                    {
                        _loadRequested = true;
                        LoadNewestSave();
                    }

                    if (MainGame.game_started && MainGame.me != null && MainGame.me.player != null)
                    {
                        Next(1, 6f);
                    }

                    return;

                case 1:
                    if (_phaseTime < _phaseDelay || !BaseGUI.all_guis_closed ||
                        MainGame.me.game_mode != MainGame.GameMode.Normal)
                    {
                        return;
                    }

                    PickTargets();
                    return;

                case 2:
                    if (_phaseTime < _phaseDelay)
                    {
                        return;
                    }

                    BuildMoveMode.Enter();
                    Next(3, 1.5f);
                    return;

                case 3:
                    if (_phaseTime < _phaseDelay)
                    {
                        return;
                    }

                    BeginMove(_first, 4);
                    return;

                case 4:
                    if (Probe())
                    {
                        Next(5, 2.4f);
                        Plugin.Log.LogInfo("[DEMO_START]");
                    }

                    return;

                case 5:
                    if (!Animate())
                    {
                        return;
                    }

                    Next(6, 0.5f);
                    return;

                case 6:
                    if (_phaseTime < _phaseDelay)
                    {
                        return;
                    }

                    Place();
                    Next(7, 1.2f);
                    return;

                case 7:
                    if (_phaseTime < _phaseDelay)
                    {
                        return;
                    }

                    if (_second == null)
                    {
                        ReopenDesk();
                        return;
                    }

                    BeginMove(_second, 8);
                    return;

                case 8:
                    if (Probe())
                    {
                        Next(9, 2.4f);
                    }

                    return;

                case 9:
                    if (!Animate())
                    {
                        return;
                    }

                    Next(10, 0.5f);
                    return;

                case 10:
                    if (_phaseTime < _phaseDelay)
                    {
                        return;
                    }

                    Place();
                    Next(11, 1.2f);
                    return;

                case 11:
                    if (_phaseTime < _phaseDelay)
                    {
                        return;
                    }

                    ReopenDesk();
                    return;

                case 12:
                    if (_phaseTime < _phaseDelay)
                    {
                        return;
                    }

                    Finish();
                    return;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError("[DEMO] " + e);
            _phase = 99;
        }
    }

    private static void PickTargets()
    {
        var origin = MainGame.me.player.transform.position;
        var desk = WorldMap.objs
            .Where(o => o != null && o.obj_def != null &&
                        o.obj_def.interaction_type == ObjectDefinition.InteractionType.Builder)
            .OrderBy(o => Vector3.Distance(o.transform.position, origin))
            .FirstOrDefault();

        if (desk == null)
        {
            Plugin.Log.LogWarning("[DEMO] no build desk found");
            _phase = 99;
            return;
        }

        var movable = WorldMap.objs
            .Where(o => o != null && MoveSession.IsMovableForTest(o))
            .Where(o => Vector3.Distance(o.transform.position, origin) < 3500f)
            .ToList();

        _first = Priority(movable, "furnace") ?? Priority(movable, "anvil") ?? Priority(movable, "chest");
        _second = movable.FirstOrDefault(o => o != _first &&
                                              (o.obj_id.Contains("chest") || o.obj_id.Contains("anvil")));

        if (_first == null)
        {
            Plugin.Log.LogWarning("[DEMO] no movable object near the player");
            _phase = 99;
            return;
        }

        Plugin.Log.LogInfo("[DEMO] first='" + _first.obj_id + "' second='" +
                           (_second != null ? _second.obj_id : "<none>") + "' desk='" + desk.obj_id + "'");

        MainGame.me.OpenBuildObjectGUI(desk);
        Next(2, 2.4f);
    }

    private static WorldGameObject Priority(List<WorldGameObject> all, string keyword)
    {
        return all
            .Where(o => o.obj_id.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(o => o.transform.position.x)
            .FirstOrDefault();
    }

    private static void BeginMove(WorldGameObject wgo, int probePhase)
    {
        if (!MoveSession.StartFromBuildMode(wgo))
        {
            Plugin.Log.LogWarning("[DEMO] could not pick '" + wgo.obj_id + "'");
            _second = null;
            ReopenDesk();
            return;
        }

        _from = wgo.transform.position;
        _to = _from;
        _probeIndex = 0;
        _probeFrames = 0;
        CameraTools.MoveToPos(_from);
        Next(probePhase, 0f);
    }

    /// <summary>Steps the preview through the candidates until the real placement rules accept one.</summary>
    private static bool Probe()
    {
        if (_probeIndex >= Steps.Length)
        {
            Plugin.Log.LogWarning("[DEMO] no placeable destination found, cancelling");
            MoveSession.CancelForDemo();
            _second = null;
            Next(11, 0.5f);
            return false;
        }

        var candidate = _from + Steps[_probeIndex] * StepSize;

        if (_probeFrames == 0)
        {
            PreviewTarget = candidate / GridSize;
            _probeFrames = 1;
            return false;
        }

        _probeFrames++;
        if (_probeFrames < 3)
        {
            return false;
        }

        if (FloatingWorldGameObject.can_be_built)
        {
            _to = candidate;
            Plugin.Log.LogInfo("[DEMO] destination " + _from + " -> " + _to);
            return true;
        }

        _probeIndex++;
        _probeFrames = 0;
        return false;
    }

    private static bool Animate()
    {
        var t = Mathf.Clamp01(_phaseTime / _phaseDelay);
        var eased = Mathf.SmoothStep(0f, 1f, t);
        PreviewTarget = Vector2.Lerp(_from, _to, eased) / GridSize;
        CameraTools.MoveToPos((_from + _to) / 2f);
        return t >= 1f;
    }

    private static void Place()
    {
        if (!FloatingWorldGameObject.can_be_built)
        {
            Plugin.Log.LogWarning("[DEMO] destination became invalid, cancelling");
            MoveSession.CancelForDemo();
            return;
        }

        MoveSession.CommitForTest();
    }

    private static void ReopenDesk()
    {
        PreviewTarget = null;
        BuildMoveMode.Stop(true);
        Next(12, 2.4f);
    }

    private static void Finish()
    {
        PreviewTarget = null;
        _phase = 99;
        Plugin.Log.LogInfo("[DEMO_END]");
    }

    private static void Next(int phase, float delay)
    {
        _phase = phase;
        _phaseTime = 0f;
        _phaseDelay = delay;
    }

    /// <summary>True once the main menu is up and the world scene exists, so a scripted load cannot hit a null world.</summary>
    private static bool CanStartLoad()
    {
        if (MainGame.game_started || MainGame.me == null || GUIElements.me == null)
        {
            return false;
        }

        if (!_menuWaitStarted)
        {
            _menuWaitStarted = true;
            _menuWaitTime = Time.unscaledTime;
        }

        if (Time.unscaledTime - _menuWaitTime < 12f)
        {
            return false;
        }

        var menu = GUIElements.me.main_menu;
        return menu != null && menu.is_shown && MainGame.me.world != null;
    }

    /// <summary>Replicates the main menu "continue" flow, which needs no click.</summary>
    private static void LoadNewestSave()
    {
        Intro.need_show_first_intro = false;
        PlatformSpecific.ReadSaveSlots(slots =>
        {
            if (slots == null || slots.Count == 0)
            {
                Plugin.Log.LogWarning("[DEMO] no save slots found");
                return;
            }

            var newest = slots
                .OrderByDescending(s =>
                {
                    var file = PlatformSpecific.GetSaveFolder() + s.filename_no_extension + ".dat";
                    return System.IO.File.Exists(file)
                        ? System.IO.File.GetLastWriteTime(file)
                        : DateTime.MinValue;
                })
                .First();

            var menu = GUIElements.me.saves ?? SlotMenuFallback();
            if (menu == null)
            {
                Plugin.Log.LogWarning("[DEMO] save slots menu not available");
                return;
            }

            Plugin.Log.LogInfo("[DEMO] loading save '" + newest.filename_no_extension + "'");

            TryStep("loading gui", delegate
            {
                LoadingGUI.HideImmediate();
                LoadingGUI.Show();
                LoadingGUI.ShowBlackBackground(true);
            });
            TryStep("hide slots", delegate { menu.Hide(false); });
            TryStep("hide main menu", delegate { GUIElements.me.main_menu.Hide(false); });
            TryStep("hide hud", delegate { GUIElements.me.hud.Hide(); });
            TryStep("progress bar", delegate { LoadingGUI.ShowProgressBar(); });

            PlatformSpecific.LoadGame(newest, save =>
            {
                if (save == null)
                {
                    Plugin.Log.LogWarning("[DEMO] load returned null");
                    return;
                }

                MainGame.me.save = save;
                GJTimer.AddTimer(0f, delegate
                {
                    TryStep("progress", delegate { LoadingGUI.IncreaseProgressBar(); });
                    MainGame.me.save.map.RestoreScene();
                    GJTimer.AddTimer(0f, delegate
                    {
                        TryStep("progress", delegate { LoadingGUI.IncreaseProgressBar(); });
                        menu.StartPlayingGame();
                    });
                });
            });
        });
    }

    private static SaveSlotsMenuGUI SlotMenuFallback()
    {
        var field = typeof(MainMenuGUI).GetField("_menu",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(GUIElements.me.main_menu) as SaveSlotsMenuGUI;
    }

    private static void TryStep(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[DEMO] " + name + " failed: " + e.Message);
        }
    }
}
#endif

