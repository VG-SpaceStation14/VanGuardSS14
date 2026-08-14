using Content.Client.UserInterface.Controls;
using Content.Client.VendingMachines.UI;
using Content.Shared.VendingMachines;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using System.Linq;
using Content.Shared.VendingMachines.Components;
using Content.Client._VanGuard.VendingMachines.UI; // VG-Tweak: VanGuard vending window
using Content.Shared._VanGuard.VendingMachines; // VG-Tweak: VanGuard vending window

namespace Content.Client.VendingMachines;

public sealed class VendingMachineBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private FancyVendingMachineMenu? _menu; // VG-Tweak: VanGuard vending window

    // VG-Tweak Start: last known balance, applied once the window is ready
    private int _lastBalance = -1;
    // VG-Tweak End

    [ViewVariables]
    private List<VendingMachineInventoryEntry> _cachedInventory = new();

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindowCenteredLeft<FancyVendingMachineMenu>(); // VG-Tweak: VanGuard vending window
        _menu.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        // VG-Tweak Start: the server may push the balance before the window finished opening
        _menu.UpdateBalance(_lastBalance);
        // VG-Tweak End
        _menu.OnItemSelected += OnItemSelected;
        Refresh();
    }

    public void Refresh()
    {
        var enabled = EntMan.TryGetComponent(Owner, out VendingMachineEjectComponent? eject) && !eject.Ejecting;

        var system = EntMan.System<VendingMachineSystem>();
        _cachedInventory = system.GetAllInventory(Owner);

        var allForFree = EntMan.TryGetComponent(Owner, out VendingMachineComponent? vend) && vend.AllForFree;

        _menu?.Populate(_cachedInventory, enabled, allForFree);
    }

    public void UpdateAmounts()
    {
        var enabled = EntMan.TryGetComponent(Owner, out VendingMachineEjectComponent? eject) && !eject.Ejecting;

        var system = EntMan.System<VendingMachineSystem>();
        _cachedInventory = system.GetAllInventory(Owner);
        _menu?.UpdateAmounts(_cachedInventory, enabled);
    }

    // VG-Tweak Start: the compact cards select whole entries instead of list rows
    private void OnItemSelected(VendingMachineInventoryEntry selectedItem)
    {
        SendPredictedMessage(new VendingMachineEjectMessage(selectedItem.Type, selectedItem.ID));
    }
    // VG-Tweak End

    // VG-Tweak Start: server pushes balance updates to the VanGuard vending window
    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is VendingMachineBalanceUpdateMessage balanceUpdate)
        {
            _lastBalance = balanceUpdate.Balance;
            _menu?.UpdateBalance(balanceUpdate.Balance);
        }
    }
    // VG-Tweak End

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        if (_menu == null)
            return;

        _menu.OnItemSelected -= OnItemSelected;
        _menu.OnClose -= Close;
        _menu.Dispose();
    }
}
