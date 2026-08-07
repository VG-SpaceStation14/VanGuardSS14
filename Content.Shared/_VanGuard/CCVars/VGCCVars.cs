using Robust.Shared.Configuration;

namespace Content.Shared._VanGuard.CCVars;

[CVarDefs]
public sealed class VGCCVars
{
    public static readonly CVarDef<bool> DetailedExamine =
        CVarDef.Create("vg.detailed_examine", true, CVar.CLIENT | CVar.ARCHIVE);
}