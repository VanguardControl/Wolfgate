using Content.Shared.Gravity;

namespace Content.Server.Gravity;

public sealed partial class GravityGeneratorSystem
{
    /// <summary>
    /// Sets a generator's gravity flag from content and refreshes the grid it sits on, mirroring the activate and
    /// deactivate handlers. This is the only [Access]-gated write the crack feature makes, which is why it lives in a
    /// _WF partial of the owning system rather than reaching in from outside.
    /// The Falling push uses it to stop the centrifuge counting as lift: the pooled capacity rebuild skips a generator
    /// whose GravityActive is clear, and every other route is worse - a zero rating reads as infinite lift, cutting
    /// power takes the full 240 s discharge, and the virtual mass is rewritten every second by the capacity sweep.
    /// </summary>
    public void WfSetGravityActive(Entity<GravityGeneratorComponent> ent, bool active)
    {
        ent.Comp.GravityActive = active;

        var xform = Transform(ent);

        if (!TryComp(xform.ParentUid, out GravityComponent? gravity))
            return;

        if (active)
            _gravitySystem.EnableGravity(xform.ParentUid, gravity);
        else
            _gravitySystem.RefreshGravity(xform.ParentUid, gravity);
    }
}
