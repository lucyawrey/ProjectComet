namespace Comet.Simulation
{
    /// <summary>Movement constants shared by every player: the body's size and the world's physics.</summary>
    public sealed class MovementRules
    {
        /// <summary>Downward acceleration, in metres per second squared.</summary>
        public float Gravity { get; set; } = 25f;

        /// <summary>The player's collision body: a square footprint this far from its centre, in metres.</summary>
        public float BodyRadius { get; set; } = 0.4f;

        /// <summary>The player's collision body height, in metres.</summary>
        public float BodyHeight { get; set; } = 1f;

        /// <summary>The highest ledge a player walks up without jumping, in metres.</summary>
        public float StepHeight { get; set; } = 0.35f;

        /// <summary>How far below the feet the ground can drop while a player stays grounded (walking downhill), in metres.</summary>
        public float GroundSnap { get; set; } = 0.4f;

        /// <summary>Horizontal acceleration on the ground and in the air, in metres per second squared.</summary>
        public float GroundAcceleration { get; set; } = 40f;

        public float AirAcceleration { get; set; } = 15f;

        /// <summary>The highest a jump rises: v² / 2g.</summary>
        public float JumpApex(float jumpVelocity) => jumpVelocity * jumpVelocity / (2 * Gravity);
    }
}
