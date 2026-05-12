using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OcdWizard.ControlStation.Models;

public record WatchDataSnapshot(
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("heartRate")] float HeartRate,
    [property: JsonPropertyName("accelX")] float AccelX,
    [property: JsonPropertyName("accelY")] float AccelY,
    [property: JsonPropertyName("accelZ")] float AccelZ,
    [property: JsonPropertyName("stepCount")] int StepCount,
    [property: JsonPropertyName("sessionId")] string SessionId
);

public record WatchDataBatch(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("snapshots")] List<WatchDataSnapshot> Snapshots
);
