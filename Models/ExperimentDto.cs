using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OcdWizard.ControlStation.Models;

public record InterventionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("notification")] string Notification,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("durationSeconds")] int DurationSeconds,
    [property: JsonPropertyName("gameType")] string? GameType = null,
    [property: JsonPropertyName("phoneTaskType")] string? PhoneTaskType = null,
    [property: JsonPropertyName("triviaQuestion")] TriviaQuestion? TriviaQuestion = null
);

public record ExperimentEventDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("duration")] float Duration = 0f,
    [property: JsonPropertyName("type")] string Type = "",
    [property: JsonPropertyName("intervention")] InterventionDto? Intervention = null,
    [property: JsonPropertyName("gameType")] string? GameType = null,
    [property: JsonPropertyName("kind")] EventKindDto? Kind = null
);

public record ExperimentDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] long Version = 1L,
    [property: JsonPropertyName("updatedAt")] long UpdatedAt = 0L,
    [property: JsonPropertyName("events")] List<ExperimentEventDto>? Events = null,
    [property: JsonPropertyName("triggers")] List<TriggerDto>? Triggers = null
);

public record ExperimentEnvelopeDto(
    [property: JsonPropertyName("etag")] string Etag,
    [property: JsonPropertyName("experiment")] ExperimentDto Experiment
);

public record ExperimentPutRequestDto(
    [property: JsonPropertyName("experiment")] ExperimentDto Experiment,
    [property: JsonPropertyName("ifMatch")] string? IfMatch = null
);

public record ExperimentControlDto(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("runId")] string? RunId = null,
    [property: JsonPropertyName("targetEventIndex")] int? TargetEventIndex = null
);
