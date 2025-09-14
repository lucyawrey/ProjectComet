using AzaleaGames.ProjectComet.Api.Entities;

public class Content : IUpdated, IGeneratedId
{
    public long Id { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required string Name { get; set; }
    public required ContentType ContentType { get; set; }
    public required ContentSubtype ContentSubtype { get; set; }
    public bool AlwaysUnlocked { get; set; } = false;
    public required ContentData ContentData { get; set; }
}

public class ContentData
{

}

public enum ContentType
{
    None = 0,

    Class = 1,
    Craft = 2,

    Item = 100,
    Companion = 200,
    Unlockable = 300,
}

public enum ContentSubtype
{
    None = 0,

    Currency = 100,
    Material = 101,
    Consumable = 102,
    QuestItem = 103,
    UnlockItem = 104,
    Equipment = 105,
    ContainerItem = 106,
    ClassItem = 107,

    Mount = 200,
    Pet = 201,

    ColorOption = 350,
    BodyTypeOption = 351,
    HairstyleOption = 352,
    MakeupOptions = 353,
    UnderclothesOption = 354,
}
