namespace Comet.Simulation
{
    /// <summary>Movement constants shared by every player: the body's size and the world's physics.</summary>
    public sealed class MovementRules
    {
        /// <summary>Downward acceleration, in meters per second squared.</summary>
        public float Gravity { get; set; } = 25f;

        /// <summary>The player's collision body: an upright cylinder of this radius, in meters.</summary>
        public float BodyRadius { get; set; } = 0.4f;

        /// <summary>The player's collision body height, in meters.</summary>
        public float BodyHeight { get; set; } = 1f;

        /// <summary>The highest ledge a player walks up without jumping, in meters.</summary>
        public float StepHeight { get; set; } = 0.35f;

        /// <summary>The steepest ground a player stands on, in degrees from level; steeper is a wall.</summary>
        public float MaxSlope { get; set; } = 60f;

        /// <summary>How far below the feet the ground can drop while a player stays grounded (walking downhill), in meters.</summary>
        public float GroundSnap { get; set; } = 0.4f;

        /// <summary>Horizontal acceleration on the ground, in meters per second squared.</summary>
        public float GroundAcceleration { get; set; } = 40f;

        /// <summary>Horizontal acceleration in the air, in meters per second squared.</summary>
        public float AirAcceleration { get; set; } = 15f;

        /// <summary>How fast a player turns to face the way it's moving, in degrees per second. Only facing turns; movement changes direction at once.</summary>
        public float TurnSpeed { get; set; } = 900f;

        /// <summary>The highest a jump rises: v² / 2g.</summary>
        public float JumpApex(float jumpVelocity) => jumpVelocity * jumpVelocity / (2 * Gravity);
    }
}
