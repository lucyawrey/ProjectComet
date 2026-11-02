namespace Comet.Data;

/// <summary>A row with creation and change times, stamped by <see cref="TimestampInterceptor"/> on every save.</summary>
public interface ITimestamped
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
