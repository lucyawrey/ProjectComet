namespace Comet.Data;

/// <summary>Set by <see cref="TimestampInterceptor"/> on save; don't set these yourself.</summary>
public interface ITimestamped
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
