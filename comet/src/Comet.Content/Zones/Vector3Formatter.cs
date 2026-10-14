using System.Numerics;
using MessagePack;
using MessagePack.Formatters;

namespace Comet.Content
{
    /// <summary>Writes a <see cref="Vector3"/> as an array of three floats.</summary>
    public sealed class Vector3Formatter : IMessagePackFormatter<Vector3>
    {
        public void Serialize(ref MessagePackWriter writer, Vector3 value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(3);
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }

        public Vector3 Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.ReadArrayHeader() != 3)
            {
                throw new MessagePackSerializationException("A Vector3 needs three numbers.");
            }

            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
