namespace Comet.ContentBuild;

/// <summary>A warning doesn't stop the build; <see cref="ToString"/> uses the compiler's <c>file(line,col): error: message</c> format.</summary>
public sealed record ContentError(string File, int Line, int Column, string Message, bool Warning = false)
{
    public override string ToString() => $"{File}({Line},{Column}): {(Warning ? "warning" : "error")}: {Message}";
}
