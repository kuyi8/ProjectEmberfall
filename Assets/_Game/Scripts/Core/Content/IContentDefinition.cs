using Emberfall.Core.Identifiers;

namespace Emberfall.Core.Content
{
    /// <summary>
    /// Implemented by immutable authored-data records that can be indexed at runtime.
    /// </summary>
    public interface IContentDefinition
    {
        ContentId Id { get; }
    }
}
