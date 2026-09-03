using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Spawning;

namespace TiledGame.Game.Entities;

/// <summary>
/// The walking, falling, jumping body spawned by the <c>player</c> entries of
/// <c>scenes/room.tmj</c> and <c>scenes/hall.scene.json</c>.
/// <para>
/// A concrete entity with one public constructor taking an <see cref="EntitySpawn"/> claims its
/// kebab-cased class name as the document entry type it spawns from, so <c>Player</c> answers to
/// <c>"player"</c> with nothing registered by hand; <c>[SpawnType("...")]</c> renames the claim
/// without renaming the class.
/// </para>
/// <para>
/// <see cref="Entity.Position"/> is the top-left corner of the 8x8 body: the box collider is
/// corner-anchored, and the sprite anchors its frame's bottom-centre at that same pivot offset
/// from the corner, so the frame covers the body facing either way and an authored entry's
/// coordinate is taken as that corner here. Anchoring an authored coordinate is each entity's own
/// convention.
/// </para>
/// <para>
/// The frame is drawn by a <see cref="SpriteRenderer"/> whose <see cref="SpriteRenderer.FlipX"/>
/// the walk direction sets, so one texture faces both ways. Flip and
/// <see cref="SpriteRenderer.Scale"/> both work about the frame's pivot, and the pivot is the
/// point <see cref="SpriteRenderer.Offset"/> puts on the body: at the feet, so a mirror is about
/// the body's horizontal centre and a squash-and-stretch keeps the feet planted — the squash
/// spreads into the floor instead of sinking through it, and the stretch grows upward.
/// </para>
/// <para>
/// That squash-and-stretch is presentation and nothing more. Jumping and landing each throw the
/// sprite's scale off <see cref="Vector2.One"/> and it eases back; the collider keeps its 8x8 box
/// throughout, so a stretched player is no taller to the physics than a resting one.
/// </para>
/// <para>
/// The two collision filters are independent. <see cref="KinematicBody2D.BlocksOn"/> names what
/// stops the sweep — <c>solid</c> and <c>platform</c>, the layers the room's tiles are authored on
/// — while <see cref="Collider2D.Detects"/> names what the collider reports, which is <c>sensor</c>
/// alone: the player walks through a <see cref="Sensor"/> and says so. Contacts, jumps and landings
/// are logged through <see cref="Log"/>, which the shell drains to the console at boot, each line
/// prefixed with the tick it happened on.
/// </para>
/// </summary>
public sealed class Player : Entity
{
    /// <summary>World units per second.</summary>
    private const float WalkSpeed = 80f;

    /// <summary>World units per second squared, downwards in a Y-down world.</summary>
    private const float Gravity = 600f;

    /// <summary>World units per second at take-off; an apex of about 40px, clearing a two-tile ledge.</summary>
    private const float JumpSpeed = 220f;

    /// <summary>The body's edge in world units, and the frame's in texels: one texel per unit.</summary>
    private const int BodyPixels = 8;

    /// <summary>
    /// The scale the sprite snaps to on take-off: tall and thin, the classic platformer stretch.
    /// Tune the pair together — the product is what reads as volume, and nothing enforces it.
    /// </summary>
    private static readonly Vector2 JumpStretch = new(0.6f, 1.4f);

    /// <summary>The scale the sprite snaps to on landing: wide and flat, the stretch inverted.</summary>
    private static readonly Vector2 LandSquash = new(1.4f, 0.6f);

    /// <summary>
    /// Scale units per second each axis walks back towards 1 after an impulse. At 1.6 the 0.4 of
    /// either impulse is spent in a quarter second; raise it for a snappier recovery, lower it to
    /// let the deformation linger.
    /// </summary>
    private const float ScaleRecovery = 1.6f;

    private static readonly Vector2 Body = new(BodyPixels, BodyPixels);

