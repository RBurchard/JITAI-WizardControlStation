using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.ViewModels;

/// <summary>
/// Drives one participant's panel during replay. Feeds recorded watch snapshots into the shared
/// <see cref="Views.MotionView"/> (via <see cref="IMotionSource"/>) as playback time advances, and
/// exposes the sensor read-outs at the current playback time.
///
/// Forward stepping just enqueues the snapshots that were crossed; a backward seek raises
/// <see cref="MotionResetRequested"/> and re-warms the reconstructor from a short window before the
/// target so orientation and the velocity trail recover deterministically.
/// </summary>
public partial class ReplayParticipantViewModel : ObservableObject, IMotionSource
{
    // The reconstructor's velocity decays in ~500 ms; 1.5 s of warm-up rebuilds orientation + trail.
    private const long WarmupMs = 1500;

    private readonly List<WatchDataSnapshot> _samples;
    private int _fedIndex = -1; // index of the last sample pushed to the motion view

    public string Label { get; }
    public string Key { get; }

    [ObservableProperty] private float _currentBpm;
    [ObservableProperty] private float _accelX;
    [ObservableProperty] private float _accelY;
    [ObservableProperty] private float _accelZ;
    [ObservableProperty] private int _stepCount;
    [ObservableProperty] private float _rotW = 1f;
    [ObservableProperty] private float _rotX;
    [ObservableProperty] private float _rotY;
    [ObservableProperty] private float _rotZ;
    [ObservableProperty] private long _currentSampleTs;
    [ObservableProperty] private int _sampleCount;

    public event Action<IReadOnlyList<WatchDataSnapshot>>? MotionSamplesReceived;
    public event Action? MotionResetRequested;

    public ReplayParticipantViewModel(ReplayParticipant p)
    {
        Label = p.Label;
        Key = p.Key;
        _samples = p.Samples;
        SampleCount = _samples.Count;
    }

    /// <summary>Clear motion state back to "nothing fed" (called on (re)load / seek to start).</summary>
    public void ResetMotion()
    {
        _fedIndex = -1;
        MotionResetRequested?.Invoke();
    }

    /// <summary>Move the participant's view to absolute epoch time <paramref name="absMs"/>.</summary>
    public void SeekTo(long absMs)
    {
        if (_samples.Count == 0) return;

        int target = LastIndexAtOrBefore(absMs);

        if (target > _fedIndex)
        {
            // Forward: feed everything crossed since the last push.
            int from = _fedIndex + 1;
            int count = target - _fedIndex;
            if (count > 0) MotionSamplesReceived?.Invoke(_samples.GetRange(from, count));
        }
        else if (target < _fedIndex)
        {
            // Backward: reset and re-warm from a short window before the target.
            MotionResetRequested?.Invoke();
            if (target >= 0)
            {
                int warmStart = FirstIndexAtOrAfter(absMs - WarmupMs);
                if (warmStart < 0 || warmStart > target) warmStart = target;
                MotionSamplesReceived?.Invoke(_samples.GetRange(warmStart, target - warmStart + 1));
            }
        }

        _fedIndex = target;
        UpdateReadout(target);
    }

    private void UpdateReadout(int index)
    {
        if (index < 0 || index >= _samples.Count) return;
        var s = _samples[index];
        CurrentBpm = s.HeartRate;
        AccelX = s.AccelX; AccelY = s.AccelY; AccelZ = s.AccelZ;
        StepCount = s.StepCount;
        RotW = s.RotationW; RotX = s.RotationX; RotY = s.RotationY; RotZ = s.RotationZ;
        CurrentSampleTs = s.Timestamp;
    }

    // Last sample with Timestamp <= absMs, or -1 if absMs precedes the first sample.
    private int LastIndexAtOrBefore(long absMs)
    {
        int lo = 0, hi = _samples.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_samples[mid].Timestamp <= absMs) { ans = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return ans;
    }

    // First sample with Timestamp >= absMs, or -1 if all precede it.
    private int FirstIndexAtOrAfter(long absMs)
    {
        int lo = 0, hi = _samples.Count - 1, ans = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_samples[mid].Timestamp >= absMs) { ans = mid; hi = mid - 1; }
            else lo = mid + 1;
        }
        return ans;
    }
}
