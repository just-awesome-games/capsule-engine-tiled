using Capsule.Input;

namespace TiledGame.Game;

/// <summary>
/// The game's actions, and the one place devices are named. An action is the seam between a device
/// and the logic that reacts to it: the shell installs these bindings once through
/// <c>WithBindings</c>, and everything else in the game reads actions, never keys or pad buttons.
/// </summary>
public static class GameInput
{
    /// <summary>Horizontal movement, in [-1, 1].</summary>
    public static readonly AxisAction Move = new("move");

    /// <summary>Leaves the floor.</summary>
    public static readonly InputAction Jump = new("jump");

    /// <summary>Accepts the menu.</summary>
    public static readonly InputAction Confirm = new("confirm");

    /// <summary>Leaves the game.</summary>
    public static readonly InputAction Quit = new("quit");

    /// <summary>Binds every action to the devices the game supports.</summary>
    public static void Bind(ActionBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        // Axis contributions accumulate, so each pair adds another way to push the same axis.
        bindings.BindAxis(Move, Key.A, Key.D);
        bindings.BindAxis(Move, Key.Left, Key.Right);
        bindings.BindAxis(Move, PadButton.DPadLeft, PadButton.DPadRight);
        bindings.BindAxis(Move, PadAxis.LeftStickX);

        bindings.Bind(Jump, Key.Space, PadButton.South);
        bindings.Bind(Confirm, Key.Enter, Key.Space, PadButton.South);
        bindings.Bind(Quit, Key.Escape, PadButton.Start);
    }
}
