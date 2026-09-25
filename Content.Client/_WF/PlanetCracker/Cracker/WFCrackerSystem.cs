using Content.Shared._WF.PlanetCracker.Cracker;

namespace Content.Client._WF.PlanetCracker.Cracker;

/// <summary>Registers the shared cracker base on the client; abstract systems only register through a concrete subtype.</summary>
public sealed partial class WFCrackerSystem : SharedWFCrackerSystem;
