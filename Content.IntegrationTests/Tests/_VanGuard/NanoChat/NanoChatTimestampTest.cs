#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._VanGuard.NanoChat;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._VanGuard.NanoChat;

/// <summary>
///     Verifies that NanoChat message timestamps survive the real net serializer
///     (the exact one used to push <see cref="NanoChatUiState"/> to the client)
///     and that the server's game clock is non-zero when messages are sent.
/// </summary>
public sealed class NanoChatTimestampTest : InteractionTest
{
    [Test]
    public void Timestamp_SurvivesGameNetSerialization()
    {
        var serializer = Server.ResolveDependency<IRobustSerializer>();

        var message = new NanoChatMessage(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(7), "hello", 42);
        var state = new NanoChatUiState(
            recipients: new Dictionary<uint, NanoChatRecipient> { [42] = new(42, "Alice", "Cargo Technician") },
            messages: new Dictionary<uint, List<NanoChatMessage>> { [42] = [message] },
            contacts: null,
            currentChat: 42,
            ownNumber: 42,
            maxRecipients: 50,
            notificationsMuted: false,
            listNumber: true);

        using var stream = new MemoryStream();
        serializer.SerializeDirect(stream, state);
        stream.Position = 0;

        NanoChatUiState read;
        serializer.DeserializeDirect(stream, out read);

        Assert.That(read.Messages[42][0].Timestamp, Is.EqualTo(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(7)),
            "NanoChat message timestamp must survive net serialization (was zeroed, breaking the time display).");
    }

    [Test]
    public async Task GameClock_AdvancesPastZero()
    {
        await RunTicks(10);
        var timing = Server.ResolveDependency<IGameTiming>();
        Assert.That(timing.CurTime, Is.GreaterThan(TimeSpan.Zero),
            "Game clock must be non-zero while a round is running so sent messages get real timestamps.");
    }
}
