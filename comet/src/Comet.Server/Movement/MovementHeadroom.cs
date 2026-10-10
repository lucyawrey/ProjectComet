namespace Comet.Server.Movement;

/// <summary>
/// How much of the movement tolerances one player's reports have actually needed, for tuning them
/// (<see cref="MovementTolerances"/>). Measured against exact rules, not the current tolerances, so it shows how far
/// they could tighten before this player would have been snapped back.
/// </summary>
public sealed class MovementHeadroom
{
    /// <summary>The speed factors the burst need is measured for.</summary>
    public static readonly float[] SpeedFactors = [1.0f, 1.05f, 1.1f, 1.15f, 1.2f];

    // Per speed factor: the running shortfall of a distance budget earned at that speed (a token bucket's deficit),
    // and the largest it has been.
    private readonly float[] _deficit = new float[SpeedFactors.Length];
    private readonly float[] _maxDeficit = new float[SpeedFactors.Length];
    private readonly float[] _maxSpeed = new float[SpeedFactors.Length];

    /// <summary>The highest any accepted position has been above an exact jump's apex from the last ground, in metres.</summary>
    public float MaxRiseOverApex { get; private set; } = float.NegativeInfinity;

    /// <summary>
    /// The most an accepted position's arc needed to start late, in seconds: how much hang time past an exact jump's
    /// fall it used (<see cref="MovementTolerances.AirTimeSlack"/>).
    /// </summary>
    public float MaxAirSlack { get; private set; } = float.NegativeInfinity;

    /// <summary>
    /// The banked movement, in seconds at <c>factor</c> × max speed, that this player's reports needed so far with that
    /// speed factor (<see cref="MovementTolerances.MaxBurstSeconds"/>); 0 if they never moved faster than it allows.
    /// </summary>
    public float BurstSecondsNeeded(int factorIndex) =>
        _maxSpeed[factorIndex] > 0 ? _maxDeficit[factorIndex] / _maxSpeed[factorIndex] : 0;

    internal void AddMove(float distance, float elapsedSeconds, float maxSpeed)
    {
        for (var i = 0; i < SpeedFactors.Length; i++)
        {
            var speed = maxSpeed * SpeedFactors[i];
            _maxSpeed[i] = speed;
            _deficit[i] = MathF.Max(0, _deficit[i] - speed * elapsedSeconds + distance);
            _maxDeficit[i] = MathF.Max(_maxDeficit[i], _deficit[i]);
        }
    }

    /// <param name="rise">Height above the last ground.</param>
    /// <param name="airSeconds">Time since the last ground, by the report's stamp.</param>
    internal void AddHeight(float rise, float airSeconds, float jumpVelocity, float gravity)
    {
        var apex = jumpVelocity * jumpVelocity / (2 * gravity);
        MaxRiseOverApex = MathF.Max(MaxRiseOverApex, rise - apex);
        if (rise > 0 && rise <= apex)
        {
            // On an exact jump's way down, this height is reached this long after the apex.
            var falling = MathF.Sqrt(2 * (apex - rise) / gravity);
            MaxAirSlack = MathF.Max(MaxAirSlack, airSeconds - jumpVelocity / gravity - falling);
        }
    }
}
