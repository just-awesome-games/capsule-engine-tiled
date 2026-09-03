using System.Numerics;
using Capsule.Assets.Generated;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Spawning;

namespace TiledGame.Game.Entities;

/// <summary>
/// An entity that collides without blocking. It sits on the <c>sensor</c> collision layer, which
/// <see cref="Player"/> detects but does not block on, so the player walks straight through it and
/// its contact is only reported. It listens to nothing itself: it never turns
/// <see cref="Collider2D.ReportsContacts"/> on, so it costs a shape in the world and no work.
/// </summary>
public sealed class Sensor : Entity
{
    private static readonly Vector2 Body = new(16f, 24f);

    /// <summary>The whole of <c>textures/sensor.png</c>, anchored at its top-left corner; it never flips, so the pivot stays there.</summary>
    private static readonly Sprite Field = new(GameAssets.Textures.Sensor, new TextureRegion(0, 0, 16, 24));

    public Sensor(EntitySpawn spawn)
        : base(spawn.Position)
    {
        Add(new SpriteRenderer(Field));
        Add(new BoxCollider2D(Body) { Layer = "sensor" });
    }
}
