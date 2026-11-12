using MessagePack;

namespace ShapeLand.Shared.Messages
{
    [MessagePackObject]
    public struct ZoneEntered
    {
        [Key(0)]
        public int Zone;

        [Key(1)]
        public string[] Flipped;
    }

    [MessagePackObject]
    public struct PlacementsFlipped
    {
        [Key(0)]
        public string Tag;

        [Key(1)]
        public bool Flipped;
    }
}
