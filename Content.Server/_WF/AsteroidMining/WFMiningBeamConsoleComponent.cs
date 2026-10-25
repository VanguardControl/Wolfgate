namespace Content.Server._WF.AsteroidMining;

/// <summary>Picks targets and sets power for the mining beam emitters on its grid.</summary>
[RegisterComponent]
public sealed partial class WFMiningBeamConsoleComponent : Component
{
    /// <summary>How far from the grid's centre the console lists asteroids.</summary>
    [DataField] public float Range = 150f;

    public TimeSpan NextCommand;
}
