using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OcdWizard.ControlStation.Models;

public record SessionLogEntryDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("ts")] long Ts,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("runId")] string? RunId = null,
    [property: JsonPropertyName("level")] string Level = "INFO",
    [property: JsonPropertyName("payloadJson")] string PayloadJson = ""
);

public record SessionLogPageDto(
    [property: JsonPropertyName("entries")] List<SessionLogEntryDto> Entries,
    [property: JsonPropertyName("nextSince")] long NextSince
);
