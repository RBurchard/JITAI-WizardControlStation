using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OcdWizard.ControlStation.Models;

public class TriviaQuestion
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("question")] public string Question { get; set; } = "";
    [JsonPropertyName("answers")] public List<string> Answers { get; set; } = ["", "", "", ""];
    [JsonPropertyName("correctIndex")] public int CorrectIndex { get; set; }
}
