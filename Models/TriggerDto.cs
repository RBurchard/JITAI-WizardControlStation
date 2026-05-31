using System.Text.Json.Serialization;

namespace OcdWizard.ControlStation.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RapidMovementKindDto), typeDiscriminator: "RapidMovement")]
[JsonDerivedType(typeof(RepeatingMovementKindDto), typeDiscriminator: "RepeatingMovement")]
[JsonDerivedType(typeof(HeartRateKindDto), typeDiscriminator: "HeartRate")]
[JsonDerivedType(typeof(ManualKindDto), typeDiscriminator: "Manual")]
public abstract record TriggerKindDto;

public record RapidMovementKindDto(
    [property: JsonPropertyName("accelThreshold")] float AccelThreshold = 2.5f,
    [property: JsonPropertyName("windowMs")] int WindowMs = 500
) : TriggerKindDto;

public record RepeatingMovementKindDto(
    [property: JsonPropertyName("minHz")] float MinHz = 1.0f,
    [property: JsonPropertyName("maxHz")] float MaxHz = 4.0f,
    [property: JsonPropertyName("sustainSec")] float SustainSec = 3.0f
) : TriggerKindDto;

public record HeartRateKindDto(
    [property: JsonPropertyName("bpm")] int Bpm = 110,
    [property: JsonPropertyName("above")] bool Above = true,
    [property: JsonPropertyName("sustainSec")] float SustainSec = 3.0f
) : TriggerKindDto;

public record ManualKindDto() : TriggerKindDto;

public record TriggerDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] TriggerKindDto Kind,
    [property: JsonPropertyName("enabled")] bool Enabled = true
);

public record TriggerEventDto(
    [property: JsonPropertyName("triggerId")] string TriggerId,
    [property: JsonPropertyName("ts")] long Ts,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("details")] string Details = ""
);

public record TriggerFireRequestDto(
    [property: JsonPropertyName("triggerId")] string TriggerId,
    [property: JsonPropertyName("source")] string Source = "manual-pc"
);
