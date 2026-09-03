using Capsule;
using Capsule.Scenes;
using TiledGame.Game.Cameras;

namespace TiledGame.Game.Scenes;

/// <summary>
/// The playable room: a scene that is a document and a class at once. The document is
/// <c>src/asset-sources/scenes/room.tmj</c>, a Tiled map the JAG.Capsule.Tiled package translates
/// at build time into a scene document that Capsule then validates and re-emits to
/// <c>assets/scenes/room.scene.json</c> beside the executable; <c>[SceneDocument("room")]</c> names
/// it, and without the attribute the kebab-cased class name would be the document name anyway. The
/// <see cref="SceneContent"/> constructor is the claim — a scene with one is composed from its
/// document, entry by entry in file order: the tile map first, then the <c>player</c> and
/// <c>sensor</c> placements.
/// <para>
/// This class is the code half of that scene: which camera it installs, and what quitting means.
/// The camera's own framing lives in <see cref="GameCamera"/>, not here.
/// <c>hall.scene.json</c> is the contrasting case — a document claimed by no class at all, which
/// still loads and plays as a plain <see cref="Scene"/>.
/// </para>
/// </summary>
[SceneDocument("room")]
public sealed class Room : Scene
{
    public Room(SceneContent content)
        : base(content)
    {
    }

    /// <inheritdoc/>
    protected override void OnStart() => Camera = new GameCamera();

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(GameInput.Quit))
        {
            RequestExit();
        }
    }
}
