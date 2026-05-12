using System.Text.Json.Serialization;

namespace OcdWizard.ControlStation.Models;

public record InterventionRequest(
    [property: JsonPropertyName("notificationType")] string NotificationType,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("durationMs")] int DurationMs = 30000,
    [property: JsonPropertyName("gameType")] string? GameType = null,
    [property: JsonPropertyName("phoneTaskType")] string? PhoneTaskType = null,
    [property: JsonPropertyName("triviaQuestion")] TriviaQuestion? TriviaQuestion = null
);
