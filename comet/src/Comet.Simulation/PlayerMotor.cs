using System;
using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    /// <summary>A moving player's physical state.</summary>
    public struct MotorState
    {
        /// <summary>The bottom center of the body.</summary>
        public Vector3 Position;
        /// <summary>In meters per second.</summary>
        public Vector3 Velocity;
        /// <summary>True while standing on terrain or a box.</summary>
        public bool Grounded;

        /// <summary>Facing as a yaw angle in radians (0 faces +Z).</summary>
        public float Facing;

        /// <summary>How far the last step lifted the body onto a ledge (a step or a mantle), so a client can draw it rising smoothly.</summary>
        public float Climbed;
    }

    /// <summary>
    /// Moves a player one step: slides toward the wanted horizontal velocity, jumps, falls, stands on ground
    /// and slides along solid meshes and terrain walls. Clients and bots move with it, so their movement fits the rules the server checks.
    /// </summary>
    public static class PlayerMotor
    {
        /// <param name="state">The player's state, updated in place.</param>
        /// <param name="move">Wanted direction on the ground plane (X, Z), length 0 to 1.</param>
        /// <param name="jump">Start a jump if grounded.</param>
        /// <param name="seconds">The step length.</param>
        /// <param name="maxSpeed">The player's top ground speed, in meters per second.</param>
        /// <param name="jumpVelocity">The player's jump take-off speed, in meters per second.</param>
        /// <param name="passThrough">Meshes the player passes through (turned on around them), or null.</param>
        public static void Step(ref MotorState state, Vector2 move, bool jump, float seconds, float maxSpeed, float jumpVelocity, CollisionWorld world, MovementRules rules, ISet<int>? passThrough = null)
        {
            if (move.LengthSquared() > 1)
            {
                move = Vector2.Normalize(move);
            }

            // Horizontal: accelerate toward the wanted velocity.
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
                state.Facing = TurnToward(state.Facing, MathF.Atan2(move.X, move.Y), rules.TurnSpeed * MathF.PI / 180 * seconds);
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

            // Move horizontally, sliding along walls one axis at a time and walking up low ledges. Falling, a move into
            // a ledge's side mantles onto it (rising, the jump carries on, so a mantle never adds to it); skimming the
            // ground, it also rides up slopes as steep as terrain climbs, however fast, so a jump up a slope doesn't
            // stop against it as it takes off or lands.
            var position = state.Position;
            var grounded = state.Grounded;
            var stepUp = grounded ? rules.StepHeight : 0;
            var skimming = !grounded && world.TryGetGround(position, rules.BodyRadius, position.Y, out var under, 0, passThrough)
                && position.Y - under <= 2 * current.Length() * seconds;
            float Climb(float axisMove) => grounded ? stepUp : MathF.Max(velocityY <= 0 ? rules.MantleHeight : 0, skimming ? 2 * MathF.Abs(axisMove) : 0);
            var climbed = 0f;
            position = TryMoveAxis(position, new Vector3(current.X * seconds, 0, 0), Climb(current.X * seconds), world, rules, passThrough, ref current.X, ref climbed);
            position = TryMoveAxis(position, new Vector3(0, 0, current.Y * seconds), Climb(current.Y * seconds), world, rules, passThrough, ref current.Y, ref climbed);

            // Move vertically, landing on ground or stopping under something solid.
            var startY = position.Y;
            var targetY = startY + velocityY * seconds;
            if (velocityY > 0)
            {
                var up = new Vector3(position.X, targetY, position.Z);
                if (world.IsBlocked(up, rules.BodyRadius, rules.BodyHeight, passThrough))
                {
                    velocityY = 0;
                    targetY = startY;
                }
            }

            // Terrain may rise by up to twice the distance moved (slopes up to ~63°), however fast the player moves.
            var snap = state.Grounded ? rules.GroundSnap : 0;
            var climb = 2 * Vector2.Distance(new Vector2(state.Position.X, state.Position.Z), new Vector2(position.X, position.Z));
            if (velocityY <= 0 && world.TryGetGround(position, rules.BodyRadius, startY + stepUp, out var ground, climb, passThrough) && targetY - snap <= ground)
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

            // Landing or stepping down beside a steep surface can leave the body in it, where no sideways move gets
            // out: push it out sideways. Where no push gets out (a V of steep faces), the fall stops where it fits.
            // A body held up that way stands, as on ground.
            var held = !world.TryPushOut(ref position, rules.BodyRadius, rules.BodyHeight, passThrough)
                ? velocityY <= 0 && world.TryRest(ref position, startY, rules.BodyRadius, rules.BodyHeight, passThrough)
                : !state.Grounded && velocityY <= 0 && world.IsWedged(position, rules.BodyRadius, rules.BodyHeight, passThrough);
            if (held)
            {
                velocityY = 0;
                state.Grounded = true;
            }

            state.Position = position;
            state.Velocity = new Vector3(current.X, velocityY, current.Y);
            state.Climbed = climbed;
        }

        // Turns a yaw angle the short way round toward another, by at most maxTurn radians; the result is in (-π, π].
        private static float TurnToward(float from, float to, float maxTurn)
        {
            var difference = to - from;
            difference -= 2 * MathF.PI * MathF.Floor((difference + MathF.PI) / (2 * MathF.PI));
            var turned = from + Math.Clamp(difference, -maxTurn, maxTurn);
            return turned - 2 * MathF.PI * MathF.Floor((turned + MathF.PI) / (2 * MathF.PI));
        }

        private static Vector3 TryMoveAxis(Vector3 position, Vector3 delta, float stepUp, CollisionWorld world, MovementRules rules, ISet<int>? passThrough, ref float axisVelocity, ref float climbed)
        {
            if (delta == Vector3.Zero)
            {
                return position;
            }

            var moved = position + delta;
            if (!world.IsBlocked(moved, rules.BodyRadius, rules.BodyHeight, passThrough))
            {
                return moved;
            }

            // Walk up a low ledge (a step, or a slope's rise) if the body fits on top of it.
            if (stepUp > 0 && world.TryGetGround(moved, rules.BodyRadius, position.Y + stepUp, out var ledge, 0, passThrough) && ledge > position.Y)
            {
                var stepped = new Vector3(moved.X, ledge, moved.Z);
                if (!world.IsBlocked(stepped, rules.BodyRadius, rules.BodyHeight, passThrough))
                {
                    // Rising no more than a slope as steep as terrain climbs isn't a ledge.
                    if (ledge - position.Y > 2 * delta.Length())
                    {
                        climbed += ledge - position.Y;
                    }

                    return stepped;
                }
            }

            axisVelocity = 0;
            return position;
        }
    }
}
