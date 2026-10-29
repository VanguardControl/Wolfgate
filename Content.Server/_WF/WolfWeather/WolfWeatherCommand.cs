using System.Globalization;
using Content.Server.Administration;
using Content.Shared._WF.WolfWeather;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.WolfWeather;

/// <summary>Admin command that sets or clears the weather on a map.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class WolfWeatherCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;

    public override string Command => "wolfweather";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteError(Loc.GetString("cmd-wolfweather-help"));
            return;
        }

        if (!WolfWeatherCommandHelpers.TryParseMap(shell, _map, args[0], out var mapUid))
            return;

        ProtoId<WolfWeatherPrototype>? weather = null;
        if (args[1] != "clear")
        {
            if (!_proto.HasIndex<WolfWeatherPrototype>(args[1]))
            {
                shell.WriteError(Loc.GetString("wolf-weather-command-bad-weather"));
                return;
            }

            weather = args[1];
        }

        TimeSpan? duration = null;
        if (args.Length == 3)
        {
            if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0f)
            {
                shell.WriteError(Loc.GetString("cmd-wolfweather-help"));
                return;
            }

            duration = TimeSpan.FromSeconds(seconds);
        }

        _wolfWeather.SetWeather(mapUid, weather, duration);
        shell.WriteLine(Loc.GetString("wolf-weather-command-set", ("weather", args[1])));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => WolfWeatherCommandHelpers.MapCompletion(EntityManager),
            2 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<WolfWeatherPrototype>(), Loc.GetString("wolf-weather-command-hint-weather")),
            3 => CompletionResult.FromHint(Loc.GetString("wolf-weather-command-hint-seconds")),
            _ => CompletionResult.Empty,
        };
    }
}
