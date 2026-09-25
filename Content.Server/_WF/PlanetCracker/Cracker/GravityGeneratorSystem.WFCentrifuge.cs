using Content.Shared.Gravity;

namespace Content.Server.Gravity;

public sealed partial class GravityGeneratorSystem
{
    /// <summary>Sets a generator's gravity flag and refreshes its grid; the crack fall uses it to drop lift.</summary>
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
