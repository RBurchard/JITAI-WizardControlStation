using System.Text.Json.Serialization;

namespace OcdWizard.ControlStation.Models;

public record WsEnvelopeDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payloadJson")] string PayloadJson
)
{
    public const string TYPE_STATE = "state";
    public const string TYPE_LOG = "log";
    public const string TYPE_TRIGGER = "trigger";
    public const string TYPE_EVENT_ADVANCE = "event-advance";
    public const string TYPE_RUN_STARTED = "run-started";
    public const string TYPE_RUN_STOPPED = "run-stopped";
    public const string TYPE_INTERVENTION_STARTED = "intervention-started";
    public const string TYPE_INTERVENTION_FINISHED = "intervention-finished";
}

public record EngineStateSnapshotDto(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("currentEventIndex")] int CurrentEventIndex,
    [property: JsonPropertyName("currentEventId")] string? CurrentEventId = null,
    [property: JsonPropertyName("currentEventKind")] string? CurrentEventKind = null,
    [property: JsonPropertyName("elapsedSecInEvent")] float ElapsedSecInEvent = 0f,
    [property: JsonPropertyName("waitingForActionId")] string? WaitingForActionId = null,
    [property: JsonPropertyName("waitingForTriggerId")] string? WaitingForTriggerId = null,
    [property: JsonPropertyName("randomWaitRemainingSec")] float? RandomWaitRemainingSec = null,
    [property: JsonPropertyName("runId")] string? RunId = null,
    [property: JsonPropertyName("experimentId")] string? ExperimentId = null,
    [property: JsonPropertyName("experimentName")] string? ExperimentName = null
);

public record ProtocolPaths
{
    public const string EXPERIMENT = "/experiment";
    public const string LOGS = "/logs";
    public const string EXPERIMENT_CONTROL = "/experiment/control";
    public const string TRIGGER_FIRE = "/trigger/fire";
    public const string EVENTS_WS = "/events";
    public const string ETAG_HEADER = "X-Experiment-Etag";
    public const int HTTP_PORT = 8080;
}
