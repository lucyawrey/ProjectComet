namespace Comet.ContentBuild;

/// <summary>
/// A problem in a content file. <see cref="ToString"/> uses the compiler's <c>file(line,col): error: message</c>
/// format, so terminals and editors link straight to the spot.
/// </summary>
public sealed record ContentError(string File, int Line, int Column, string Message)
{
    public override string ToString() => $"{File}({Line},{Column}): error: {Message}";
}
