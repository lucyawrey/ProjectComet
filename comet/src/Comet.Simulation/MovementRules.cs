namespace Comet.Simulation
{
    public sealed class MovementRules
    {
        /// <summary>Downward acceleration, in meters per second squared.</summary>
        public float Gravity { get; set; } = 25f;

        /// <summary>The collision body is an upright cylinder of this radius, in meters.</summary>
        public float BodyRadius { get; set; } = 0.4f;

        /// <summary>The collision body height, in meters.</summary>
        public float BodyHeight { get; set; } = 1f;

        /// <summary>The highest ledge a player walks up without jumping, in meters.</summary>
        public float StepHeight { get; set; } = 0.35f;

        /// <summary>The most an airborne player rides up onto a ledge it moves into, in meters (also capped by steepness); 0 turns it off.</summary>
        public float MantleHeight { get; set; } = 0.35f;

        /// <summary>The steepest ground a player stands on, in degrees from level; steeper is a wall.</summary>
        public float MaxSlope { get; set; } = 60f;

        /// <summary>How far the ground can drop below the feet while a player stays grounded, in meters.</summary>
        public float GroundSnap { get; set; } = 0.4f;

        /// <summary>Horizontal acceleration on the ground, in meters per second squared.</summary>
        public float GroundAcceleration { get; set; } = 40f;

        /// <summary>Horizontal acceleration in the air, in meters per second squared.</summary>
        public float AirAcceleration { get; set; } = 15f;

        /// <summary>How fast facing turns, in degrees per second; movement changes direction at once.</summary>
        public float TurnSpeed { get; set; } = 900f;

        public float JumpApex(float jumpVelocity) => jumpVelocity * jumpVelocity / (2 * Gravity);
    }
}
