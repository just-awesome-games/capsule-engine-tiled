using Capsule;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace TiledGame.Game.Scenes;

/// <summary>
/// The boot scene, and the one backed by no <c>*.scene.json</c>. Its public
/// parameterless constructor is what marks it class-only: <c>RunScene&lt;MainMenu&gt;()</c> builds it
/// as it is, with no document composed into it. It draws nothing on purpose — it holds no entities,
/// and says what it wants through the log.
/// </summary>
public sealed class MainMenu : Scene
{
    /// <inheritdoc/>
    protected override void OnStart()
    {
        // The other half of the camera model: a scene with nothing to follow spans the plain
        // camera it is given rather than installing one of its own.
        Camera.ViewportSize = World.ViewportSize;
        Log.Info("Main menu: press Confirm to enter the room, Quit to leave.");
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Confirm))
        {
            RequestScene<Room>();
        }
        else if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }
}
