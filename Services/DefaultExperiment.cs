using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

/// <summary>
/// Loads the bundled default experiment shipped as Assets/default_experiment.json. This is the
/// starting point shown in a fresh editor and is kept identical to the Android app's
/// mobile/src/main/assets/default_experiment.json so both seed the same ~10-minute default.
/// Update flow: edit in the editor, Export, replace the JSON in both repos, rebuild.
/// </summary>
public static class DefaultExperiment
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ExperimentDto Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "default_experiment.json");
            if (File.Exists(path))
            {
                var dto = JsonSerializer.Deserialize<ExperimentDto>(File.ReadAllText(path), JsonOpts);
                if (dto != null) return dto;
            }
        }
        catch
        {
            // A missing or corrupt asset must never crash startup — fall back to an empty
            // experiment (the prior behaviour) so the editor still opens.
        }
        return new ExperimentDto(
            Guid.NewGuid().ToString(), Loc.Get("Editor_UntitledExperiment"), 1L,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), "",
            new List<ExperimentEventDto>(), new List<TriggerDto>());
    }
}
