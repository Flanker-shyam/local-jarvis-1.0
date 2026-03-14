using FsCheck;
using FsCheck.Xunit;
using Jarvis.Core.Models;
using Jarvis.Services.Response;

namespace Jarvis.Tests.Properties;

/// <summary>
/// Property 12: Response builder produces non-empty output for all results.
/// For any CommandResult (success or failure), BuildResponse returns a non-empty string.
/// **Validates: Requirements 8.1, 8.2**
/// </summary>
public class Property12_ResponseBuilderTests
{
    private static readonly ResponseBuilder Builder = new();

    [Property(Arbitrary = new[] { typeof(CommandResultArbitrary) })]
    public Property BuildResponse_AlwaysReturnsNonEmptyString(CommandResult result)
    {
        var response = Builder.BuildResponse(result);
        return (!string.IsNullOrEmpty(response)).ToProperty();
    }

    [Property]
    public Property BuildResponse_SuccessWithPlaybackResult_ReturnsNonEmpty(
        NonEmptyString trackName, NonEmptyString artistName)
    {
        var result = new CommandResult
        {
            Success = true,
            Message = "Playing",
            Data = new PlaybackResult
            {
                Success = true,
                Message = "Playing",
                NowPlaying = new Track
                {
                    Id = "1",
                    Name = trackName.Get,
                    Artist = artistName.Get,
                    Uri = "spotify:track:1"
                },
                QueueLength = 1
            }
        };

        var response = Builder.BuildResponse(result);
        return (!string.IsNullOrEmpty(response)
            && response.Contains(trackName.Get)
            && response.Contains(artistName.Get)).ToProperty();
    }

    [Property]
    public Property BuildResponse_FailureResult_ReturnsNonEmpty(bool hasMessage)
    {
        var result = new CommandResult
        {
            Success = false,
            Message = hasMessage ? "Some error occurred." : "",
            Data = null
        };

        var response = Builder.BuildResponse(result);
        return (!string.IsNullOrEmpty(response)).ToProperty();
    }

    /// <summary>
    /// Custom Arbitrary for generating CommandResult instances.
    /// </summary>
    public static class CommandResultArbitrary
    {
        public static Arbitrary<CommandResult> CommandResult()
        {
            var gen = from success in Arb.Generate<bool>()
                      from message in Gen.Elements("Playing track.", "Paused.", "No results found.", "Error occurred.", "", "Skipped to next track.")
                      from hasPlayback in Arb.Generate<bool>()
                      select new CommandResult
                      {
                          Success = success,
                          Message = message,
                          Data = hasPlayback
                              ? new PlaybackResult
                              {
                                  Success = success,
                                  Message = message,
                                  NowPlaying = success ? new Track { Id = "1", Name = "Test Track", Artist = "Test Artist", Uri = "spotify:track:1" } : null,
                                  QueueLength = success ? 1 : 0
                              }
                              : null
                      };

            return Arb.From(gen);
        }
    }
}
