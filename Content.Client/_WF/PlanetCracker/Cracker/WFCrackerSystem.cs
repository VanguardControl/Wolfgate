using Content.Shared._WF.PlanetCracker.Cracker;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>
/// Client half of the cracker: no subscriptions and no body. It exists only so the shared geometry base is registered
/// at all - ReflectionManager.GetAllChildren skips abstract types, and EntitySystemManager registers a base type only
/// from a concrete subtype, so without this file every client resolution of SharedWFCrackerSystem throws
/// UnregisteredTypeException the first time a diagram draws.
/// </summary>
public sealed partial class WFCrackerSystem : SharedWFCrackerSystem;
