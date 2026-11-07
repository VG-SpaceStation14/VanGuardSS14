using Content.Server._VanGuard.Handover;
using Moq;
using NUnit.Framework;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Tests.Shared;

[TestFixture]
[TestOf(typeof(HandoverSystem))]
public sealed class HandoverSessionTest
{
    private readonly EntityUid _entity = new(1);
    private readonly EntityUid _otherEntity = new(2);

    private static ICommonSession CreateSession(
        EntityUid attached,
        SessionStatus status = SessionStatus.InGame,
        bool hasChannel = true)
    {
        var session = new Mock<ICommonSession>();
        session.SetupGet(s => s.AttachedEntity).Returns(attached);
        session.SetupGet(s => s.Status).Returns(status);
        session.SetupGet(s => s.Channel).Returns(hasChannel ? Mock.Of<INetChannel>() : null!);
        return session.Object;
    }

    [Test]
    public void NullSessionIsRejectedWithoutThrowing()
    {
        Assert.That(HandoverSystem.IsSessionAttachedTo(null, _entity), Is.False);
    }

    [Test]
    public void SessionAttachedToOtherEntityIsRejected()
    {
        var session = CreateSession(_otherEntity);
        Assert.That(HandoverSystem.IsSessionAttachedTo(session, _entity), Is.False);
    }

    [Test]
    public void DisconnectedSessionIsRejected()
    {
        var session = CreateSession(_entity, SessionStatus.Disconnected);
        Assert.That(HandoverSystem.IsSessionAttachedTo(session, _entity), Is.False);
    }

    [Test]
    public void SessionWithoutChannelIsRejected()
    {
        var session = CreateSession(_entity, hasChannel: false);
        Assert.That(HandoverSystem.IsSessionAttachedTo(session, _entity), Is.False);
    }

    [Test]
    public void AttachedActiveSessionIsAccepted()
    {
        var session = CreateSession(_entity);
        Assert.That(HandoverSystem.IsSessionAttachedTo(session, _entity), Is.True);
    }
}
