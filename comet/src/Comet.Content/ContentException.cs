using System;

namespace Comet.Content
{
    public sealed class ContentException : Exception
    {
        public ContentException(string message)
            : base(message) { }

        public ContentException(string message, Exception inner)
            : base(message, inner) { }
    }
}
