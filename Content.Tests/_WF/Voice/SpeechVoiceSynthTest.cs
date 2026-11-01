using System;
using System.Linq;
using Content.Client._WF.Voice;
using Content.Shared._WF.Voice;
using NUnit.Framework;

namespace Content.Tests._WF.Voice;

/// <summary>Rendering into reused buffers gives the same line as fresh ones, and the longest line fits them.</summary>
[TestFixture]
public sealed class SpeechVoiceSynthTest
{
    private const string Line = "the quick brown fox jumps over the lazy dog, and then? it naps!";

    [Test]
    public void DirtyBuffersRenderTheSameLine([Values] SpeechVoiceFilter filter, [Values] bool radio)
    {
        var voice = new SpeechVoice { Filter = filter };

        var fresh = new SpeechVoiceBuffers();
        var length = new SpeechVoiceSynth().Render(Line, voice, radio, fresh);

        var dirty = new SpeechVoiceBuffers();
        Array.Fill(dirty.Samples, 1f);
        Array.Fill(dirty.Dry, 1f);
        Array.Fill(dirty.Pcm, short.MaxValue);
        var dirtyLength = new SpeechVoiceSynth().Render(Line, voice, radio, dirty);

        Assert.That(length, Is.GreaterThan(0));
        Assert.That(dirtyLength, Is.EqualTo(length));
        Assert.That(dirty.Pcm.Take(length), Is.EqualTo(fresh.Pcm.Take(length)));
    }

    [Test]
    public void LongestLineFitsTheBuffers([Values] SpeechVoiceFilter filter)
    {
        var voice = new SpeechVoice
        {
            Filter = filter,
            Speed = SpeechVoice.MaxSpeed,
            Pitch = SpeechVoice.MinPitch,
        };
        var text = string.Concat(Enumerable.Repeat("long words. ", 40));

        var length = new SpeechVoiceSynth().Render(text, voice, true, new SpeechVoiceBuffers());

        Assert.That(length, Is.InRange(1, SpeechVoiceSynth.MaxSamples));
    }

    [Test]
    public void NoLettersRenderNothing()
    {
        Assert.That(new SpeechVoiceSynth().Render("... !?", new SpeechVoice(), false, new SpeechVoiceBuffers()),
            Is.Zero);
    }
}
