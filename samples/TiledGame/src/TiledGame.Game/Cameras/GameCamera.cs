using Capsule;
using Capsule.Scenes;
using TiledGame.Game.Entities;

namespace TiledGame.Game.Cameras;

/// <summary>
/// The follow camera: it owns the span the room is framed at, finds the player in
/// <see cref="OnStart"/>, and settles the follow in <see cref="OnLateStep"/>. A scene installs it
/// and touches it no further.
/// </summary>
public sealed class GameCamera : Camera
{
    private Player _subject = null!;

    public GameCamera() => ViewportSize = World.ViewportSize;

    /// <inheritdoc/>
    protected override void OnStart()
    {
        _subject = Scene!.FindSingle<Player>();

        // The room opens framed on the player rather than sweeping to it from the world origin.
        Teleport(_subject.Position);
    }

    /// <inheritdoc/>
    protected override void OnLateStep(in StepContext context) => Center = _subject.Position;
}
