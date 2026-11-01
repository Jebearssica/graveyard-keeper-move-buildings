using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MoveBuildings;

/// <summary>
///     Reflection access to the game members this mod needs that are not public in the shipped game assembly.
///     Both build paths loosen the references at compile time (the declaration stubs in ci/refs expose the used
///     members as public, and a local build publicizes the real assemblies), so reading such a member directly
///     compiles either way. At runtime the plugin runs against the unmodified game assembly, where the member keeps
///     its original visibility: a lenient runtime lets the access through, one that enforces visibility throws
///     FieldAccessException / MethodAccessException. Everything non-public therefore goes through here.
///     A missing member is reported once and then ignored, so an unfamiliar game version degrades instead of crashing.
/// </summary>
internal static class GameAccess
{
    private static readonly FieldInfo CrafteryField = Field(typeof(CraftGUI), "craftery_wgo");
    private static readonly FieldInfo BuildZoneIdField = Field(typeof(BuildModeLogics), "_cur_build_zone_id");
    private static readonly FieldInfo BuildZoneField = Field(typeof(BuildModeLogics), "_cur_build_zone");
    private static readonly FieldInfo BuildZoneInventoryField = Field(typeof(BuildModeLogics), "_multi_inventory");
    private static readonly FieldInfo FloatingCellsField = Field(typeof(FloatingWorldGameObject), "_cells");
    private static readonly FieldInfo LanguageField = Field(typeof(GameSettings), "_cur_lng");
    private static readonly MethodInfo RedrawVariationMethod = Method(typeof(WorldGameObject), "RedrawVariation");

    /// <summary>The desk window owned by <paramref name="craftGui" />; <c>BaseCraftGUI.craftery_wgo</c> is protected.</summary>
    internal static WorldGameObject GetCraftery(CraftGUI craftGui)
    {
        return craftGui == null ? null : Get(CrafteryField, craftGui) as WorldGameObject;
    }

    /// <summary>
    ///     Writes the three private zone fields at once. Reading them back is done through the public
    ///     <see cref="BuildModeLogics" /> properties.
    /// </summary>
    internal static void SetBuildZoneState(BuildModeLogics logics, string zoneId, WorldZone zone,
        MultiInventory inventory)
    {
        if (logics == null)
        {
            return;
        }

        Set(BuildZoneIdField, logics, zoneId);
        Set(BuildZoneField, logics, zone);
        Set(BuildZoneInventoryField, logics, inventory);
    }

    /// <summary>The preview's grid cells; <c>FloatingWorldGameObject._cells</c> is private static.</summary>
    internal static List<FlowGridCell> GetFloatingCells()
    {
        return Get(FloatingCellsField, null) as List<FlowGridCell>;
    }

    /// <summary>The configured language; <c>GameSettings._cur_lng</c> is private static.</summary>
    internal static string GetCurrentLanguage()
    {
        return Get(LanguageField, null) as string ?? string.Empty;
    }

    /// <summary><c>WorldGameObject.RedrawVariation()</c> is private.</summary>
    internal static void RedrawVariation(WorldGameObject wgo)
    {
        if (wgo == null || RedrawVariationMethod == null)
        {
            return;
        }

        try
        {
            RedrawVariationMethod.Invoke(wgo, null);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("RedrawVariation failed: " + e.Message);
        }
    }

    private static object Get(FieldInfo field, object instance)
    {
        if (field == null)
        {
            return null;
        }

        try
        {
            return field.GetValue(instance);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not read {field.DeclaringType?.Name}.{field.Name}: {e.Message}");
            return null;
        }
    }

    private static void Set(FieldInfo field, object instance, object value)
    {
        if (field == null)
        {
            return;
        }

        try
        {
            field.SetValue(instance, value);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Could not write {field.DeclaringType?.Name}.{field.Name}: {e.Message}");
        }
    }

    private static FieldInfo Field(Type type, string name)
    {
        var field = AccessTools.Field(type, name);
        Report(field, type, name);
        return field;
    }

    private static MethodInfo Method(Type type, string name)
    {
        var method = AccessTools.Method(type, name, Type.EmptyTypes);
        Report(method, type, name);
        return method;
    }

    private static void Report(MemberInfo member, Type type, string name)
    {
        if (member != null || Plugin.Log == null)
        {
            return;
        }

        Plugin.Log.LogError($"Game member {type.Name}.{name} was not found; the related feature is disabled.");
    }
}
