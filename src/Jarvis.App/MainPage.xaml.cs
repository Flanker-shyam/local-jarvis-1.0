namespace Jarvis.App;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnMicButtonClicked(object sender, EventArgs e)
    {
        StatusLabel.Text = "Listening...";
        // Voice command pipeline will be wired in later tasks
        await Task.Delay(500);
        StatusLabel.Text = "Ready";
    }
}
