namespace OcdWizard.ControlStation.Models;

public record SessionSummary(
    string Id,
    long StartedAt,
    long? EndedAt,
    string Name,
    string Participants,
    string Experiments,
    long WatchDataCount);
