using FsCheck;
using FsCheck.Xunit;
using Jarvis.Services.Spotify;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 10: Mood-to-valence mapping is bounded.
/// For any mood string (including null and unrecognized),
/// MoodToValence returns a float in [0.0, 1.0],
/// with null/unknown returning 0.5.
///
/// **Validates: Requirements 6.1, 6.2**
/// </summary>
public class Property10_MoodValenceTests
{
    [Property(MaxTest = 300)]
    public bool MoodToValence_ForAnyString_ReturnsBoundedFloat(string? mood)
    {
        var valence = SpotifyService.MoodToValence(mood);
        return valence >= 0.0f && valence <= 1.0f;
    }

    [Property(MaxTest = 300)]
    public bool MoodToValence_NullOrUnknown_ReturnsHalf(string? mood)
    {
        var knownMoods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "happy", "upbeat", "energetic", "party", "chill",
            "relaxed", "mellow", "melancholy", "sad", "angry",
            "dark", "romantic", "nostalgic", "focused", "epic"
        };

        var isKnown = mood != null
            && !string.IsNullOrWhiteSpace(mood)
            && knownMoods.Contains(mood.Trim());

        if (isKnown) return true; // skip known moods for this property

        var valence = SpotifyService.MoodToValence(mood);
        return valence == 0.5f;
    }

    [Fact]
    public void MoodToValence_Null_ReturnsHalf()
    {
        Assert.Equal(0.5f, SpotifyService.MoodToValence(null));
    }

    [Fact]
    public void MoodToValence_CaseInsensitive()
    {
        Assert.Equal(0.9f, SpotifyService.MoodToValence("HAPPY"));
        Assert.Equal(0.9f, SpotifyService.MoodToValence("Happy"));
        Assert.Equal(0.15f, SpotifyService.MoodToValence("SAD"));
    }
}
