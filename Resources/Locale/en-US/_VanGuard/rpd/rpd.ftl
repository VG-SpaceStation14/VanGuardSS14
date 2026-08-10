ent-RPD = RPD
    .desc = The rapid pipe dispenser is used to quickly build structures for atmosphere operation and disposal.
ent-RPDEmpty = { ent-RPD }
    .desc = { ent-RPD.desc }
    .suffix = Empty
ent-RPDRecharging = experimental RPD
    .desc = A bluespace-enhanced rapid pipe dispenser that passively generates its own compressed matter.
    .suffix = Auto-Recharge

### Interface

rpd-component-examine-mode-details = Mode selected: '{ $mode }'.
rpd-component-examine-build-details = Build mode selected: { $name }.
### Interaction Messages

# Mode change
rpd-component-change-mode = RPD switched to '{ $mode }' mode.
rpd-component-change-build-mode = RPD switched to build mode. Building { $name }.
# Matter count
rpd-component-no-ammo-message = RPD has run out of charges!
rpd-component-insufficient-ammo-message = Not enough charges!
# Deconstruction
rpd-component-deconstruct-target-not-on-whitelist-message = You cannot deconstruct this!
rpd-component-nothing-to-deconstruct-message = Nothing to deconstruct here!
# Construction
rpd-component-cannot-build-on-empty-tile-message = This cannot be built without a foundation.
rpd-component-must-build-on-subfloor-message = This can only be built on a subfloor!
rpd-component-cannot-build-on-occupied-tile-message = Cannot build here, the location is occupied!

### Category names

rpd-component-DisposalPipe = Disposal pipes
rpd-component-Gaspipes = Gas pipes
rpd-component-Devices = Devices

### Additional info

rpd-component-deconstruct = Deconstruct

### Radial menu labels

rpd-component-FireAlarm = { ent-FireAlarm }
rpd-component-GasPipeBend = { ent-GasPipeBend }
rpd-component-GasPipeStraight = { ent-GasPipeStraight }
rpd-component-GasPipeHalf = { ent-GasPipeHalf }
rpd-component-GasPipeFourway = { ent-GasPipeFourway }
rpd-component-GasPipeTJunction = { ent-GasPipeTJunction }
rpd-component-GasPressurePump = { ent-GasPressurePump }
rpd-component-GasMixer = { ent-GasMixer }
rpd-component-GasMixerFlipped = { ent-GasMixerFlipped }
rpd-component-GasFilter = { ent-GasFilter }
rpd-component-GasFilterFlipped = { ent-GasFilterFlipped }
rpd-component-GasVolumePump = { ent-GasVolumePump }
rpd-component-GasPassiveVent = { ent-GasPassiveVent }
rpd-component-GasOutletInjector = { ent-GasOutletInjector }
rpd-component-GasVentPump = { ent-GasVentPump }
rpd-component-GasValve = { ent-GasValve }
rpd-component-GasVentScrubber = { ent-GasVentScrubber }
rpd-component-GasPassiveGate = { ent-GasPassiveGate }
rpd-component-GasDualPortVentPump = { ent-GasDualPortVentPump }
rpd-component-PressureControlledValve = { ent-PressureControlledValve }
rpd-component-DisposalUnit = { ent-DisposalUnit }
rpd-component-MailingUnit = { ent-MailingUnit }
rpd-component-GasPort = { ent-GasPort }
rpd-component-DisposalJunctionFlipped = { ent-DisposalJunctionFlipped }
rpd-component-DisposalJunction = { ent-DisposalJunction }
rpd-component-DisposalRouterFlipped = { ent-DisposalRouterFlipped }
rpd-component-DisposalRouter = { ent-DisposalRouter }
rpd-component-DisposalTagger = { ent-DisposalTagger }
rpd-component-DisposalBend = { ent-DisposalBend }
rpd-component-DisposalYJunction = { ent-DisposalYJunction }
rpd-component-DisposalSignalRouter = { ent-DisposalSignalRouter }
rpd-component-DisposalSignalRouterFlipped = { ent-DisposalSignalRouterFlipped }
rpd-component-DisposalTrunk = { ent-DisposalTrunk }
rpd-component-DisposalPipes = { ent-DisposalPipe }
rpd-component-AirSensor = { ent-AirSensor }
rpd-component-FloorDrain = { ent-FloorDrain }
rpd-component-AirAlarm = { ent-AirAlarm }
rpd-component-SignalControlledValve = { ent-SignalControlledValve }
rpd-component-Radiator = { ent-HeatExchanger }
