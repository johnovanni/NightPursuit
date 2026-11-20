namespace NightPursuit.Core.Session;

/// <summary>
/// "Release first": after <see cref="Begin"/> (a result screen has just appeared), a confirm is ignored until every confirm/fire
/// control has been let go once. A player still holding the guns as the mission ends then cannot skip the screen by accident.
/// </summary>
public sealed class ReleaseFirstGate
{
    /// <summary>True while a confirm should still be ignored.</summary>
    public bool Waiting { get; private set; }

    /// <summary>Starts waiting for the release (call when the screen appears).</summary>
    public void Begin() => Waiting = true;

    /// <summary>Call once per frame with whether any confirm/fire control is held; the first frame with none held ends the wait.</summary>
    public void Update(bool anyConfirmHeld)
    {
        if (Waiting && !anyConfirmHeld) Waiting = false;
    }
}
