using System.Text.Json.Serialization;

namespace OcdWizard.ControlStation.Models;

/// <summary>
/// How the watch's microgames are tuned for one participant.
///
/// Mirrors <c>convention/models/MicrogameSettings.kt</c> on the Android side, field for field.
/// It travels on <see cref="ExperimentDto"/>, so a condition's difficulty is part of the
/// schedule rather than something a researcher has to set by hand on each wrist: loading a
/// schedule on the phone applies these to the watch in the same action that loads its events.
///
/// Every property has a default so a schedule written before this field existed still
/// round-trips through the editor and lands on the documented defaults.
/// </summary>
public record MicrogameSettingsDto(
    /// <summary>Simon Says show-phase speed, 1..5. Higher is faster; the input window never shrinks.</summary>
    [property: JsonPropertyName("simonDifficulty")] int SimonDifficulty = 2,

    /// <summary>How many Simon Says rounds must be cleared, 2..8.</summary>
    [property: JsonPropertyName("simonRounds")] int SimonRounds = 4,

    /// <summary>Lock picking stand-still sensitivity and corridor length, 1..5. Higher is harder.</summary>
    [property: JsonPropertyName("lockDifficulty")] int LockDifficulty = 2,

    /// <summary>What a lock-picking mistake costs: STUN, PUSHBACK or RESET.</summary>
    [property: JsonPropertyName("lockPenalty")] string LockPenalty = "STUN"
)
{
    public const int MinDifficulty = 1;
    public const int MaxDifficulty = 5;
    public const int MinRounds = 2;
    public const int MaxRounds = 8;

    /// <summary>Penalty values the watch understands, in ascending severity.</summary>
    public static readonly string[] PenaltyOptions = { "STUN", "PUSHBACK", "RESET" };

    /// <summary>
    /// The same values with every field forced into range.
    ///
    /// Applied on the way in from a hand-edited file or an older phone build, so a bad number
    /// degrades to a playable game rather than, say, a Simon sequence that cannot be finished.
    /// </summary>
    public MicrogameSettingsDto Sanitized() => this with
    {
        SimonDifficulty = System.Math.Clamp(SimonDifficulty, MinDifficulty, MaxDifficulty),
        SimonRounds = System.Math.Clamp(SimonRounds, MinRounds, MaxRounds),
        LockDifficulty = System.Math.Clamp(LockDifficulty, MinDifficulty, MaxDifficulty),
        LockPenalty = System.Array.Exists(PenaltyOptions, p => p == LockPenalty) ? LockPenalty : "STUN",
    };

    /// <summary>One-line summary for a log or status row.</summary>
    public string Summary() =>
        $"simon_difficulty={SimonDifficulty}, simon_rounds={SimonRounds}, " +
        $"lock_difficulty={LockDifficulty}, lock_penalty={LockPenalty}";
}
