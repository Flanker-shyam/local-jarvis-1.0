using Android.App;
using Android.Content;
using Android.Content.PM;

namespace Jarvis.App.Platforms.Android;

/// <summary>
/// Handles the jarvis://callback redirect from Spotify OAuth.
/// When Spotify finishes login in the browser, it redirects to jarvis://callback?code=...
/// Android intercepts this via the intent filter and routes it to this activity,
/// which forwards the URI to the main app.
/// </summary>
[Activity(
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "jarvis",
    DataHost = "callback")]
public class SpotifyCallbackActivity : Activity
{
    protected override void OnResume()
    {
        base.OnResume();

        // Forward the callback URI to the MAUI app
        var uri = Intent?.Data?.ToString();
        if (!string.IsNullOrEmpty(uri))
        {
            // Store the callback URI so AuthManager can pick it up
            Preferences.Default.Set("spotify_callback_uri", uri);
        }

        // Return to the main activity
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        StartActivity(intent);
        Finish();
    }
}
