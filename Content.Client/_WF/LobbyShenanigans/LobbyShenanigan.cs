using System.Numerics;
using Content.Shared._WF.LobbyShenanigans;
using System.Collections.Generic;
using Robust.Client.Graphics;

namespace Content.Client._WF.LobbyShenanigans;

/// <summary>
/// One bit of lobby fun, set off for every client from the SSymphony panel. A subclass is found by reflection and
/// moves and draws on a layer over the whole lobby that never takes a click.
/// </summary>
public abstract class LobbyShenanigan
{
    /// <summary>One of <see cref="LobbyShenaniganIds"/>, which also names its Fluent strings.</summary>
    public abstract string Id { get; }

    /// <summary>Every shenanigan on the layer, this one too, so one can play with what another shows.</summary>
    public IReadOnlyList<LobbyShenanigan> All { get; internal set; } = Array.Empty<LobbyShenanigan>();

    /// <summary>The server set it off.</summary>
    public abstract void Trigger(Vector2 bounds, float scale);

    /// <summary>Moves on by <paramref name="dt"/> seconds in a layer of <paramref name="bounds"/> pixels.</summary>
    public abstract void Update(float dt, Vector2 bounds, float scale);

    /// <summary>Draws in the layer's pixels.</summary>
    public abstract void Draw(DrawingHandleScreen handle, float scale);

    /// <summary>Staff cleared the lobby: everything it shows goes.</summary>
    public abstract void Clear();

    /// <summary>Whether something it shows is under <paramref name="pos"/>, in the layer's pixels; the layer takes clicks only there.</summary>
    public virtual bool HitTest(Vector2 pos) => false;

    /// <summary>Picks up what is under <paramref name="pos"/>; false when there is nothing to pick up.</summary>
    public virtual bool TryGrab(Vector2 pos) => false;

    /// <summary>The mouse moved to <paramref name="pos"/> while holding what was picked up.</summary>
    public virtual void Drag(Vector2 pos, float dt)
    {
    }

    /// <summary>The mouse button came up.</summary>
    public virtual void Release()
    {
    }
}

/// <summary>A shenanigan whose pieces get knocked aside by something passing through, such as a carp.</summary>
public interface ILobbyShovable
{
    /// <summary>Pushes whatever is within <paramref name="radius"/> of <paramref name="at"/> out of the way, along with <paramref name="velocity"/>.</summary>
    void Shove(Vector2 at, float radius, Vector2 velocity);
}
