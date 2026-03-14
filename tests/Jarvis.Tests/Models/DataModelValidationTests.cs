using Jarvis.Core.Enums;
using Jarvis.Core.Models;

namespace Jarvis.Tests.Models;

public class IntentResultValidationTests
{
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void Constructor_ValidConfidence_Succeeds(float confidence)
    {
        var result = new IntentResult(IntentType.PlayMusic, confidence, null, "test");
        Assert.Equal(confidence, result.Confidence);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(-1.0f)]
    [InlineData(2.0f)]
    public void Constructor_InvalidConfidence_ThrowsArgumentOutOfRange(float confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IntentResult(IntentType.PlayMusic, confidence, null, "test"));
    }

    [Fact]
    public void Constructor_NullRawTranscript_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new IntentResult(IntentType.PlayMusic, 0.5f, null, null!));
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var searchParams = new SearchParams { Artist = "Queen" };
        var result = new IntentResult(IntentType.PlayMusic, 0.9f, searchParams, "play queen");

        Assert.Equal(IntentType.PlayMusic, result.IntentType);
        Assert.Equal(0.9f, result.Confidence);
        Assert.Same(searchParams, result.SearchParams);
        Assert.Equal("play queen", result.RawTranscript);
    }
}

public class SearchParamsValidationTests
{
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void Energy_ValidValue_Succeeds(float energy)
    {
        var sp = new SearchParams { Energy = energy };
        Assert.Equal(energy, sp.Energy);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(-1.0f)]
    [InlineData(2.0f)]
    public void Energy_InvalidValue_ThrowsArgumentOutOfRange(float energy)
    {
        var sp = new SearchParams();
        Assert.Throws<ArgumentOutOfRangeException>(() => sp.Energy = energy);
    }

    [Fact]
    public void Energy_Null_Succeeds()
    {
        var sp = new SearchParams { Energy = null };
        Assert.Null(sp.Energy);
    }

    [Fact]
    public void ValidateForPlayMusic_WithQuery_Succeeds()
    {
        var sp = new SearchParams { Query = "bohemian rhapsody" };
        sp.ValidateForPlayMusic(); // should not throw
    }

    [Fact]
    public void ValidateForPlayMusic_WithArtist_Succeeds()
    {
        var sp = new SearchParams { Artist = "Queen" };
        sp.ValidateForPlayMusic();
    }

    [Fact]
    public void ValidateForPlayMusic_WithTrack_Succeeds()
    {
        var sp = new SearchParams { Track = "Bohemian Rhapsody" };
        sp.ValidateForPlayMusic();
    }

    [Fact]
    public void ValidateForPlayMusic_WithGenres_Succeeds()
    {
        var sp = new SearchParams { Genres = new List<string> { "rock" } };
        sp.ValidateForPlayMusic();
    }

    [Fact]
    public void ValidateForPlayMusic_WithMood_Succeeds()
    {
        var sp = new SearchParams { Mood = "chill" };
        sp.ValidateForPlayMusic();
    }

    [Fact]
    public void ValidateForPlayMusic_NoCriteria_ThrowsArgumentException()
    {
        var sp = new SearchParams();
        Assert.Throws<ArgumentException>(() => sp.ValidateForPlayMusic());
    }

    [Fact]
    public void ValidateForPlayMusic_OnlyEmptyStrings_ThrowsArgumentException()
    {
        var sp = new SearchParams { Query = "", Artist = "  ", Track = "" };
        Assert.Throws<ArgumentException>(() => sp.ValidateForPlayMusic());
    }

    [Fact]
    public void ValidateForPlayMusic_EmptyGenresList_ThrowsArgumentException()
    {
        var sp = new SearchParams { Genres = new List<string>() };
        Assert.Throws<ArgumentException>(() => sp.ValidateForPlayMusic());
    }
}

public class TranscriptResultValidationTests
{
    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.5f)]
    [InlineData(1.0f)]
    public void Confidence_ValidValue_Succeeds(float confidence)
    {
        var tr = new TranscriptResult { Confidence = confidence };
        Assert.Equal(confidence, tr.Confidence);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(-1.0f)]
    [InlineData(2.0f)]
    public void Confidence_InvalidValue_ThrowsArgumentOutOfRange(float confidence)
    {
        var tr = new TranscriptResult();
        Assert.Throws<ArgumentOutOfRangeException>(() => tr.Confidence = confidence);
    }

    [Fact]
    public void Text_DefaultsToEmpty()
    {
        var tr = new TranscriptResult();
        Assert.Equal(string.Empty, tr.Text);
    }
}
