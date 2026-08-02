using System;
using System.Collections.Generic;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.ViewModels;

/// <summary>
/// Contract the <see cref="Views.MotionView"/> binds to, so the same 3D hand-model +
/// velocity-trail view drives both the live participant panel and the replay panel.
///
/// Live: <see cref="ParticipantViewModel"/> raises <see cref="MotionSamplesReceived"/> as
/// batches stream off the watch and never raises <see cref="MotionResetRequested"/>.
/// Replay: <see cref="ReplayParticipantViewModel"/> raises samples as playback advances and
/// raises <see cref="MotionResetRequested"/> on a backward seek so the reconstructor can
/// re-warm from a clean state.
/// </summary>
public interface IMotionSource
{
    /// <summary>Latest heart rate (drives the HeartbeatView pulse).</summary>
    float CurrentBpm { get; }

    /// <summary>Fired with the ordered snapshot series to feed into the reconstructor.</summary>
    event Action<IReadOnlyList<WatchDataSnapshot>>? MotionSamplesReceived;

    /// <summary>Fired when the view should clear the reconstructor (e.g. a backward seek).</summary>
    event Action? MotionResetRequested;
}
