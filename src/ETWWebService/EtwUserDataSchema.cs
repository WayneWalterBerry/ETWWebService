using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;


namespace ETWWebService
{
    /// <summary>
    /// Represents the schema for an ETW provider's events and their user data columns.
    /// </summary>
    public class EtwUserDataSchema
    {
        /// <summary>
        /// Gets the provider GUID associated with this schema.
        /// </summary>
        public Guid ProviderGuid { get; }

        /// <summary>
        /// Dictionary mapping event IDs to their column definitions.
        /// </summary>
        private readonly Dictionary<string, List<EtwManifestUserDataReader.UserDataColumn>> _eventSchema;

        /// <summary>
        /// Initializes a new instance of the EtwUserDataSchema class.
        /// </summary>
        public EtwUserDataSchema(Guid providerGuid,
            Dictionary<string, List<EtwManifestUserDataReader.UserDataColumn>> eventSchema)
        {
            ProviderGuid = providerGuid;
            _eventSchema = eventSchema ?? new Dictionary<string, List<EtwManifestUserDataReader.UserDataColumn>>();
        }

        /// <summary>
        /// Gets the event IDs defined in this schema.
        /// </summary>
        public IEnumerable<string> EventIds => _eventSchema.Keys;

        /// <summary>
        /// Gets the number of events defined in the schema.
        /// </summary>
        public int EventCount => _eventSchema.Count;

        /// <summary>
        /// Gets the column definitions for a specific event ID.
        /// </summary>
        /// <param name="eventId">The event ID to query</param>
        /// <param name="columns">The column definitions for the event if found</param>
        /// <returns>True if the event ID exists in the schema, false otherwise</returns>
        public bool TryGetEventColumns(string eventId, out List<EtwManifestUserDataReader.UserDataColumn> columns)
        {
            return _eventSchema.TryGetValue(eventId, out columns);
        }

        /// <summary>
        /// Gets the column definitions for a specific event ID.
        /// </summary>
        /// <param name="eventId">The event ID to query</param>
        /// <param name="columns">The column definitions for the event if found</param>
        /// <returns>True if the event ID exists in the schema, false otherwise</returns>
        public bool TryGetEventColumns(int eventId, out List<EtwManifestUserDataReader.UserDataColumn> columns)
        {
            return _eventSchema.TryGetValue(eventId.ToString(), out columns);
        }

        /// <summary>
        /// Gets the column definitions for a specific event ID.
        /// </summary>
        /// <param name="eventId">The event ID to query</param>
        /// <returns>The column definitions for the event, or null if not found</returns>
        public List<EtwManifestUserDataReader.UserDataColumn> GetEventColumns(string eventId)
        {
            if (_eventSchema.TryGetValue(eventId, out var columns))
            {
                return columns;
            }
            return null;
        }

        /// <summary>
        /// Gets the column definitions for a specific event ID.
        /// </summary>
        /// <param name="eventId">The event ID to query</param>
        /// <returns>The column definitions for the event, or null if not found</returns>
        public List<EtwManifestUserDataReader.UserDataColumn> GetEventColumns(int eventId)
        {
            return GetEventColumns(eventId.ToString());
        }

        /// <summary>
        /// Enumerates all event IDs and their column definitions.
        /// </summary>
        /// <returns>An enumerable of key-value pairs mapping event IDs to column definitions</returns>
        public IEnumerable<KeyValuePair<string, List<EtwManifestUserDataReader.UserDataColumn>>> EnumerateEvents()
        {
            foreach (var pair in _eventSchema)
            {
                yield return pair;
            }
        }
    }
}
