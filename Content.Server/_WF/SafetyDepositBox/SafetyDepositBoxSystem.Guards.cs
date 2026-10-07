using System.Linq;
using System.Threading.Tasks;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared.Storage;

namespace Content.Server._WF.SafetyDepositBox;

// Wolfgate's duplication guards, kept out of the upstream file.
public sealed partial class SafetyDepositBoxSystem
{
    /// <summary>
    /// Runs a request for one box, or drops it while another request for that box is still running.
    /// </summary>
    public async void RunBoxRequest(Guid boxId, Func<Task> request)
    {
        if (!_pendingBoxes.Add(boxId))
            return;

        try
        {
            await request();
        }
        finally
        {
            _pendingBoxes.Remove(boxId);
        }
    }

    /// <summary>
    /// Marks a box as withdrawn again if it left the console slot or its contents changed while its deposit was saving.
    /// </summary>
    /// <returns>True if the deposit was undone and the box stays in the world.</returns>
    public async Task<bool> RevertChangedDeposit(
        EntityUid consoleUid,
        SafetyDepositConsoleComponent component,
        EntityUid player,
        EntityUid boxEntity,
        SafetyDepositBoxComponent boxComp,
        StorageComponent storageComp,
        List<EntityUid> savedItems)
    {
        // A deleted box took its contents with it, so the save is the only copy.
        if (Deleted(boxEntity))
            return false;

        if (component.BoxSlot.Item == boxEntity && storageComp.Container.ContainedEntities.SequenceEqual(savedItems))
            return false;

        await _dbManager.ClearSafetyDepositBoxItems(boxComp.BoxId!.Value, _gameTicker.RoundId);
        PlayDenySound(consoleUid, component);
        UpdateUI(consoleUid, component, player);
        return true;
    }
}
