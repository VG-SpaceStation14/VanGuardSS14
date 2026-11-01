using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._VanGuard.Effects;

/// <summary>
/// Spawns short-lived spark particles at a location. Only the server creates the entities; clients
/// receive them through normal entity PVS.
/// </summary>
public sealed partial class SparksSystem : EntitySystem
{
    private const string SparkEntity = "EffectSpark";

    [Dependency] private INetManager _network = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transforms = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private static readonly SoundSpecifier SparkSound = new SoundCollectionSpecifier("sparks");

    public void DoSparks(
        EntityCoordinates origin,
        int minimum = 1,
        int maximum = 3,
        float minSpeed = 1f,
        float maxSpeed = 4f,
        bool withSound = true)
    {
        if (_network.IsClient)
            return;

        var count = _random.Next(minimum, maximum + 1);
        if (count < 1)
            return;

        if (withSound)
            _audio.PlayPvs(SparkSound, origin);

        var mapOrigin = _transforms.ToMapCoordinates(origin);
        var fixedSpeed = minSpeed >= maxSpeed ? minSpeed : (float?) null;

        for (var i = 0; i < count; i++)
        {
            var spark = Spawn(SparkEntity, mapOrigin);
            var direction = _random.NextVector2();
            if (direction == Vector2.Zero)
                direction = Vector2.UnitX;

            var speed = fixedSpeed ?? _random.NextFloat(minSpeed, maxSpeed);
            _physics.SetLinearVelocity(spark, direction.Normalized() * speed);
        }
    }
}
