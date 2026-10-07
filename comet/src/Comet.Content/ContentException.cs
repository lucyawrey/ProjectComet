using System;

namespace Comet.Content
{
    /// <summary>Content is missing, unknown or malformed.</summary>
    public sealed class ContentException : Exception
    {
        public ContentException(string message)
            : base(message)
        {
        }

        public ContentException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
