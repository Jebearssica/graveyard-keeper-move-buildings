using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MoveBuildings;

/// <summary>
///     Loads the bundled "move building" icon: a build-style blue tile with a four-direction arrow, drawn in the same
///     palette as the vanilla build/remove rows. Falls back to a vanilla icon when the resource cannot be read.
/// </summary>
internal static class MoveIcon
{
    private const string ResourceName = "MoveBuildings.icon.move_building.png";

    private static Sprite _sprite;
    private static bool _loaded;

    internal static Sprite Get()
    {
        if (_loaded)
        {
            return _sprite;
        }

        _loaded = true;

        try
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    Plugin.Log.LogWarning("Move icon resource '" + ResourceName + "' is missing, using a vanilla icon.");
                    return null;
                }

                var bytes = new byte[stream.Length];
                var read = 0;
                while (read < bytes.Length)
                {
                    var chunk = stream.Read(bytes, read, bytes.Length - read);
                    if (chunk <= 0)
                    {
                        break;
                    }

                    read += chunk;
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };

                if (!texture.LoadImage(bytes))
                {
                    Plugin.Log.LogWarning("Move icon resource could not be decoded, using a vanilla icon.");
                    return null;
                }

                _sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    texture.width);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Could not create the move icon: " + e.Message);
        }

        return _sprite;
    }
}
