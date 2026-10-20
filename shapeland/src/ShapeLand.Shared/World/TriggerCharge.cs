using Comet.Content;

namespace ShapeLand.Shared.World
{
    /// <summary>
    /// Times standing in trigger volumes, for one player (their spawn claims or teleporters) or for one trigger
    /// shared by everyone in it: fires once whoever it times has been inside for the volume's stand time, then again
    /// only after stepping out (<see cref="Rearm.StepOff"/>) or every stand time while still inside
    /// (<see cref="Rearm.Repeat"/>). The server decides with it; the client times its own player with it too, to
    /// draw a pad charging.
    /// </summary>
    public sealed class TriggerCharge
    {
        private object? _inside;
        private TriggerVolume? _volume;
        private float _since;
        private bool _fired;

        /// <summary>
        /// Takes what's being stood in now (<paramref name="inside"/>, with its <paramref name="volume"/>; null for
        /// nothing) at <paramref name="seconds"/>, and returns whether it fires now. Stepping into something else
        /// starts over.
        /// </summary>
        public bool Step(object? inside, TriggerVolume? volume, float seconds)
        {
            if (inside != _inside)
            {
                _inside = inside;
                _volume = volume;
                _since = seconds;
                _fired = false;
            }

            if (_inside == null || _volume == null || _fired || seconds - _since < _volume.StandSeconds)
            {
                return false;
            }

            if (_volume.Rearm == Rearm.Repeat)
            {
                _since = seconds;
            }
            else
            {
                _fired = true;
            }

            return true;
        }

        /// <summary>
        /// Counts the player as already inside <paramref name="inside"/> and fired, so it doesn't fire until they've
        /// stepped out and in again: for arriving on a teleporter.
        /// </summary>
        public void Arrived(object inside, TriggerVolume volume)
        {
            _inside = inside;
            _volume = volume;
            _fired = true;
        }

        /// <summary>Forgets what was stood in (after the player went somewhere else).</summary>
        public void Clear()
        {
            _inside = null;
            _volume = null;
        }

        /// <summary>
        /// How far it is toward firing at <paramref name="seconds"/>, from 0 (just stepped in, or fired and waiting
        /// for a step off) to 1; 0 when standing in nothing.
        /// </summary>
        public float Progress(float seconds)
        {
            if (_inside == null || _volume == null || _fired)
            {
                return 0;
            }

            return _volume.StandSeconds <= 0 ? 1 : System.Math.Min(1, (seconds - _since) / _volume.StandSeconds);
        }

        /// <summary>What's being stood in, or null.</summary>
        public object? Inside => _inside;
    }
}
