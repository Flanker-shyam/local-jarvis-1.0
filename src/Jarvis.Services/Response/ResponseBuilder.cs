using Jarvis.Core.Interfaces;
using Jarvis.Core.Models;

namespace Jarvis.Services.Response;

/// <summary>
/// Builds conversational response strings from command results.
/// For successful results: generates confirmation with track/artist details when available.
/// For failed results: generates helpful error messages with recovery suggestions.
/// Always returns a non-empty string.
/// </summary>
public class ResponseBuilder : IResponseBuilder
{
    public string BuildResponse(CommandResult result)
    {
        if (result is null)
            return "Something went wrong. Please try again.";

        if (result.Success)
            return BuildSuccessResponse(result);

        return BuildFailureResponse(result);
    }

    private static string BuildSuccessResponse(CommandResult result)
    {
        if (result.Data is PlaybackResult playback && playback.NowPlaying is not null)
        {
            var track = playback.NowPlaying;
            return $"Now playing {track.Name} by {track.Artist}.";
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
            return result.Message;

        return "Done.";
    }

    private static string BuildFailureResponse(CommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message))
            return $"{result.Message} Try rephrasing your request or check your Spotify connection.";

        return "Something went wrong. Try rephrasing your request or check your Spotify connection.";
    }
}
