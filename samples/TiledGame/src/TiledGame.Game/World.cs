using System.Numerics;

namespace TiledGame.Game;

/// <summary>The game's world declarations. One world unit is one world-pixel, Y-down.</summary>
public static class World
{
    /// <summary>World units every camera in this game spans.</summary>
    public static readonly Vector2 ViewportSize = new(320f, 180f);
}
