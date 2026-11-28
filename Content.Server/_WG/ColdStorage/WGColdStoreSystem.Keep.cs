using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Wires;
using Content.Shared.NodeContainer;

namespace Content.Server._WG.ColdStorage;

/// <summary>State a save does not write, carried through a store in components of its own.</summary>
public sealed partial class WGColdStoreSystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;

    private readonly List<EntityUid> _keepWalk = new();
    private readonly List<EntityUid> _keptDone = new();

    /// <summary>Notes on each pipe the share of its net's gas it holds, before the grid is copied.</summary>
    private void KeepUnsaved(EntityUid grid)
    {
        _keepWalk.Clear();
        _keepWalk.Add(grid);
        for (var i = 0; i < _keepWalk.Count; i++)
        {
            var uid = _keepWalk[i];
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _keepWalk.Add(child);
            }

            KeepEntity(uid);
        }

        _keepWalk.Clear();
    }

    /// <summary>
    /// Notes on one entity what a save does not write: a board's wires, and a pipe's share of its net's gas. For
    /// anything about to be saved and deleted; <see cref="DropKeptOn"/> takes the notes off if it is not.
    /// </summary>
    public void KeepEntity(EntityUid uid)
    {
        KeepWires(uid);

        if (!TryComp<NodeContainerComponent>(uid, out var nodes))
            return;

        WGKeptPipeGasComponent? kept = null;
        foreach (var (name, node) in nodes.Nodes)
        {
            if (node is not PipeNode pipe || pipe.NodeGroup is not IPipeNet net
                || net.Air.TotalMoles <= 0f || net.Air.Volume <= 0f)
                continue;

            var share = net.Air.Clone();
            share.Multiply(Math.Clamp(pipe.Volume / net.Air.Volume, 0f, 1f));
            share.Volume = pipe.Volume;
            kept ??= EnsureComp<WGKeptPipeGasComponent>(uid);
            kept.Saving = true;
            kept.Nodes[name] = share;
        }
    }

    /// <summary>Takes the notes of <see cref="KeepEntity"/> off an entity that was not stored after all.</summary>
    public void DropKeptOn(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;

        RemComp<WGKeptPipeGasComponent>(uid);
        RemComp<WGKeptWiresComponent>(uid);
    }

    /// <summary>Takes the notes off a grid that was not stored after all; its pipes still hold the gas.</summary>
    private void DropKept(EntityUid grid)
    {
        if (TerminatingOrDeleted(grid))
            return;

        _keepWalk.Clear();
        _keepWalk.Add(grid);
        for (var i = 0; i < _keepWalk.Count; i++)
        {
            var uid = _keepWalk[i];
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _keepWalk.Add(child);
            }

            RemComp<WGKeptPipeGasComponent>(uid);
            RemComp<WGKeptWiresComponent>(uid);
        }

        _keepWalk.Clear();
    }

    /// <summary>Notes a board's cut wires, seed and serial number.</summary>
    private void KeepWires(EntityUid uid)
    {
        if (!TryComp<WiresComponent>(uid, out var wires) || wires.WiresList.Count == 0)
            return;

        var kept = EnsureComp<WGKeptWiresComponent>(uid);
        kept.Seed = wires.WireSeed;
        kept.Serial = wires.SerialNumber;
        kept.Cut.Clear();
        foreach (var wire in wires.WiresList)
        {
            if (wire.IsCut)
                kept.Cut.Add(wire.Id);
        }
    }

    /// <summary>
    /// Puts kept wire state back on a restored grid's boards, after the re-init has given them their wires. Only the
    /// cut itself is restored: what a cut did (a door's power, its bolts) was saved with the device.
    /// </summary>
    private void GiveBackWires(EntityUid grid)
    {
        _keepWalk.Clear();
        _keepWalk.Add(grid);
        for (var i = 0; i < _keepWalk.Count; i++)
        {
            var uid = _keepWalk[i];
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _keepWalk.Add(child);
            }

            GiveBackWiresTo(uid);
        }

        _keepWalk.Clear();
    }

    /// <summary>Puts kept wire state back on one restored board, after the re-init has given it its wires.</summary>
    public void GiveBackWiresTo(EntityUid uid)
    {
        if (!TryComp<WGKeptWiresComponent>(uid, out var kept))
            return;

        if (TryComp<WiresComponent>(uid, out var wires))
        {
            if (kept.Seed != 0)
                wires.WireSeed = kept.Seed;

            if (kept.Serial != null)
                wires.SerialNumber = kept.Serial;

            foreach (var wire in wires.WiresList)
            {
                if (kept.Cut.Contains(wire.Id))
                    wire.IsCut = true;
            }
        }

        RemComp<WGKeptWiresComponent>(uid);
    }

    /// <summary>
    /// Gives kept gas back to pipes of restored grids once their nets exist, which is a tick or two after the load.
    /// </summary>
    private void GiveBackKept()
    {
        var query = AllEntityQuery<WGKeptPipeGasComponent, NodeContainerComponent>();
        while (query.MoveNext(out var uid, out var kept, out var nodes))
        {
            if (kept.Saving)
                continue;

            var waiting = false;
            foreach (var (name, gas) in kept.Nodes)
            {
                if (!nodes.Nodes.TryGetValue(name, out var node) || node is not PipeNode pipe)
                    continue;

                if (pipe.NodeGroup is not IPipeNet)
                    waiting = true;
            }

            if (waiting)
                continue;

            foreach (var (name, gas) in kept.Nodes)
            {
                if (nodes.Nodes.TryGetValue(name, out var node) && node is PipeNode { NodeGroup: IPipeNet net })
                    _atmos.Merge(net.Air, gas);
            }

            _keptDone.Add(uid);
        }

        foreach (var uid in _keptDone)
        {
            RemComp<WGKeptPipeGasComponent>(uid);
        }

        _keptDone.Clear();
    }
}
