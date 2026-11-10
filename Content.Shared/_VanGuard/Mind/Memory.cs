using Robust.Shared.Serialization;

namespace Content.Shared._VanGuard.Mind;

/// <summary>
///     A short piece of character knowledge shown in the U-menu memory section.
/// </summary>
[Serializable, NetSerializable]
public sealed class Memory(string name, string value)
{
    public string Name { get; set; } = name;
    public string Value { get; set; } = value;
}
