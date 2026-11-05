using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Shared._VanGuard.Storage;

/// <summary>
///     Resolves a sound source for a storage entity that may be nested in other containers.
/// </summary>
/// <remarks>
///     Entities inside containers are not in PVS, so playing a sound directly from such an entity
///     can lose the sound. The outermost container owner is guaranteed to have a world position.
/// </remarks>
public static class StorageSoundSource
{
    private const int MaxContainerDepth = 64;

    /// <summary>
    ///     Returns the outermost container owner, or the original entity when it is not contained.
    /// </summary>
    /// <param name="entityManager">Entity manager used for validity checks.</param>
    /// <param name="containerSystem">Container system used to walk the container hierarchy.</param>
    /// <param name="storage">The storage entity that wants to emit a sound.</param>
    /// <param name="fallback">A fallback source, usually the entity that performed the interaction.</param>
    public static EntityUid Resolve(
        IEntityManager entityManager,
        SharedContainerSystem containerSystem,
        EntityUid storage,
        EntityUid? fallback = null)
    {
        var source = storage;
        var current = storage;

        for (var depth = 0; depth < MaxContainerDepth; depth++)
        {
            if (!containerSystem.TryGetContainingContainer(current, out var container))
                break;

            if (container.Owner == current)
                break;

            source = container.Owner;
            current = container.Owner;
        }

        if (source == storage)
            return storage;

        if (entityManager.EntityExists(source) && !entityManager.IsQueuedForDeletion(source))
            return source;

        return fallback is { } candidate && entityManager.EntityExists(candidate)
            ? candidate
            : storage;
    }
}
