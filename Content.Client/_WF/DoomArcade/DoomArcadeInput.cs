using Content.Client._WF.DoomArcade.Engine;
using Content.Client._WF.DoomArcade.Engine.UserInput;

namespace Content.Client._WF.DoomArcade;

/// <summary>Builds the per-tic player command from held keys. Keyboard only.</summary>
public sealed class DoomArcadeInput : IUserInput
{
    private const int WeaponKeys = 7;

    private readonly Config _config;
    private readonly HashSet<DoomKey> _held = new();
    private int _turnHeld;

    public int MaxMouseSensitivity => 15;

    public int MouseSensitivity
    {
        get => _config.mouse_sensitivity;
        set => _config.mouse_sensitivity = value;
    }

    public DoomArcadeInput(Config config)
    {
        _config = config;
    }

    public void SetHeld(DoomKey key, bool held)
    {
        if (held)
            _held.Add(key);
        else
            _held.Remove(key);
    }

    public void BuildTicCmd(TicCmd cmd)
    {
        var turnLeft = IsHeld(_config.key_turnleft);
        var turnRight = IsHeld(_config.key_turnright);

        cmd.Clear();

        var speed = IsHeld(_config.key_run) ? 1 : 0;
        if (_config.game_alwaysrun)
            speed = 1 - speed;

        _turnHeld = turnLeft || turnRight ? _turnHeld + 1 : 0;
        var turnSpeed = _turnHeld < PlayerBehavior.SlowTurnTics ? 2 : speed;

        var forward = 0;
        var side = 0;

        if (IsHeld(_config.key_strafe))
        {
            if (turnRight)
                side += PlayerBehavior.SideMove[speed];
            if (turnLeft)
                side -= PlayerBehavior.SideMove[speed];
        }
        else
        {
            if (turnRight)
                cmd.AngleTurn -= (short) PlayerBehavior.AngleTurn[turnSpeed];
            if (turnLeft)
                cmd.AngleTurn += (short) PlayerBehavior.AngleTurn[turnSpeed];
        }

        if (IsHeld(_config.key_forward))
            forward += PlayerBehavior.ForwardMove[speed];
        if (IsHeld(_config.key_backward))
            forward -= PlayerBehavior.ForwardMove[speed];
        if (IsHeld(_config.key_strafeleft))
            side -= PlayerBehavior.SideMove[speed];
        if (IsHeld(_config.key_straferight))
            side += PlayerBehavior.SideMove[speed];

        if (IsHeld(_config.key_fire))
            cmd.Buttons |= TicCmdButtons.Attack;
        if (IsHeld(_config.key_use))
            cmd.Buttons |= TicCmdButtons.Use;

        for (var i = 0; i < WeaponKeys; i++)
        {
            if (!_held.Contains(DoomKey.Num1 + i))
                continue;

            cmd.Buttons |= TicCmdButtons.Change;
            cmd.Buttons |= (byte) (i << TicCmdButtons.WeaponShift);
            break;
        }

        cmd.ForwardMove += (sbyte) Math.Clamp(forward, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
        cmd.SideMove += (sbyte) Math.Clamp(side, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
    }

    public void Reset()
    {
    }

    public void GrabMouse()
    {
    }

    public void ReleaseMouse()
    {
    }

    private bool IsHeld(KeyBinding binding)
    {
        foreach (var key in binding.Keys)
        {
            if (_held.Contains(key))
                return true;
        }

        return false;
    }
}
