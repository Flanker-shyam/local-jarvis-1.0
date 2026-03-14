using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Enums;
using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;
using Jarvis.Services.Nlu;
using Moq;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 3: PLAY_MUSIC intent always has populated search params.
/// For any IntentResult with IntentType.PlayMusic, SearchParams is non-null
/// with at least one populated criterion (Query, Artist, Track, Genres, or Mood).
///
/// **Validates: Requirements 2.5**
/// </summary>
public class Property3_PlayMusicSearchParamsTests
{
    /// <summary>
    /// Represents a generated search params combination that always has
    /// at least one populated criterion.
    /// </summary>
    public class PopulatedSearchParamsData
    {
        public string? Query { get; set; }
        public string? Artist { get; set; }
        public string? Track { get; set; }
        public List<string>? Genres { get; set; }
        public string? Mood { get; set; }

        public override string ToString() =>
            $"Query={Query}, Artist={Artist}, Track={Track}, " +
            $"Genres=[{string.Join(",", Genres ?? new List<string>())}], Mood={Mood}";
    }

    /// <summary>
    /// FsCheck generator that produces search param combinations where at least
    /// one of Query, Artist, Track, Genres (non-empty), or Mood is populated.
    /// </summary>
    public static Arbitrary<PopulatedSearchParamsData> ArbPopulatedSearchParams()
    {
        var genNonEmptyString = Arb.Default.NonEmptyString().Generator.Select(s => s.Get);
        var genOptionalString = Gen.OneOf(Gen.Constant<string?>(null), genNonEmptyString.Select<string, string?>(s => s));
        var genGenreList = genNonEmptyString.ListOf().Select(g => (List<string>?)g.ToList());
        var genOptionalGenres = Gen.OneOf(
            Gen.Constant<List<string>?>(null),
            Gen.Constant<List<string>?>(new List<string>()),
            genGenreList
        );

        var gen = from query in genOptionalString
                  from artist in genOptionalString
                  from track in genOptionalString
                  from genres in genOptionalGenres
                  from mood in genOptionalString
                  let data = new PopulatedSearchParamsData
                  {
                      Query = query,
                      Artist = artist,
                      Track = track,
                      Genres = genres,
                      Mood = mood
                  }
                  // Filter: at least one criterion must be populated
                  where !string.IsNullOrWhiteSpace(data.Query)
                      || !string.IsNullOrWhiteSpace(data.Artist)
                      || !string.IsNullOrWhiteSpace(data.Track)
                      || (data.Genres != null && data.Genres.Count > 0)
                      || !string.IsNullOrWhiteSpace(data.Mood)
                  select data;

        return Arb.From(gen, Arb.Default.Derive<PopulatedSearchParamsData>().Shrinker);
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(Property3_PlayMusicSearchParamsTests) })]
    public bool PlayMusic_Intent_Always_Has_NonNull_SearchParams_With_AtLeastOneCriterion(
        PopulatedSearchParamsData searchData,
        NonEmptyString nonEmptyTranscript)
    {
        var transcript = nonEmptyTranscript.Get;
        if (string.IsNullOrWhiteSpace(transcript))
            return true; // vacuously true

        var confidence = 0.85f;
        var llmJson = BuildPlayMusicLlmResponse(searchData, confidence);

        var mockLlmClient = new Mock<ILlmClient>();
        mockLlmClient
            .Setup(c => c.ChatAsync(It.IsAny<string>(), It.IsAny<List<ChatMessage>>()))
            .ReturnsAsync(llmJson);

        var mockConversationStore = new Mock<IConversationStore>();
        mockConversationStore
            .Setup(s => s.GetRecentTurns(It.IsAny<int>()))
            .Returns(new List<Turn>());
        mockConversationStore
            .Setup(s => s.GetLastPlayedTrack())
            .Returns((Track?)null);

        var resolver = new NluResolver(mockLlmClient.Object, mockConversationStore.Object);
        var result = resolver.ResolveIntentAsync(transcript, new List<Turn>()).GetAwaiter().GetResult();

        // Only check the property when the result is actually PlayMusic
        if (result.IntentType != IntentType.PlayMusic)
            return true; // not applicable for non-PlayMusic results

        // Property: SearchParams must be non-null
        if (result.SearchParams == null)
            return false;

        // Property: At least one criterion must be populated
        var hasQuery = !string.IsNullOrWhiteSpace(result.SearchParams.Query);
        var hasArtist = !string.IsNullOrWhiteSpace(result.SearchParams.Artist);
        var hasTrack = !string.IsNullOrWhiteSpace(result.SearchParams.Track);
        var hasGenres = result.SearchParams.Genres is { Count: > 0 };
        var hasMood = !string.IsNullOrWhiteSpace(result.SearchParams.Mood);

        return hasQuery || hasArtist || hasTrack || hasGenres || hasMood;
    }

    private static string BuildPlayMusicLlmResponse(PopulatedSearchParamsData searchData, float confidence)
    {
        var searchParamsDict = new Dictionary<string, object>();

        if (searchData.Query != null)
            searchParamsDict["query"] = searchData.Query;
        if (searchData.Artist != null)
            searchParamsDict["artist"] = searchData.Artist;
        if (searchData.Track != null)
            searchParamsDict["track"] = searchData.Track;
        if (searchData.Genres != null && searchData.Genres.Count > 0)
            searchParamsDict["genres"] = searchData.Genres;
        if (searchData.Mood != null)
            searchParamsDict["mood"] = searchData.Mood;

        searchParamsDict["isVague"] = string.IsNullOrWhiteSpace(searchData.Artist)
            && string.IsNullOrWhiteSpace(searchData.Track);

        var responseObj = new Dictionary<string, object>
        {
            ["intent"] = "PLAY_MUSIC",
            ["confidence"] = confidence,
            ["searchParams"] = searchParamsDict
        };

        return JsonSerializer.Serialize(responseObj);
    }
}
