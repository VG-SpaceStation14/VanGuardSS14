using Content.Client.CartridgeLoader;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Timing; // VG-Tweak

namespace Content.Client.PDA
{
    [UsedImplicitly]
    public sealed class PdaBoundUserInterface : CartridgeLoaderBoundUserInterface
    {
        private readonly PdaSystem _pdaSystem;

        [ViewVariables]
        private PdaMenu? _menu;

        // VG-PDAScreens Start
        private bool _bootFinishedSent;
        private bool _hasReceivedInitialState;
        // VG-PDAScreens End

        public PdaBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            _pdaSystem = EntMan.System<PdaSystem>();
        }

        protected override void Open()
        {
            // VG-PDAScreens Start
            if (_menu != null && !_menu.IsOpen)
            {
                _menu.Dispose();
                _menu = null;
            }
            // VG-PDAScreens End

            base.Open();

            if (_menu == null)
                CreateMenu();
        }

        private void CreateMenu()
        {
            _menu = this.CreateWindowCenteredLeft<PdaMenu>();

            _menu.FlashLightToggleButton.OnToggled += _ =>
            {
                SendMessage(new PdaToggleFlashlightMessage());
            };

            _menu.EjectIdButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaIdSlotId));
            };

            _menu.EjectPenButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaPenSlotId));
            };

            _menu.EjectPaiButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaPaiSlotId));
            };

            _menu.ActivateMusicButton.OnPressed += _ =>
            {
                SendMessage(new PdaShowMusicMessage());
            };

            _menu.AccessRingtoneButton.OnPressed += _ =>
            {
                SendMessage(new PdaShowRingtoneMessage());
            };

            _menu.ShowUplinkButton.OnPressed += _ =>
            {
                SendMessage(new PdaShowUplinkMessage());
            };

            _menu.LockUplinkButton.OnPressed += _ =>
            {
                SendMessage(new PdaLockUplinkMessage());
            };

            _menu.OnWallpaperColorSelected += color =>
            {
                SendMessage(new PdaSetWallpaperColorMessage(color));
            };

            _menu.OnProgramItemPressed += ActivateCartridge;
            _menu.OnInstallButtonPressed += InstallCartridge;
            _menu.OnUninstallButtonPressed += UninstallCartridge;
            _menu.ProgramCloseButton.OnPressed += _ => DeactivateActiveCartridge();

            // VG-Tweak Start
            _menu.PowerOffButton.OnPressed += _ =>
            {
                SendMessage(new PdaPowerOffMessage());
            };
            // VG-Tweak End

            var borderColorComponent = GetBorderColorComponent();
            if (borderColorComponent == null)
                return;

            _menu.BorderColor = borderColorComponent.BorderColor;
            _menu.AccentHColor = borderColorComponent.AccentHColor;
            _menu.AccentVColor = borderColorComponent.AccentVColor;
            _menu.DefaultWallpaperColor = GetDefaultWallpaperColor(borderColorComponent);
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not PdaUpdateState updateState)
                return;

            if (_menu == null)
            {
                return;
            }

            _menu.UpdateState(updateState);

            // VG-PDAScreens Start
            if (!updateState.Powered && _menu.IsOpen && _hasReceivedInitialState)
            {
                _menu.Close();
                _menu = null;
                return;
            }
            _hasReceivedInitialState = true;

            if (!updateState.Booted && !_bootFinishedSent && _menu.IsOpen)
            {
                _menu.ShowBootScreen(true);
                _bootFinishedSent = true;
                Timer.Spawn(2000, () =>
                {
                    if (_menu?.IsOpen == true)
                    {
                        SendMessage(new PdaBootFinishedMessage());
                        _menu.ShowBootScreen(false);
                    }
                });
            }
            else if (updateState.Booted)
            {
                _menu.ShowBootScreen(false);
            }
            // VG-PDAScreens End
        }

        protected override void AttachCartridgeUI(Control cartridgeUIFragment, string? title)
        {
            _menu?.ProgramView.AddChild(cartridgeUIFragment);
            _menu?.ToProgramView(title ?? Loc.GetString("comp-pda-io-program-fallback-title"));
        }

        protected override void DetachCartridgeUI(Control cartridgeUIFragment)
        {
            if (_menu is null)
                return;

            _menu.ToHomeScreen();
            _menu.HideProgramHeader();
            _menu.ProgramView.RemoveChild(cartridgeUIFragment);
        }

        protected override void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs)
        {
            _menu?.UpdateAvailablePrograms(programs);
        }

        private PdaBorderColorComponent? GetBorderColorComponent()
        {
            return EntMan.GetComponentOrNull<PdaBorderColorComponent>(Owner);
        }

        private static Color GetDefaultWallpaperColor(PdaBorderColorComponent borderColor)
        {
            var source = borderColor.AccentVColor
                ?? borderColor.AccentHColor
                ?? borderColor.BorderColor;

            var color = Color.FromHex(source, Color.FromHex("#25252a"));
            return new Color(
                color.R * 0.24f,
                color.G * 0.24f,
                color.B * 0.24f,
                1f);
        }
    }
}
