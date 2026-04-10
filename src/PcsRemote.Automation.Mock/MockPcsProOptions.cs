namespace PcsRemote.Automation.Mock;

public class MockPcsProOptions
{
    // Per-transition delays — default TimeSpan.Zero (zero-configurable per M-SC-8)
    public TimeSpan LaunchDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan LoginDetectedDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan CredentialsEnteredDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan SearchTriggeredDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan SpinnerGoneDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan MatchOpenedDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan ChangeMatchDelay { get; set; } = TimeSpan.Zero;
    public TimeSpan StopDelay { get; set; } = TimeSpan.Zero;

    // Behavioural knobs
    public double ErrorProbability { get; set; } = 0.0;
    public int? RngSeed { get; set; } = null;
    public double ImageVariationProbability { get; set; } = 0.2;
    public int FakeMatchCount { get; set; } = 3;
}