    /// <summary>
    /// The frame's pivot, in texels from its top-left corner, and the same vector from the body's
    /// corner to the point it anchors. Bottom-centre on purpose: a flip and a scale both work
    /// about the pivot, so the horizontal centre keeps the drawn frame over the corner-anchored
    /// collider in both facings, and the bottom edge keeps a squashed or stretched frame standing
    /// on the floor the body stands on. Both derive from the region's own extent.
    /// </summary>
    private static readonly Vector2 Pivot = new(BodyPixels / 2f, BodyPixels);

    /// <summary>The whole of <c>textures/player.png</c>.</summary>
    private static readonly Sprite Idle =
        new(GameAssets.Textures.Player, new TextureRegion(0, 0, BodyPixels, BodyPixels), Pivot);

    private readonly SpriteRenderer _sprite;
    private readonly KinematicBody2D _body;

    private Vector2 _velocity;

    public Player(EntitySpawn spawn)
        : base(spawn.Position)
    {
        _sprite = new SpriteRenderer(Idle) { Offset = Pivot };
        Add(_sprite);

        BoxCollider2D collider = new(Body);
        collider.Detects("sensor");
        collider.ReportsContacts = true;
        collider.ContactEntered += contact =>
            Log.Info(FormattableString.Invariant($"entered {contact.LayerName} at {contact.Point}"));
        collider.ContactExited += contact =>
            Log.Info(FormattableString.Invariant($"exited {contact.LayerName} at {contact.Point}"));
        Add(collider);

        _body = new KinematicBody2D(collider);
        _body.BlocksOn("solid", "platform");
        Add(_body);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        float delta = context.DeltaSeconds;

        // Recovery runs before this step's impulses, so an impulse set below is drawn whole.
        // On the fixed step, so the deformation is identical on every machine and frame rate.
        _sprite.Scale = new Vector2(
            Approach(_sprite.Scale.X, 1f, ScaleRecovery * delta),
            Approach(_sprite.Scale.Y, 1f, ScaleRecovery * delta));

        // The body applies no forces: velocity is the game's, every step.
        _velocity.X = context.Input.Axis(GameInput.Move) * WalkSpeed;
        _velocity.Y += Gravity * delta;

        // Facing is kept through a standstill, so the player stops looking where it walked.
        if (_velocity.X != 0f)
        {
            _sprite.FlipX = _velocity.X < 0f;
        }

        // IsOnFloor is state as of the last Move, so this reads the previous step's landing.
        bool wasOnFloor = _body.IsOnFloor;
        if (wasOnFloor && context.Input.WasPressed(GameInput.Jump))
        {
            _velocity.Y = -JumpSpeed;

            // Scale is presentation alone: the 8x8 box collider does not follow the frame, so
            // this taller player is exactly as tall to the sweep below as a resting one.
            _sprite.Scale = JumpStretch;
            Log.Info("jumped");
        }

        _body.Move(_velocity * delta);

        if (_body.IsOnFloor)
        {
            _velocity.Y = 0f;

            if (!wasOnFloor)
            {
                // The room's ledges collide on their top face alone, so landing on one names
                // 'platform' here while a jump up through it names nothing at all.
                foreach (ColliderContact2D contact in _body.MoveContacts)
                {
                    if (contact.Normal.Y < 0f)
                    {
                        _sprite.Scale = LandSquash;
                        Log.Info("landed on " + contact.LayerName);
                        break;
                    }
                }
            }
        }

        if (_body.IsOnCeiling)
        {
            _velocity.Y = 0f;
        }
    }

    /// <summary>
    /// Moves <paramref name="value"/> towards <paramref name="target"/> by at most
    /// <paramref name="maxDelta"/>, landing exactly on it rather than overshooting.
    /// </summary>
    private static float Approach(float value, float target, float maxDelta) =>
        value > target ? MathF.Max(value - maxDelta, target) : MathF.Min(value + maxDelta, target);
}
