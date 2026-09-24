namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>How a body was deliberately ended (M2, OD17).</summary>
public enum WolfmedEnding : byte
{
    /// <summary>Catastrophic brain injury, revivable by brain (or core) repair and a shock or restart.</summary>
    Execution,

    /// <summary>The same injury; the ghost that already left cannot come back.</summary>
    Suicide,
}

/// <summary>
/// M2 (OD17, P10, P29): raised directed on the victim by the marked lines in the shared execution system. On a wound
/// host the server sets the brain (or positronic core) to 0 where it sits and then kills the body, the order Succumb
/// uses. Nothing subscribes on the client.
/// </summary>
[ByRefEvent]
public record struct WolfmedEndingEvent(WolfmedEnding Ending, bool Handled = false);
