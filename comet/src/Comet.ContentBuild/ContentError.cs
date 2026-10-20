namespace Comet.ContentBuild;

/// <summary>
/// A problem in a content file, or with <see cref="Warning"/> something to deal with before a release that doesn't
/// stop the build. <see cref="ToString"/> uses the compiler's <c>file(line,col): error: message</c> format, so
/// terminals and editors link straight to the spot.
/// </summary>
public sealed record ContentError(string File, int Line, int Column, string Message, bool Warning = false)
{
    public override string ToString() => $"{File}({Line},{Column}): {(Warning ? "warning" : "error")}: {Message}";
}
