using System.IO;
using System.Threading.Tasks;
using Content.Server._WG.Planets;
using Content.Shared._WG.Caverns;
using Content.Shared.Mind.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.ColdStorage;

/// <summary>Saving, deleting and rebuilding a world, each a slice a tick.</summary>
public sealed partial class WGPlanetStoreSystem
{
    /// <summary>
    /// Sorts everything on a world's layers and hulls into what goes into the file and what stays, and starts the
    /// save. Atmos is stopped on the layers and hulls first: their walls are about to go.
    /// </summary>
    private void BeginUnload(WGStoredPlanet planet, WGPlanetNetworkComponent net)
    {
        planet.Tops.Clear();
        planet.Slept.Clear();
        planet.Entities.Clear();
        planet.Hollow.Clear();
        planet.Next = 0;
        planet.Frozen = 0;
        planet.Froze.Clear();
        planet.Errors = 0;
        planet.KeptEntities = 0;
        planet.Zip = null;
        planet.File = null;
        planet.LoadWanted = false;

        var kept = new List<EntityUid>();
        var stored = new HashSet<EntityUid>();
        foreach (var layer in Maps(net, false))
        {
            if (TerminatingOrDeleted(layer))
                continue;

            planet.Hollow.Add(layer);
            var children = Transform(layer).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                // A hull standing on the world stays, as a ship's does in space; what is on it is sorted like the rest.
                if (_gridQuery.HasComp(child))
                {
                    planet.Hollow.Add(child);
                    var aboard = Transform(child).ChildEnumerator;
                    while (aboard.MoveNext(out var onHull))
                    {
                        Sort(planet, onHull, kept, stored);
                    }

                    continue;
                }

                Sort(planet, child, kept, stored);
            }
        }

        foreach (var uid in planet.Hollow)
        {
            _stasis.SetHollow(uid, true);
            _away.Add(uid);
        }

