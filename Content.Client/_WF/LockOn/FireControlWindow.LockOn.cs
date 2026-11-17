namespace Content.Client._Mono.FireControl.UI;

public sealed partial class FireControlWindow
{
    /// <summary>Whether a weapon is picked, which keeps left click on firing instead of locking.</summary>
    public bool WfLockOnHasSelection()
    {
        foreach (var (uid, button) in WeaponsList)
        {
            if (button.Pressed && WfAvailableWeapon(uid))
                return true;
        }
        return false;
    }

    /// <summary>Selected guns, or every available gun when none are picked, for the lead indicator.</summary>
    public IReadOnlyList<NetEntity> WfLockOnWeapons()
    {
        var selected = new List<NetEntity>();
        foreach (var (uid, button) in WeaponsList)
        {
            if (button.Pressed && WfAvailableWeapon(uid))
                selected.Add(uid);
        }

        if (selected.Count > 0)
            return selected;

        foreach (var uid in WeaponsList.Keys)
        {
            if (WfAvailableWeapon(uid))
                selected.Add(uid);
        }
        return selected;
    }
}
