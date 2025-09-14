namespace AzaleaGames.ProjectComet.Api.Entities;

public class Character : IUpdated, IGeneratedId, IGeneratedHandle
{
    public long Id { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Handle { get; set; } = 0;
    public required string Name { get; set; }
    public Role Role { get; set; } = Role.NewPlayer;
    public required World HomeWorld { get; set; }
    public required User User { get; set; }
    public Ancestry Ancestry { get; set; } = Ancestry.Catkin;
    public Gender Gender { get; set; } = Gender.TheyThem;
    public required CharacterOptions CharacterOptions { get; set; }
    public required CharacterData CharacterData { get; set; }
}

public enum Ancestry
{
    Catkin = 0, Human = 1, Elf = 2,
}

public enum Gender
{
    TheyThem = 0, ItIts = 1, SheHer = 2, HeHim = 3
}

public class CharacterOptions
{

}

public class CharacterData
{

}
