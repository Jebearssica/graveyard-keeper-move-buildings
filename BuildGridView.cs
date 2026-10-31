using System;
using System.Reflection;
using UnityEngine;

namespace MoveBuildings;

/// <summary>
///     Wraps the vanilla build grid for the move flow.
///     Some building overhauls (for example IBuildWhereIWant with its "Grid" option enabled) prefix
///     <see cref="BuildGrid.ShowBuildGrid" /> and force <c>show</c> to false, which would leave the player without any
///     placement feedback while moving. In that case the grid is toggled through its own private
///     <c>BuildGrid.Show(bool)</c> - the method <see cref="BuildGrid.ShowBuildGrid" /> uses itself, which recomputes the
///     cell colours and activates the GameObject.
/// </summary>
internal static class BuildGridView
{
    private static MethodInfo _showMethod;
    private static bool _reportedSuppression;

    internal static void ShowAt(Vector2 worldPosition)
    {
        var grid = BuildGrid.me;
        if (grid == null)
        {
            return;
        }

        // RefreshGrid() samples the world around the grid's current position, so the grid has to be moved first.
        grid.MoveBuildGridTo(worldPosition);
        BuildGrid.ShowBuildGrid(true, true);

        if (grid.gameObject.activeSelf)
        {
            return;
        }

        SetGridActive(grid, true);

        if (!_reportedSuppression)
        {
            _reportedSuppression = true;
            Plugin.Log.LogInfo("Another mod suppresses BuildGrid.ShowBuildGrid, showing the build grid directly.");
        }
    }

    internal static void Hide()
    {
        BuildGrid.ShowBuildGrid(false, true);

        var grid = BuildGrid.me;
        if (grid != null && grid.gameObject.activeSelf)
        {
            SetGridActive(grid, false);
        }
    }

    private static void SetGridActive(BuildGrid grid, bool show)
    {
        if (_showMethod == null)
        {
            _showMethod = typeof(BuildGrid).GetMethod("Show", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        try
        {
            if (_showMethod != null)
            {
                _showMethod.Invoke(grid, new object[] { show });
                return;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Could not toggle the build grid directly: " + e.Message);
        }

        grid.gameObject.SetActive(show);
    }
}
