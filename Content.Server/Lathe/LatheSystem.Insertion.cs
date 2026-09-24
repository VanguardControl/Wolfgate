using System.Linq;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Lathe;

namespace Content.Server.Lathe;

public sealed partial class LatheSystem
{
    [Dependency] private EntityStorageSystem _entityStorage = default!;

    private void OnInteractUsing(EntityUid uid, LatheComponent component, InteractUsingEvent args)
    {
        if (args.Handled ||
            !TryComp<EntityStorageComponent>(uid, out var storage) ||
            MetaData(args.Used).EntityPrototype?.ID is not { } partPrototype)
            return;

        var needed = GetAvailableRecipes(uid, component).Any(id =>
            _proto.Index(id).Entities.Keys.Any(part => part.Id == partPrototype));
        if (!needed)
            return;

        // EntityStorage.Insert drops items next to an open compartment. Close it
        // first so a part used on the lathe goes into storage in either state.
        if (storage.Open && !_entityStorage.TryCloseStorage(uid))
            return;

        if (!_entityStorage.CanInsert(args.Used, uid, storage))
            return;

        if (!_entityStorage.AddToContents(args.Used, uid, storage))
            return;

        args.Handled = true;
        TryStartProducing(uid, component);
        UpdateUserInterfaceState(uid, component);
    }
}
