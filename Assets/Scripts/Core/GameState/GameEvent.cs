namespace Pantheon.Core
{
    /// <summary>
    /// Fat semantic event: a named thing that happened, carrying its own deltas as payload,
    /// so a view can render it with zero rules knowledge.
    /// </summary>
    public abstract record GameEvent;
}
