using System;
using System.Collections.Generic;
using System.Numerics;

namespace Comet.Simulation
{
    public struct MotorState
    {
        public Vector3 Position;

        /// <summary>In meters per second.</summary>
        public Vector3 Velocity;

        public bool Grounded;

        /// <summary>Facing as a yaw angle in radians (0 faces +Z).</summary>
        public float Facing;
    }

    public static class PlayerMotor
    {
        /// <param name="move">Wanted direction on the ground plane (X, Z), length 0 to 1.</param>
        /// <param name="maxSpeed">The player's top ground speed, in meters per second.</param>
        /// <param name="jumpVelocity">The player's jump take-off speed, in meters per second.</param>
        /// <param name="passThrough">Meshes the player passes through, or null.</param>
        public static void Step(
            ref MotorState state,
            Vector2 move,
            bool jump,
            float seconds,
            float maxSpeed,
            float jumpVelocity,
            CollisionWorld world,
            MovementRules rules,
            ISet<int>? passThrough = null
        )
        {
            if (move.LengthSquared() > 1)
            {
                move = Vector2.Normalize(move);
            }

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

            var position = state.Position;
            var stepUp = state.Grounded ? rules.StepHeight : 0;
            position = TryMoveAxis(
                position,
                new Vector3(current.X * seconds, 0, 0),
                state.Grounded ? stepUp : MathF.Min(2 * MathF.Abs(current.X * seconds), rules.MantleHeight),
                world,
                rules,
                passThrough,
                ref current.X
            );
            position = TryMoveAxis(
                position,
                new Vector3(0, 0, current.Y * seconds),
                state.Grounded ? stepUp : MathF.Min(2 * MathF.Abs(current.Y * seconds), rules.MantleHeight),
                world,
                rules,
                passThrough,
                ref current.Y
            );

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

            var snap = state.Grounded ? rules.GroundSnap : 0;
            var climb = 2 * Vector2.Distance(new Vector2(state.Position.X, state.Position.Z), new Vector2(position.X, position.Z));
            if (
                velocityY <= 0
                && world.TryGetGround(position, rules.BodyRadius, startY + stepUp, out var ground, climb, passThrough)
                && targetY - snap <= ground
            )
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
        }

        private static float TurnToward(float from, float to, float maxTurn)
        {
            var difference = to - from;
            difference -= 2 * MathF.PI * MathF.Floor((difference + MathF.PI) / (2 * MathF.PI));
            var turned = from + Math.Clamp(difference, -maxTurn, maxTurn);
            return turned - 2 * MathF.PI * MathF.Floor((turned + MathF.PI) / (2 * MathF.PI));
        }

        private static Vector3 TryMoveAxis(
            Vector3 position,
            Vector3 delta,
            float stepUp,
            CollisionWorld world,
            MovementRules rules,
            ISet<int>? passThrough,
            ref float axisVelocity
        )
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

            if (stepUp > 0 && world.TryGetGround(moved, rules.BodyRadius, position.Y + stepUp, out var ledge, 0, passThrough) && ledge > position.Y)
            {
                var stepped = new Vector3(moved.X, ledge, moved.Z);
                if (!world.IsBlocked(stepped, rules.BodyRadius, rules.BodyHeight, passThrough))
                {
                    return stepped;
                }
            }

            axisVelocity = 0;
            return position;
        }
    }
}
