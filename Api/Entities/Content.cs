using AzaleaGames.ProjectComet.Api.Entities;

public class Content : IUpdated, IGeneratedId
{
    public long Id { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public required string Name { get; set; }
    public required ContentType ContentType { get; set; }
    public required ContentSubtype ContentSubtype { get; set; }
    public required ContentData ContentData { get; set; }
}

public class ContentData
{

}

public enum ContentType
{
    Item = 0, Companion = 1, Unlock = 2
}

public enum ContentSubtype
{

}
