using System;
using System.Numerics;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

//TODO Increasing of Velocity Space (cm instead of m)

/// <summary>
/// Turns watch sensor snapshots into two outputs:
///
///  1. Orientation from Android's fused rotation vector (drift-stable, accurate).
///     Drives the 3D hand model directly.
///
///  2. Velocity trail one integration of world-frame linear acceleration with a
///     short 250 ms decay. This is what actually works from a consumer IMU:
///     - Zero drift (decays to zero when still within ~500 ms)
///     - Faithfully shows the shape and direction of each motion burst
///     - Distinguishable patterns per gesture (clap / hand-wash / jumping jacks)
///     Double-integrating to position is deliberately NOT done it always drifts
///     within 1-2 s and produces fake movement unrelated to real hand motion.
///
/// Two quaternions are used deliberately:
///  - Orientation: (Y,-X,Z) remapped quaternion empirically confirmed correct for the
///    hand model display.
///  - Physics: original un-remapped quaternion Android guarantees this maps device→world
///    ENU so gravity removal (aWorld.Z -= g) is exact at every wrist orientation.
/// </summary>
public sealed class MotionReconstructor
{
    // 250 ms decay: fast enough to show gesture shape, slow enough to see the arc.
    private const float VelocityTau = 0.25f;

    // ZUPT: snap velocity to zero faster when the hand is clearly still.
    private const float StillAccelThreshold = 0.35f;  // m/s^2
    private const float StillGyroThreshold  = 0.20f;  // rad/s
    private const long  StillHoldMs         = 200;

    private const float Gravity      = 9.81f;
    private const int   TrailCapacity = 256;

    private Quaternion _reference = Quaternion.Identity;

    private Vector3 _velocity;
    private long    _lastTs;
    private long    _stillSinceMs;
    // Any sample with timestamp ≤ this is a pre-reset sample; discard it.
    private long    _discardBefore;

    private readonly Vector3[] _trail = new Vector3[TrailCapacity];
    private int _trailStart;
    private int _trailCount;

    /// <summary>Reset-relative orientation of the hand. Drives the 3D model.</summary>
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;

    /// <summary>Latest raw device→world orientation (absolute, normalized).</summary>
    public Quaternion RawOrientation { get; private set; } = Quaternion.Identity;

    // ── Configurable visual axis remap ────────────────────────────────────
    // Each visual axis (X, Y, Z) can pull from any Android component (0=X 1=Y 2=Z)
    // and optionally negate it.  Default (1,false, 0,true, 2,false) = (Y, -X, Z).
    public int  RemapSrcX { get; set; } = 1; public bool RemapFlipX { get; set; } = false;
    public int  RemapSrcY { get; set; } = 0; public bool RemapFlipY { get; set; } = true;
    public int  RemapSrcZ { get; set; } = 2; public bool RemapFlipZ { get; set; } = false;

    private Quaternion ApplyRemap(Quaternion q)
    {
        float[] c = [q.X, q.Y, q.Z];
        return new Quaternion(
            RemapFlipX ? -c[RemapSrcX] : c[RemapSrcX],
            RemapFlipY ? -c[RemapSrcY] : c[RemapSrcY],
            RemapFlipZ ? -c[RemapSrcZ] : c[RemapSrcZ],
            q.W);
    }

    /// <summary>Current world-frame velocity in m/s (decays to zero when still).</summary>
    public Vector3 Velocity => _velocity;

    /// <summary>Number of trail points available (velocity history).</summary>
    public int TrailCount => _trailCount;

    /// <summary>Trail point by age: 0 = oldest, TrailCount-1 = newest (current velocity).</summary>
    public Vector3 TrailPoint(int i) => _trail[(_trailStart + i) % TrailCapacity];

    /// <summary>Capture current pose as the new zero AND clear the velocity trail.</summary>
    public void Reset()
    {
        _reference  = Quaternion.Conjugate(RawOrientation);
        Orientation = Quaternion.Identity;
        ResetVelocity();
    }

    /// <summary>Clear velocity and trail only orientation reference is unchanged.</summary>
    public void ResetVelocity()
    {
        _discardBefore = _lastTs; // reject any samples already in-flight
        _velocity      = Vector3.Zero;
        _lastTs        = 0;
        _stillSinceMs  = 0;
        _trailStart    = 0;
        _trailCount    = 0;
    }

    public void Step(WatchDataSnapshot s)
    {
        // Discard samples that were already in-flight when ResetVelocity was called.
        if (s.Timestamp <= _discardBefore) return;

        // --- Orientation ---
        // Compute the relative rotation in Android's proper frame first (group property
        // holds → reference cancels correctly regardless of starting pose). Then apply
        // the visual axis remap (X,-Z,-Y) to the already-relative quaternion so the
        // hand model renders correctly. Doing the remap before the relative composition
        // broke non-default positions because the remap is an improper transformation.
        var qAndroid = new Quaternion(s.RotationX, s.RotationY, s.RotationZ, s.RotationW);
        if (qAndroid.LengthSquared() < 1e-9f) qAndroid = Quaternion.Identity;
        else qAndroid = Quaternion.Normalize(qAndroid);
        RawOrientation = qAndroid;
        var qRel = Quaternion.Normalize(_reference * qAndroid);
        Orientation = ApplyRemap(qRel);

        var ts = s.Timestamp;
        if (_lastTs == 0) { _lastTs = ts; PushTrail(_velocity); return; }
        if (ts <= _lastTs) { _lastTs = ts; return; }

        var dt = MathF.Min((ts - _lastTs) / 1000f, 0.1f);
        _lastTs = ts;

        // --- World-frame linear acceleration (ORIGINAL quaternion, correct physics) ---
        // The visual remap (X,-Z,-Y) is only for the hand model. For gravity removal we
        // need the real Android rotation vector: it maps device→world ENU (X=East,
        // Y=North, Z=Up). In world ENU a stationary accelerometer reads +g on Z, so
        // subtracting g from Z is always correct regardless of wrist orientation.
        var qPhys = new Quaternion(s.RotationX, s.RotationY, s.RotationZ, s.RotationW);
        if (qPhys.LengthSquared() < 1e-9f) qPhys = Quaternion.Identity;
        else qPhys = Quaternion.Normalize(qPhys);
        var aWorld = Vector3.Transform(new Vector3(s.AccelX, s.AccelY, s.AccelZ), qPhys);
        aWorld.Z -= Gravity;

        // --- ZUPT ---
        var gyroMag = MathF.Sqrt(s.GyroX * s.GyroX + s.GyroY * s.GyroY + s.GyroZ * s.GyroZ);
        var accelMag = aWorld.Length();
        if (accelMag < StillAccelThreshold && gyroMag < StillGyroThreshold)
        {
            if (_stillSinceMs == 0) _stillSinceMs = ts;
            if (ts - _stillSinceMs >= StillHoldMs) _velocity = Vector3.Zero;
            aWorld = Vector3.Zero;
        }
        else
        {
            _stillSinceMs = 0;
        }

        // --- Velocity integration with exponential decay ---
        // One integration only no position. Decays in ~500 ms when hand is still.
        var decay = MathF.Exp(-dt / VelocityTau);
        _velocity = _velocity * decay + aWorld * dt;

        PushTrail(_velocity);
    }

    private void PushTrail(Vector3 v)
    {
        if (_trailCount < TrailCapacity)
        {
            _trail[(_trailStart + _trailCount) % TrailCapacity] = v;
            _trailCount++;
        }
        else
        {
            _trail[_trailStart] = v;
            _trailStart = (_trailStart + 1) % TrailCapacity;
        }
    }
}
