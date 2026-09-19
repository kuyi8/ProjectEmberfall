using System;
using System.Collections.Generic;
using Emberfall.Core.Identifiers;

namespace Emberfall.Core.Content
{
    /// <summary>
    /// Read-only runtime index built from validated authored data.
    /// Runtime state belongs elsewhere and must never be written back into these definitions.
    /// </summary>
    public sealed class ContentRegistry<TDefinition>
        where TDefinition : class, IContentDefinition
    {
        private readonly Dictionary<ContentId, TDefinition> _definitions;

        public ContentRegistry(IEnumerable<TDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            _definitions = new Dictionary<ContentId, TDefinition>();
            foreach (TDefinition definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException("Content definitions cannot contain null entries.", nameof(definitions));
                }

                if (definition.Id.IsEmpty)
                {
                    throw new ArgumentException("Content definitions must have a non-empty stable ID.", nameof(definitions));
                }

                if (!_definitions.TryAdd(definition.Id, definition))
                {
                    throw new ArgumentException($"Duplicate content ID '{definition.Id}'.", nameof(definitions));
                }
            }
        }

        public int Count => _definitions.Count;

        public bool TryGet(ContentId id, out TDefinition definition) => _definitions.TryGetValue(id, out definition);

        public TDefinition GetRequired(ContentId id)
        {
            if (_definitions.TryGetValue(id, out TDefinition definition))
            {
                return definition;
            }

            throw new KeyNotFoundException($"Content ID '{id}' is not registered.");
        }
    }
}
