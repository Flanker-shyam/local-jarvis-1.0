namespace Jarvis.Core.Models;

public class SearchParams
{
    public string? Query { get; set; }
    public string? Artist { get; set; }
    public string? Track { get; set; }
    public string? Album { get; set; }
    public List<string> Genres { get; set; } = new();
    public string? Mood { get; set; }
    public string? Era { get; set; }
    public string? Context { get; set; }

    private float? _energy;
    public float? Energy
    {
        get => _energy;
        set
        {
            if (value.HasValue && (value.Value < 0.0f || value.Value > 1.0f))
                throw new ArgumentOutOfRangeException(nameof(Energy), value, "Energy must be between 0.0 and 1.0.");
            _energy = value;
        }
    }

    public bool IsVague { get; set; }
    public string? SeedTrackId { get; set; }

    /// <summary>
    /// Validates that at least one search criterion is populated.
    /// Should be called when IntentType is PlayMusic.
    /// </summary>
    public void ValidateForPlayMusic()
    {
        bool hasCriterion =
            !string.IsNullOrWhiteSpace(Query) ||
            !string.IsNullOrWhiteSpace(Artist) ||
            !string.IsNullOrWhiteSpace(Track) ||
            (Genres is { Count: > 0 }) ||
            !string.IsNullOrWhiteSpace(Mood);

        if (!hasCriterion)
            throw new ArgumentException("SearchParams must have at least one populated criterion (Query, Artist, Track, Genres, or Mood) when IntentType is PlayMusic.");
    }
}
