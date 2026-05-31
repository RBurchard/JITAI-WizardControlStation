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
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("gyroX")] float GyroX = 0f,
    [property: JsonPropertyName("gyroY")] float GyroY = 0f,
    [property: JsonPropertyName("gyroZ")] float GyroZ = 0f,
    [property: JsonPropertyName("rotationX")] float RotationX = 0f,
    [property: JsonPropertyName("rotationY")] float RotationY = 0f,
    [property: JsonPropertyName("rotationZ")] float RotationZ = 0f,
    [property: JsonPropertyName("rotationW")] float RotationW = 0f,
    [property: JsonPropertyName("barometer")] float Barometer = 0f,
    [property: JsonPropertyName("light")] float Light = 0f
);

public record WatchDataBatch(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("snapshots")] List<WatchDataSnapshot> Snapshots
);