        // What stays sleeps until the world is whole again; what the site stash already put to sleep is its own.
        foreach (var top in kept)
        {
            _walk.Clear();
            _walk.Add(top);
            for (var i = 0; i < _walk.Count; i++)
            {
                planet.KeptEntities++;
                if (_stash.PauseEntity(_walk[i]))
                    planet.Slept.Add(_walk[i]);

                var children = Transform(_walk[i]).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    _walk.Add(child);
                }
            }
        }

        _walk.Clear();

        planet.Serializer = new EntitySerializer(_dependencies, SerializationOptions.Default with
        {
            MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
            LogAutoInclude = null,
            ErrorOnOrphan = false,
            KeepExternalReferences = true,
            KeepExternalParents = true,
        });

        // Only what will save is written; a sound or a passing effect inside a stored tree goes with it.
        planet.Entities.RemoveAll(uid => !planet.Serializer.IsSerializable(uid));
        stored.IntersectWith(planet.Entities);
        planet.Serializer.ReserveYamlIds(stored);
        planet.State = WGPlanetStoreState.Saving;
        _saving = planet;
    }

    /// <summary>Puts a child of a layer or hull, with its tree, among the stored or the kept.</summary>
    private void Sort(WGStoredPlanet planet, EntityUid top, List<EntityUid> kept, HashSet<EntityUid> stored)
    {
        var start = planet.Entities.Count;
        var keep = false;
        _walk.Clear();
        _walk.Add(top);
        for (var i = 0; i < _walk.Count; i++)
        {
            var uid = _walk[i];
            if (_metaQuery.GetComponent(uid).EntityPrototype is { MapSavable: false } && !Harmless(uid)
                || HasComp<ActorComponent>(uid)
                || TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind
                || Registered(uid))
            {
                keep = true;
                break;
            }

            planet.Entities.Add(uid);
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                _walk.Add(child);
            }
        }

        _walk.Clear();
        if (keep)
        {
            planet.Entities.RemoveRange(start, planet.Entities.Count - start);
            kept.Add(top);
            return;
        }

        planet.Tops.Add(top);
        for (var i = start; i < planet.Entities.Count; i++)
        {
            stored.Add(planet.Entities[i]);
            _coldStore.KeepEntity(planet.Entities[i]);
        }
    }

    /// <summary>
    /// Whether something outside the entity keeps a list it is on and would make a new one when it went: a cavern
    /// mouth's shade is in its ground's registry, which cuts a fresh shade over any hole that has lost its own.
    /// </summary>
    private bool Registered(EntityUid uid)
    {
        return HasComp<WGCavernShaftComponent>(uid);
    }

    private void StepUnloading(WGStoredPlanet planet)
    {
        if (planet.State == WGPlanetStoreState.Deleting)
        {
            StepDeleting(planet);
            if (planet.State == WGPlanetStoreState.Unloaded && planet.LoadWanted)
            {
                planet.LoadWanted = false;
                BeginLoad(planet);
            }

            return;
        }

        if (planet.State != WGPlanetStoreState.Saving || planet.Serializer is not { } serializer)
        {
            _saving = null;
            return;
        }

        if (planet.Zip != null)
        {
            if (!planet.Zip.IsCompleted)
                return;

            var bytes = planet.Zip.IsCompletedSuccessfully ? planet.Zip.GetAwaiter().GetResult() : null;
            if (bytes == null)
            {
                FailSave(planet, "the file could not be written");
                return;
            }

            planet.File = bytes;
            planet.Bytes = bytes.Length;
            planet.Ids = new Dictionary<EntityUid, int>(serializer.YamlUidMap);
            planet.Serializer = null;
            planet.StoredEntities = planet.Entities.Count;
            planet.Next = 0;
            planet.State = WGPlanetStoreState.Deleting;
            return;
        }

        // What is to be written sleeps first, so it is deleted as it was written: nothing fills, spawns in or leaves
        // a stored thing in between. What the site stash already put to sleep is its own.
        _timer.Restart();
        while (planet.Frozen < planet.Entities.Count)
        {
            if (_timer.Elapsed.TotalMilliseconds >= _budgetMs)
            {
                planet.SaveMs += _timer.Elapsed.TotalMilliseconds;
                return;
            }

            var uid = planet.Entities[planet.Frozen++];
            if (_stash.PauseEntity(uid))
                planet.Froze.Add(uid);
        }

        _slicing = planet;
        try
        {
            while (planet.Next < planet.Entities.Count)
            {
                if (_timer.Elapsed.TotalMilliseconds >= _budgetMs)
                {
                    planet.SaveMs += _timer.Elapsed.TotalMilliseconds;
                    return;
                }

                var uid = planet.Entities[planet.Next++];
                if (!TerminatingOrDeleted(uid))
                    serializer.SerializeEntity(uid);
            }

            if (planet.Errors != 0)
            {
                FailSave(planet, "the serializer logged errors");
                return;
            }

            var node = serializer.Write();
            planet.SaveMs += _timer.Elapsed.TotalMilliseconds;
            var name = planet.Name;
            planet.Zip = Task.Run(() => Compress(_shipyard.WGWriteCopy(node, name)));
        }
        catch (Exception e)
        {
            FailSave(planet, $"{e.GetType().Name}: {e.Message}");
        }
        finally
        {
            _slicing = null;
        }
    }

    /// <summary>Stops a save that has deleted nothing and leaves the world as it was.</summary>
    private void CancelSave(WGStoredPlanet planet)
    {
        foreach (var uid in planet.Entities)
        {
            _coldStore.DropKeptOn(uid);
        }

        Wake(planet);
        foreach (var uid in planet.Froze)
        {
            _stash.WakeEntity(uid);
        }

        planet.Froze.Clear();
        planet.Serializer = null;
        planet.Zip = null;
        planet.Entities.Clear();
        planet.Tops.Clear();
        planet.EmptySince = null;
        planet.State = WGPlanetStoreState.Loaded;
        if (_saving == planet)
            _saving = null;
    }

    private void FailSave(WGStoredPlanet planet, string why)
    {
        planet.Never = true;
        planet.LastError = why;
        Log.Error($"Could not unload {planet.Name}: {why}. Nothing was deleted; it stays loaded for the round.");
        CancelSave(planet);
    }

    /// <summary>Starts atmos again on a world's layers and hulls and wakes what this system put to sleep.</summary>
    private void Wake(WGStoredPlanet planet)
    {
        foreach (var uid in planet.Hollow)
        {
            _stasis.SetHollow(uid, false);
            _away.Remove(uid);
        }

        planet.Hollow.Clear();
        foreach (var uid in planet.Slept)
        {
            _stash.WakeEntity(uid);
        }

        planet.Slept.Clear();
    }

    /// <summary>
    /// Deletes what the file holds, a slice a tick. The world went on between being written and now, so only what
    /// was written goes: anything that has got into a stored tree since is set down outside it first, and anything
    /// written that has got out of its tree is deleted on its own, so nothing is lost and nothing comes back twice.
    /// </summary>
    private void StepDeleting(WGStoredPlanet planet)
    {
        _timer.Restart();
        var budget = planet.LoadWanted ? _urgentMs : _budgetMs;
        while (planet.Next < planet.Tops.Count)
        {
            if (_timer.Elapsed.TotalMilliseconds >= budget)
            {
                planet.DeleteMs += _timer.Elapsed.TotalMilliseconds;
                return;
            }

            var top = planet.Tops[planet.Next++];
            if (Deleted(top))
                continue;

            SetDownNewcomers(planet, top);
            Del(top);
        }

        foreach (var uid in planet.Entities)
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }

        planet.DeleteMs += _timer.Elapsed.TotalMilliseconds;
        planet.Tops.Clear();
        planet.Entities.Clear();
        planet.Next = 0;
        planet.UnloadedAt = _timing.CurTime;
        planet.State = WGPlanetStoreState.Unloaded;
        StoredEntities += planet.StoredEntities;
        if (_saving == planet)
            _saving = null;

        Log.Info($"Unloaded {planet.Name}: {planet.StoredEntities} entities stored, {planet.KeptEntities} kept asleep, save {planet.SaveMs:0} ms, delete {planet.DeleteMs:0} ms, {planet.Bytes} bytes.");
    }

    /// <summary>Moves out of a tree about to be deleted whatever is in it that the file does not hold.</summary>
    private void SetDownNewcomers(WGStoredPlanet planet, EntityUid top)
    {
        if (planet.Ids is not { } written)
            return;

        _walk.Clear();
        _newcomers.Clear();
        _walk.Add(top);
        for (var i = 0; i < _walk.Count; i++)
        {
            var children = Transform(_walk[i]).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (written.ContainsKey(child))
                    _walk.Add(child);
                else if (!Harmless(child) && !TerminatingOrDeleted(child))
                    _newcomers.Add(child);
            }
        }

        _walk.Clear();
        foreach (var uid in _newcomers)
        {
            _transform.AttachToGridOrMap(uid);
        }

        if (_newcomers.Count > 0)
            Log.Info($"Unloading {planet.Name}: {_newcomers.Count} entities that entered {ToPrettyString(top)} after it was written were set down beside it.");

        _newcomers.Clear();
    }

    private void BeginLoad(WGStoredPlanet planet)
    {
        if (planet.File is not { } bytes)
        {
            planet.Failures = MaxFailures;
            planet.LastError = "its file is missing";
            Log.Error($"Could not load {planet.Name}: its file is missing.");
            return;
        }

        planet.Data = null;
        planet.Load = null;
        planet.LoadMs = 0;
        planet.LoadTicks = 0;
        planet.WorstMs = 0;
        planet.Parse = Task.Run(() =>
        {
            if (MapLoaderSystem.TryParse(new StringReader(Decompress(bytes)), out var data))
                planet.Data = data;
        });
        planet.State = WGPlanetStoreState.Loading;
    }

    /// <summary>
    /// Gives a loading world this tick's share: more once someone is on a layer below orbit, where they may see the
    /// ground fill in.
    /// </summary>
    private void StepLoading(WGStoredPlanet planet)
    {
        if (planet.Parse is not { IsCompleted: true } parse)
            return;

        if (parse.IsFaulted || planet.Data is not { } data)
        {
            FailLoad(planet, parse.Exception?.GetBaseException().Message ?? "its file holds nothing");
            return;
        }

        _timer.Restart();
        planet.LoadTicks++;
        // An unset entity field is written as "invalid", which is not worth an error on the way back.
        planet.Load ??= _loader.BeginLoadEntities(data, planet.Name, new DeserializationOptions { LogInvalidEntities = false }, migrate: false);
        var budget = TimeSpan.FromMilliseconds(Urgent(planet) ? _urgentMs : _budgetMs);
        var finished = _loader.StepLoad(planet.Load, budget);
        planet.WorstMs = Math.Max(planet.WorstMs, _timer.Elapsed.TotalMilliseconds);
        if (!finished)
            return;

        var load = planet.Load;
        planet.Load = null;
        planet.Parse = null;
        planet.Data = null;
        if (load.State != StagedLoadState.Done || load.Result is not { } result)
        {
            FailLoad(planet, "the load failed");
            return;
        }

        FinishLoad(planet, load, result);
    }

    /// <summary>Whether a player is already on a layer of the world below orbit.</summary>
    private bool Urgent(WGStoredPlanet planet)
    {
        if (!TryComp<WGPlanetNetworkComponent>(planet.Network, out var net))
            return false;

        var maps = Maps(net, false);
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { } body && OnAny(body, maps))
                return true;
        }

        return false;
    }

    private void FinishLoad(WGStoredPlanet planet, StagedGridLoad load, LoadResult result)
    {
        var loaded = new List<EntityUid>(result.Entities);
        _reinit.ReinitLoadedEntities(loaded);
        foreach (var uid in loaded)
        {
            _coldStore.GiveBackWiresTo(uid);
        }

        var away = _timing.CurTime - planet.UnloadedAt;
        StoredEntities -= planet.StoredEntities;
        planet.LoadMs = load.Elapsed.TotalMilliseconds;
        planet.File = null;
        planet.Failures = 0;
        planet.EmptySince = null;
        planet.State = WGPlanetStoreState.Loaded;
        Wake(planet);

        var became = new Dictionary<EntityUid, EntityUid>();
        if (planet.Ids != null && load.UidMap != null)
        {
            foreach (var (old, id) in planet.Ids)
            {
                if (load.UidMap.TryGetValue(id, out var now))
                    became[old] = now;
            }
        }

        // The file has what this put to sleep ahead of the save as asleep; it wakes, and nothing else does.
        foreach (var old in planet.Froze)
        {
            if (became.TryGetValue(old, out var now))
                _stash.WakeEntity(now);
        }

        planet.Froze.Clear();
        _catchUp.CatchUp(planet.Network, away, loaded);

        planet.Ids = null;
        var ev = new WGPlanetRestoredEvent(planet.Network, became);
        RaiseLocalEvent(ref ev);

        var message = $"Loaded {planet.Name}: {loaded.Count} entities, load {planet.LoadMs:0} ms over {planet.LoadTicks} ticks, slowest {planet.WorstMs:0.#} ms.";
        Log.Info(message);
    }

    private void FailLoad(WGStoredPlanet planet, string why)
    {
        planet.Load = null;
        planet.Parse = null;
        planet.Data = null;
        planet.Failures++;
        planet.LastError = why;
        planet.State = WGPlanetStoreState.Unloaded;
        var next = planet.Failures < MaxFailures ? "it is tried again" : "it is left for an admin";
        Log.Error($"Could not load {planet.Name}, try {planet.Failures} of {MaxFailures}: {why}. Its file is kept; {next}.");
    }
}
