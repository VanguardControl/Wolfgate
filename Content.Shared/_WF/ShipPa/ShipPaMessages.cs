using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipPa;

/// <summary>Pilot asked for a new situation code.</summary>
[Serializable, NetSerializable]
public sealed class ShipAlertCodeRequestMessage : BoundUserInterfaceMessage
{
    public string Code;

    public ShipAlertCodeRequestMessage(string code)
    {
        Code = code;
    }
}

/// <summary>Pilot sounded or secured from general quarters.</summary>
[Serializable, NetSerializable]
public sealed class ShipGeneralQuartersRequestMessage : BoundUserInterfaceMessage
{
    public bool Active;

    public ShipGeneralQuartersRequestMessage(bool active)
    {
        Active = active;
    }
}

/// <summary>Pilot typed a free-text announcement.</summary>
[Serializable, NetSerializable]
public sealed class ShipPaAnnounceRequestMessage : BoundUserInterfaceMessage
{
    public string Text;

    public ShipPaAnnounceRequestMessage(string text)
    {
        Text = text;
    }
}

/// <summary>Pilot queued a link to play over the ship's own speakers.</summary>
[Serializable, NetSerializable]
public sealed class ShipPaInternetSoundRequestMessage : BoundUserInterfaceMessage
{
    public string Url;

    public ShipPaInternetSoundRequestMessage(string url)
    {
        Url = url;
    }
}

/// <summary>Pilot cut whatever the ship was playing.</summary>
[Serializable, NetSerializable]
public sealed class ShipPaInternetSoundStopMessage : BoundUserInterfaceMessage;
