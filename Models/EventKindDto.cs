using System.Text.Json.Serialization;

namespace OcdWizard.ControlStation.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TimedKindDto), typeDiscriminator: "Timed")]
[JsonDerivedType(typeof(RandomWaitKindDto), typeDiscriminator: "RandomWait")]
[JsonDerivedType(typeof(WaitForActionKindDto), typeDiscriminator: "WaitForAction")]
[JsonDerivedType(typeof(WaitForTriggerKindDto), typeDiscriminator: "WaitForTrigger")]
[JsonDerivedType(typeof(WaitForPreviousKindDto), typeDiscriminator: "WaitForPrevious")]
public abstract record EventKindDto;

public record TimedKindDto() : EventKindDto;

public record RandomWaitKindDto(
    [property: JsonPropertyName("minSec")] float MinSec,
    [property: JsonPropertyName("maxSec")] float MaxSec
) : EventKindDto;

public record WaitForActionKindDto(
    [property: JsonPropertyName("actionId")] string ActionId
) : EventKindDto;

public record WaitForTriggerKindDto(
    [property: JsonPropertyName("triggerId")] string TriggerId
) : EventKindDto;

public record WaitForPreviousKindDto() : EventKindDto;
