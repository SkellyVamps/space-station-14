using System;
using Content.Server.Voice;
using Content.Shared.Voice;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;

namespace Content.Tests.Shared.Voice;

[TestFixture]
public sealed class VoiceSecurityTest
{
    [Test]
    public void FramingRejectsWrongDurationStereoAndTruncation()
    {
        Assert.That(VoiceOpusPacket.IsValid(new byte[] { 0x98, 1 }), Is.True);
        Assert.That(VoiceOpusPacket.IsValid(new byte[] { 0x99, 1, 2 }), Is.False);
        Assert.That(VoiceOpusPacket.IsValid(new byte[] { 0x9c, 1 }), Is.False);
        Assert.That(VoiceOpusPacket.IsValid(new byte[] { 0x90, 1 }), Is.False);
        Assert.That(VoiceOpusPacket.IsValid(new byte[] { 0x93, 0x42, 255 }), Is.False);
        Assert.That(VoiceOpusPacket.IsValid(new byte[] { 0x92, 252 }), Is.False);
        var random = new Random(5716);
        for (var i = 0; i < 20000; i++)
        {
            var packet = new byte[random.Next(514)];
            random.NextBytes(packet);
            Assert.DoesNotThrow(() => VoiceOpusPacket.IsValid(packet));
        }
    }
    [Test]
    public void StreamIdentityIsScopedAndRotatesWithProfile()
    {
        var registry = new VoiceStreamRegistry();
        var listener = new NetUserId(Guid.NewGuid());
        var other = new NetUserId(Guid.NewGuid());
        var speaker = new NetUserId(Guid.NewGuid());
        var entity = new EntityUid(42);
        var first = registry.Get(listener, speaker, entity, 1);
        Assert.That(first, Is.Not.EqualTo(speaker));
        Assert.That(registry.Get(listener, speaker, entity, 1), Is.EqualTo(first));
        Assert.That(registry.Get(other, speaker, entity, 1), Is.Not.EqualTo(first));
        Assert.That(registry.Get(listener, speaker, entity, 2), Is.Not.EqualTo(first));
        var current = registry.Get(listener, speaker, entity, 2);
        registry.Retain(new() { speaker });
        Assert.That(registry.Get(listener, speaker, entity, 2), Is.Not.EqualTo(current));
    }
}
