using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Parallax;
using Content.Shared.Mining.Components;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Caverns;

/// <summary>Reads a cavern's natural terrain from its noise alone; wfcavern stats and the cavern tests share it.</summary>
public sealed partial class WFCavernSampler : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    private static readonly Entity<MapGridComponent>? NoGrid = null;

    private readonly Dictionary<string, bool> _solid = new();
    private readonly Dictionary<string, bool> _veins = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<EntityPrototype>())
            return;

        _solid.Clear();
        _veins.Clear();
    }

    /// <summary>Samples a square of <paramref name="size"/> tiles centred on a tile, taking every <paramref name="step"/>th tile on each axis.</summary>
    public WFCavernSample Sample(BiomeComponent biome, Vector2i centre, int size, int step = 1)
    {
        var width = (size + step - 1) / step;
        var origin = centre - new Vector2i(size / 2, size / 2);
        var sample = new WFCavernSample(origin, width, step);

        for (var y = 0; y < width; y++)
        for (var x = 0; x < width; x++)
        {
            var index = origin + new Vector2i(x * step, y * step);

            if (!_biome.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var tile))
                continue;

            var i = y * width + x;
            sample.Tiles[i] = _tileDefs[tile.Value.TypeId].ID;

            if (_biome.TryGetEntity(index, biome.Layers, tile.Value, biome.Seed, NoGrid, out var entity))
            {
                sample.Entities[i] = entity;
                sample.Solid[i] = IsSolid(entity);
                sample.Vein[i] = IsVein(entity);
            }
        }

        return sample;
    }

    /// <summary>Whether an entity is rock: anything airtight, from plain walls to pillars and ice columns.</summary>
    public bool IsSolid(string entity)
    {
        if (!_solid.TryGetValue(entity, out var solid))
        {
            solid = _proto.Index<EntityPrototype>(entity).TryGetComponent<AirtightComponent>(out _, _factory);
            _solid[entity] = solid;
        }

        return solid;
    }

    /// <summary>Whether an entity is an ore vein: rock that always yields a set ore.</summary>
    public bool IsVein(string entity)
    {
        if (!_veins.TryGetValue(entity, out var vein))
        {
            vein = _proto.Index<EntityPrototype>(entity).TryGetComponent<OreVeinComponent>(out var ore, _factory)
                   && ore.CurrentOre != null;
            _veins[entity] = vein;
        }

        return vein;
    }
}

/// <summary>A square of natural cavern terrain, row by row from its bottom-left sample.</summary>
public sealed class WFCavernSample
{
    /// <summary>The bottom-left sampled tile.</summary>
    public readonly Vector2i Origin;

    /// <summary>Samples per row and per column.</summary>
    public readonly int Width;

    /// <summary>Tiles between neighbouring samples.</summary>
    public readonly int Step;

    /// <summary>Each sample's natural tile id, or null where the noise gives none.</summary>
    public readonly string?[] Tiles;

    /// <summary>Each sample's natural entity id, or null.</summary>
    public readonly string?[] Entities;

    /// <summary>Whether each sample's entity is rock.</summary>
    public readonly bool[] Solid;

    /// <summary>Whether each sample's entity is an ore vein.</summary>
    public readonly bool[] Vein;

    /// <summary>An empty sample of a square.</summary>
    public WFCavernSample(Vector2i origin, int width, int step)
    {
        Origin = origin;
        Width = width;
        Step = step;
        Tiles = new string?[width * width];
        Entities = new string?[width * width];
        Solid = new bool[width * width];
        Vein = new bool[width * width];
    }

    /// <summary>How many samples there are.</summary>
    public int Count => Tiles.Length;

    /// <summary>The tile a sample stands on.</summary>
    public Vector2i IndexOf(int sample)
    {
        return Origin + new Vector2i(sample % Width * Step, sample / Width * Step);
    }

    /// <summary>Whether a sample is open ground: it has a floor and no rock on it. Liquids and decor count as open.</summary>
    public bool IsOpen(int sample)
    {
        return Tiles[sample] != null && !Solid[sample];
    }

    /// <summary>The share of samples that are open ground.</summary>
    public float OpenFraction()
    {
        var open = 0;
        for (var i = 0; i < Count; i++)
        {
            if (IsOpen(i))
                open++;
        }

        return Count == 0 ? 0f : (float) open / Count;
    }

    /// <summary>The share of rock samples that are ore veins.</summary>
    public float VeinFraction()
    {
        var rock = Solid.Count(solid => solid);
        var veins = Vein.Count(vein => vein);
        return rock == 0 ? 0f : (float) veins / rock;
    }

    /// <summary>The share of open samples in the largest 4-connected open region.</summary>
    public float LargestRegionShare()
    {
        var seen = new bool[Count];
        var queue = new Queue<int>();
        var open = 0;
        var largest = 0;

        for (var start = 0; start < Count; start++)
        {
            if (!IsOpen(start))
                continue;

            open++;

            if (seen[start])
                continue;

            var size = 0;
            seen[start] = true;
            queue.Enqueue(start);

            while (queue.TryDequeue(out var current))
            {
                size++;
                var x = current % Width;
                var y = current / Width;

                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            largest = Math.Max(largest, size);
        }

        return open == 0 ? 0f : (float) largest / open;

        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Width)
                return;

            var next = y * Width + x;
            if (seen[next] || !IsOpen(next))
                return;

            seen[next] = true;
            queue.Enqueue(next);
        }
    }

    /// <summary>The centred square of <paramref name="width"/> samples.</summary>
    public WFCavernSample Crop(int width)
    {
        var inset = (Width - width) / 2;
        var crop = new WFCavernSample(Origin + new Vector2i(inset * Step, inset * Step), width, Step);

        for (var y = 0; y < width; y++)
        for (var x = 0; x < width; x++)
        {
            var from = (y + inset) * Width + x + inset;
            var to = y * width + x;
            crop.Tiles[to] = Tiles[from];
            crop.Entities[to] = Entities[from];
            crop.Solid[to] = Solid[from];
            crop.Vein[to] = Vein[from];
        }

        return crop;
    }
}
