using System;
using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>A moving player's physical state.</summary>
    public struct MotorState
    {
        /// <summary>The bottom centre of the body.</summary>
        public Vector3 Position;
        public Vector3 Velocity;
        public bool Grounded;

        /// <summary>Facing as a yaw angle in radians (0 faces +Z).</summary>
        public float Facing;
    }

    /// <summary>
    /// Moves a player one step: slides towards the wanted horizontal velocity, jumps, falls, stands on ground
    /// and slides along boxes. Clients and bots move with it, so their movement fits the rules the server checks.
    /// </summary>
    public static class PlayerMotor
    {
        /// <param name="state">The player's state, updated in place.</param>
        /// <param name="move">Wanted direction on the ground plane (X, Z), length 0 to 1.</param>
        /// <param name="jump">Start a jump if grounded.</param>
        /// <param name="seconds">The step length.</param>
        /// <param name="maxSpeed">The player's top ground speed, in metres per second.</param>
        /// <param name="jumpVelocity">The player's jump take-off speed, in metres per second.</param>
        public static void Step(ref MotorState state, Vector2 move, bool jump, float seconds, float maxSpeed, float jumpVelocity, CollisionWorld world, MovementRules rules)
        {
            if (move.LengthSquared() > 1)
            {
                move = Vector2.Normalize(move);
            }

            // Horizontal: accelerate towards the wanted velocity.
            var wanted = move * maxSpeed;
            var current = new Vector2(state.Velocity.X, state.Velocity.Z);
            var change = wanted - current;
            var maxChange = (state.Grounded ? rules.GroundAcceleration : rules.AirAcceleration) * seconds;
            if (change.Length() > maxChange)
            {
                change = Vector2.Normalize(change) * maxChange;
            }

            current += change;
            if (move.LengthSquared() > 0.0001f)
            {
                state.Facing = MathF.Atan2(move.X, move.Y);
            }

            // Vertical: jump or fall.
            var velocityY = state.Velocity.Y;
            if (state.Grounded && jump)
            {
                velocityY = jumpVelocity;
                state.Grounded = false;
            }
            else if (!state.Grounded)
            {
                velocityY -= rules.Gravity * seconds;
            }

            // Move horizontally, sliding along boxes one axis at a time.
            var position = state.Position;
            var stepUp = state.Grounded ? rules.StepHeight : 0;
            position = TryMoveAxis(position, new Vector3(current.X * seconds, 0, 0), stepUp, world, rules, ref current.X);
            position = TryMoveAxis(position, new Vector3(0, 0, current.Y * seconds), stepUp, world, rules, ref current.Y);

            // Move vertically, landing on ground or stopping under a box.
            var startY = position.Y;
            var targetY = startY + velocityY * seconds;
            if (velocityY > 0)
            {
                var up = new Vector3(position.X, targetY, position.Z);
                if (world.IsBlocked(up, rules.BodyRadius, rules.BodyHeight))
                {
                    velocityY = 0;
                    targetY = startY;
                }
            }

            // Terrain may rise by up to twice the distance moved (slopes up to ~63°), however fast the player moves.
            var snap = state.Grounded ? rules.GroundSnap : 0;
            var climb = 2 * Vector2.Distance(new Vector2(state.Position.X, state.Position.Z), new Vector2(position.X, position.Z));
            if (velocityY <= 0 && world.TryGetGround(position, rules.BodyRadius, startY + stepUp, out var ground, climb) && targetY - snap <= ground)
            {
                position.Y = ground;
                velocityY = 0;
                state.Grounded = true;
            }
            else
            {
                position.Y = targetY;
                state.Grounded = false;
            }

            state.Position = position;
            state.Velocity = new Vector3(current.X, velocityY, current.Y);
        }

        private static Vector3 TryMoveAxis(Vector3 position, Vector3 delta, float stepUp, CollisionWorld world, MovementRules rules, ref float axisVelocity)
        {
            if (delta == Vector3.Zero)
            {
                return position;
            }

            var moved = position + delta;
            if (!world.IsBlocked(moved, rules.BodyRadius, rules.BodyHeight))
            {
                return moved;
            }

            // Walk up a low ledge (a step) if the body fits on top of it.
            if (stepUp > 0 && world.TryGetGround(moved, rules.BodyRadius, position.Y + stepUp, out var ledge) && ledge > position.Y)
            {
                var stepped = new Vector3(moved.X, ledge, moved.Z);
                if (!world.IsBlocked(stepped, rules.BodyRadius, rules.BodyHeight))
                {
                    return stepped;
                }
            }

            axisVelocity = 0;
            return position;
        }
    }
}
