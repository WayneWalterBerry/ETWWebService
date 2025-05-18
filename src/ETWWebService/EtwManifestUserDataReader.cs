using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Win32;

namespace ETWWebService
{
    /// <summary>
    /// Provides functionality to read UserData sections from registered ETW manifests
    /// and parse blob data into structured columns.
    /// </summary>
    public class EtwManifestUserDataReader
    {
        #region Native API Declarations

        [DllImport("tdh.dll")]
        private static extern int TdhGetManifestEventInformation(
            IntPtr ProviderGuid,
            IntPtr EventDescriptor,
            IntPtr EventInformation,
            ref int BufferSize);

        [DllImport("tdh.dll")]
        private static extern int TdhEnumerateManifestProviderEvents(
            ref Guid ProviderGuid,
            IntPtr Buffer,
            ref int BufferSize);

        [DllImport("tdh.dll")]
        private static extern int TdhGetEventInformation(
            [In] IntPtr EventRecord,
            [In] uint TdhContextCount,
            [In] IntPtr TdhContext,
            [In, Out] IntPtr EventInformation,
            [In, Out] ref int BufferSize);

        [DllImport("tdh.dll")]
        private static extern int TdhGetEventMapInformation(
            [In] IntPtr EventRecord,
            [In] string MapName,
            IntPtr EventMapInfo,
            ref int BufferSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_PROPERTY_INFO
        {
            public PropertyFlags Flags;
            public uint NameOffset;
            public EVENT_FIELD_TYPE InType;
            public EVENT_FIELD_TYPE OutType;
            public uint MapNameOffset;
            public uint Count;
            public uint Length;
            public uint Reserved;
        }

        [Flags]
        private enum PropertyFlags : uint
        {
            None = 0,
            Struct = 0x1,
            ParamLength = 0x2,
            ParamCount = 0x4,
            WBEMXMLFragment = 0x8,
            ParamFixedLength = 0x10,
            ParamFixedCount = 0x20,
            HasTags = 0x40,
            HasCustomSchema = 0x80
        }

        private enum EVENT_FIELD_TYPE : ushort
        {
            NoTypeInfo = 0,
            UnicodeString = 1,
            AnsiString = 2,
            Int8 = 3,
            UInt8 = 4,
            Int16 = 5,
            UInt16 = 6,
            Int32 = 7,
            UInt32 = 8,
            Int64 = 9,
            UInt64 = 10,
            Float = 11,
            Double = 12,
            Boolean = 13,
            Binary = 14,
            GUID = 15,
            PointerType = 16,
            SizeT = 17,
            FileTime = 18,
            SystemTime = 19,
            SID = 20,
            HexInt32 = 21,
            HexInt64 = 22,
            // etc.
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TRACE_EVENT_INFO
        {
            public Guid ProviderGuid;
            public Guid EventGuid;
            public EVENT_DESCRIPTOR EventDescriptor;
            public uint DecodingSource;
            public uint ProviderNameOffset;
            public uint LevelNameOffset;
            public uint ChannelNameOffset;
            public uint KeywordsNameOffset;
            public uint TaskNameOffset;
            public uint OpcodeNameOffset;
            public uint EventMessageOffset;
            public uint ProviderMessageOffset;
            public uint BinaryXMLOffset;
            public uint BinaryXMLSize;
            public uint EventNameOffset;
            public uint ActivityIDNameOffset;
            public uint RelatedActivityIDNameOffset;
            public uint PropertyCount;
            public uint TopLevelPropertyCount;
            // Followed by the property info array and name data
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_DESCRIPTOR
        {
            public ushort Id;
            public byte Version;
            public byte Channel;
            public byte Level;
            public byte Opcode;
            public ushort Task;
            public ulong Keyword;
        }

        #endregion

        /// <summary>
        /// Represents a column in the UserData section.
        /// </summary>
        public class UserDataColumn
        {
            public string Name { get; set; }
            public Type DataType { get; set; }
            public int Length { get; set; }
            public bool IsArray { get; set; }
            public uint ArrayCount { get; set; }
            public Dictionary<string, string> EnumValues { get; set; }

            public UserDataColumn()
            {
                EnumValues = new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Retrieves UserData schema from an ETW manifest by provider GUID.
        /// </summary>
        public EtwUserDataSchema GetUserDataSchema(Guid providerGuid)
        {
            var schemaByEventId = new Dictionary<string, List<UserDataColumn>>();

            // Get list of events in the provider
            int bufferSize = 0;
            TdhEnumerateManifestProviderEvents(ref providerGuid, IntPtr.Zero, ref bufferSize);

            if (bufferSize > 0)
            {
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    int result = TdhEnumerateManifestProviderEvents(ref providerGuid, buffer, ref bufferSize);
                    if (result == 0) // SUCCESS
                    {
                        // Parse the buffer to get event IDs
                        // For each event ID, get event schema
                        // This is simplified - actual implementation needs to parse the buffer structure
                        // For demonstration, we'll just assume we have event IDs

                        // Example event ID
                        ushort eventId = 1; // This should come from parsing the buffer

                        // Get columns for this event
                        var columns = GetUserDataColumnsForEvent(providerGuid, eventId);
                        schemaByEventId.Add(eventId.ToString(), columns);
                    }
                    else
                    {
                        throw new Win32Exception(result, "Failed to enumerate provider events.");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return new EtwUserDataSchema(providerGuid, schemaByEventId);
        }

        /// <summary>
        /// Gets columns for a specific event in the provider.
        /// </summary>
        private List<UserDataColumn> GetUserDataColumnsForEvent(Guid providerGuid, ushort eventId)
        {
            var columns = new List<UserDataColumn>();

            // Create EVENT_DESCRIPTOR for the specified event ID
            var eventDesc = new EVENT_DESCRIPTOR
            {
                Id = eventId,
                Version = 0, // Use appropriate version
                // Other fields can be 0 for manifest lookup
            };

            // Get event info buffer size
            int bufferSize = 0;
            IntPtr provGuidPtr = GCHandle.Alloc(providerGuid, GCHandleType.Pinned).AddrOfPinnedObject();
            IntPtr eventDescPtr = GCHandle.Alloc(eventDesc, GCHandleType.Pinned).AddrOfPinnedObject();

            int result = TdhGetManifestEventInformation(provGuidPtr, eventDescPtr, IntPtr.Zero, ref bufferSize);

            if (bufferSize > 0)
            {
                IntPtr eventInfoBuffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    result = TdhGetManifestEventInformation(provGuidPtr, eventDescPtr, eventInfoBuffer, ref bufferSize);
                    if (result == 0) // SUCCESS
                    {
                        // Parse the TRACE_EVENT_INFO structure to get property info
                        var eventInfo = Marshal.PtrToStructure<TRACE_EVENT_INFO>(eventInfoBuffer);

                        // Get property count
                        uint propertyCount = eventInfo.PropertyCount;
                        IntPtr propertyArrayPtr = IntPtr.Add(eventInfoBuffer, Marshal.SizeOf<TRACE_EVENT_INFO>());

                        // Parse each property
                        for (uint i = 0; i < propertyCount; i++)
                        {
                            IntPtr propertyInfoPtr = IntPtr.Add(propertyArrayPtr, (int)(i * Marshal.SizeOf<EVENT_PROPERTY_INFO>()));
                            var propInfo = Marshal.PtrToStructure<EVENT_PROPERTY_INFO>(propertyInfoPtr);

                            var column = new UserDataColumn
                            {
                                Name = GetStringFromOffset(eventInfoBuffer, propInfo.NameOffset),
                                DataType = MapEtwTypeToNetType(propInfo.InType),
                                Length = (int)propInfo.Length,
                                IsArray = (propInfo.Flags & PropertyFlags.ParamCount) == PropertyFlags.ParamCount,
                                ArrayCount = propInfo.Count
                            };

                            // Check if it has a map (enum values)
                            if (propInfo.MapNameOffset != 0)
                            {
                                string mapName = GetStringFromOffset(eventInfoBuffer, propInfo.MapNameOffset);
                                column.EnumValues = GetEnumValuesFromMap(providerGuid, eventId, mapName);
                            }

                            columns.Add(column);
                        }
                    }
                    else
                    {
                        throw new Win32Exception(result, "Failed to get manifest event information.");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(eventInfoBuffer);
                }
            }

            return columns;
        }

        /// <summary>
        /// Parses a blob of UserData according to the schema.
        /// </summary>
        public Dictionary<string, object> ParseUserDataBlob(byte[] blobData, List<UserDataColumn> columns)
        {
            Dictionary<string, object> parsedData = new Dictionary<string, object>();
            int position = 0;

            foreach (var column in columns)
            {
                if (column.IsArray)
                {
                    // Handle array type
                    var arrayValues = new List<object>();
                    for (int i = 0; i < column.ArrayCount; i++)
                    {
                        object value = ReadValueFromBlob(blobData, ref position, column.DataType, column.Length);
                        arrayValues.Add(value);
                    }
                    parsedData[column.Name] = arrayValues;
                }
                else
                {
                    // Handle scalar type
                    object value = ReadValueFromBlob(blobData, ref position, column.DataType, column.Length);

                    // If it's an enum, look up the friendly name
                    if (column.EnumValues.Count > 0 && value != null)
                    {
                        string key = value.ToString();
                        if (column.EnumValues.ContainsKey(key))
                        {
                            parsedData[column.Name] = $"{value} ({column.EnumValues[key]})";
                            continue;
                        }
                    }

                    parsedData[column.Name] = value;
                }
            }

            return parsedData;
        }

        private object ReadValueFromBlob(byte[] blobData, ref int position, Type dataType, int length)
        {
            if (position >= blobData.Length)
                return null;

            object value = null;

            if (dataType == typeof(string))
            {
                // String handling - could be null terminated or fixed length
                if (length > 0)
                {
                    // Fixed length string
                    int actualLength = Math.Min(length, blobData.Length - position);
                    value = Encoding.Unicode.GetString(blobData, position, actualLength).TrimEnd('\0');
                    position += actualLength;
                }
                else
                {
                    // Null-terminated string
                    int endPos = position;
                    while (endPos < blobData.Length && !(blobData[endPos] == 0 && blobData[endPos + 1] == 0))
                    {
                        endPos += 2;
                    }

                    int strLength = endPos - position;
                    value = Encoding.Unicode.GetString(blobData, position, strLength);
                    position = endPos + 2; // Skip null terminator
                }
            }
            else if (dataType == typeof(byte))
            {
                value = blobData[position];
                position += 1;
            }
            else if (dataType == typeof(short))
            {
                value = BitConverter.ToInt16(blobData, position);
                position += 2;
            }
            else if (dataType == typeof(ushort))
            {
                value = BitConverter.ToUInt16(blobData, position);
                position += 2;
            }
            else if (dataType == typeof(int))
            {
                value = BitConverter.ToInt32(blobData, position);
                position += 4;
            }
            else if (dataType == typeof(uint))
            {
                value = BitConverter.ToUInt32(blobData, position);
                position += 4;
            }
            else if (dataType == typeof(long))
            {
                value = BitConverter.ToInt64(blobData, position);
                position += 8;
            }
            else if (dataType == typeof(ulong))
            {
                value = BitConverter.ToUInt64(blobData, position);
                position += 8;
            }
            else if (dataType == typeof(float))
            {
                value = BitConverter.ToSingle(blobData, position);
                position += 4;
            }
            else if (dataType == typeof(double))
            {
                value = BitConverter.ToDouble(blobData, position);
                position += 8;
            }
            else if (dataType == typeof(bool))
            {
                value = BitConverter.ToBoolean(blobData, position);
                position += 1;
            }
            else if (dataType == typeof(Guid))
            {
                byte[] guidBytes = new byte[16];
                Array.Copy(blobData, position, guidBytes, 0, 16);
                value = new Guid(guidBytes);
                position += 16;
            }
            else if (dataType == typeof(byte[]))
            {
                if (length > 0)
                {
                    byte[] buffer = new byte[length];
                    Array.Copy(blobData, position, buffer, 0, Math.Min(length, blobData.Length - position));
                    value = buffer;
                    position += length;
                }
            }
            else
            {
                // Unknown type - skip based on length or default size
                int skipBytes = length > 0 ? length : 4;
                position += skipBytes;
            }

            return value;
        }

        private string GetStringFromOffset(IntPtr buffer, uint offset)
        {
            if (offset == 0)
                return string.Empty;

            IntPtr stringPtr = IntPtr.Add(buffer, (int)offset);
            return Marshal.PtrToStringUni(stringPtr);
        }

        private Type MapEtwTypeToNetType(EVENT_FIELD_TYPE etwType)
        {
            switch (etwType)
            {
                case EVENT_FIELD_TYPE.UnicodeString:
                case EVENT_FIELD_TYPE.AnsiString:
                    return typeof(string);
                case EVENT_FIELD_TYPE.Int8:
                    return typeof(sbyte);
                case EVENT_FIELD_TYPE.UInt8:
                    return typeof(byte);
                case EVENT_FIELD_TYPE.Int16:
                    return typeof(short);
                case EVENT_FIELD_TYPE.UInt16:
                    return typeof(ushort);
                case EVENT_FIELD_TYPE.Int32:
                case EVENT_FIELD_TYPE.HexInt32:
                    return typeof(int);
                case EVENT_FIELD_TYPE.UInt32:
                    return typeof(uint);
                case EVENT_FIELD_TYPE.Int64:
                case EVENT_FIELD_TYPE.HexInt64:
                    return typeof(long);
                case EVENT_FIELD_TYPE.UInt64:
                    return typeof(ulong);
                case EVENT_FIELD_TYPE.Float:
                    return typeof(float);
                case EVENT_FIELD_TYPE.Double:
                    return typeof(double);
                case EVENT_FIELD_TYPE.Boolean:
                    return typeof(bool);
                case EVENT_FIELD_TYPE.Binary:
                    return typeof(byte[]);
                case EVENT_FIELD_TYPE.GUID:
                    return typeof(Guid);
                case EVENT_FIELD_TYPE.FileTime:
                    return typeof(DateTime);
                case EVENT_FIELD_TYPE.SystemTime:
                    return typeof(DateTime);
                case EVENT_FIELD_TYPE.SID:
                    return typeof(byte[]);
                default:
                    return typeof(object);
            }
        }

        private Dictionary<string, string> GetEnumValuesFromMap(Guid providerGuid, ushort eventId, string mapName)
        {
            var enumValues = new Dictionary<string, string>();

            // This would normally involve calling TdhGetEventMapInformation
            // For brevity, implementing a simplified version

            return enumValues;
        }

        /// <summary>
        /// Parses the UserData blob from a TraceEvent and returns the values as string representations.
        /// </summary>
        /// <param name="traceEvent">The TraceEvent containing UserData to parse</param>
        /// <returns>Dictionary of field names and their string representation values</returns>
        public Dictionary<string, string> ParseTraceEventUserData(TraceEvent traceEvent)
        {
            // Get provider GUID
            Guid providerGuid = traceEvent.ProviderGuid;

            // Get event ID
            ushort eventId = (ushort)traceEvent.ID;

            // Get schema for this specific event
            var etwUserDataSchema = GetUserDataSchema(providerGuid);

            return ParseTraceEventUserData(etwUserDataSchema, traceEvent);
        }

        /// <summary>
        /// Parses the UserData blob from a TraceEvent and returns the values as string representations.
        /// </summary>
        /// <param name="traceEvent">The TraceEvent containing UserData to parse</param>
        /// <returns>Dictionary of field names and their string representation values</returns>
        public Dictionary<string, string> ParseTraceEventUserData(EtwUserDataSchema etwUserDataSchema, TraceEvent traceEvent)
        {
            if (traceEvent == null)
                throw new ArgumentNullException(nameof(traceEvent));

            Dictionary<string, string> result = new Dictionary<string, string>();

            try
            {
                // Get provider GUID
                Guid providerGuid = traceEvent.ProviderGuid;

                // Get event ID
                ushort eventId = (ushort)traceEvent.ID;

                // Get schema for this specific event
                List<UserDataColumn> columns = null;

                if (!etwUserDataSchema.TryGetEventColumns(eventId, out columns))
                {
                    // Try to get columns directly if schema lookup failed
                    columns = GetUserDataColumnsForEvent(providerGuid, eventId);
                    if (columns == null || columns.Count == 0)
                    {
                        // Fallback: if we can't get schema, use payload names directly
                        for (int i = 0; i < traceEvent.PayloadNames.Length; i++)
                        {
                            string name = traceEvent.PayloadNames[i];
                            object value = traceEvent.PayloadValue(i);
                            result[name] = value?.ToString() ?? "(null)";
                        }
                        return result;
                    }
                }

                // Get raw event data
                byte[] userData = traceEvent.EventData();
                if (userData == null || userData.Length == 0)
                {
                    // If we can't get raw data, use payload values directly
                    for (int i = 0; i < traceEvent.PayloadNames.Length; i++)
                    {
                        string name = traceEvent.PayloadNames[i];
                        object value = traceEvent.PayloadValue(i);
                        result[name] = value?.ToString() ?? "(null)";
                    }
                    return result;
                }

                // Parse the blob data using our existing method
                Dictionary<string, object> parsedData = ParseUserDataBlob(userData, columns);

                // Convert to string representations
                foreach (var item in parsedData)
                {
                    if (item.Value == null)
                    {
                        result[item.Key] = "(null)";
                    }
                    else if (item.Value is byte[])
                    {
                        // Format binary data as hex string
                        byte[] bytes = (byte[])item.Value;
                        StringBuilder hex = new StringBuilder(bytes.Length * 2);
                        foreach (byte b in bytes)
                            hex.AppendFormat("{0:X2}", b);
                        result[item.Key] = hex.ToString();
                    }
                    else if (item.Value is IEnumerable<object> enumerable)
                    {
                        // Handle collections
                        result[item.Key] = string.Join(", ", enumerable.Select(x => x?.ToString() ?? "(null)"));
                    }
                    else
                    {
                        result[item.Key] = item.Value.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't throw - return what we can from payload
                result["__ParseError"] = ex.Message;

                // Fallback to payload data
                for (int i = 0; i < traceEvent.PayloadNames.Length; i++)
                {
                    string name = traceEvent.PayloadNames[i];
                    object value = traceEvent.PayloadValue(i);
                    result[name] = value?.ToString() ?? "(null)";
                }
            }

            return result;
        }

        /// <summary>
        /// Example usage method that demonstrates how to use the class.
        /// </summary>
        public void ExampleUsage()
        {
            try
            {
                // Or by provider GUID
                Guid providerGuid = new Guid("22FB2CD6-0E7B-422B-A0C7-2FAD1FD0E716"); // Example GUID
                var schema = GetUserDataSchema(providerGuid);

                // For each event in the schema
                foreach (var eventEntry in schema.EnumerateEvents())
                {
                    Console.WriteLine($"Event ID: {eventEntry.Key}");
                    Console.WriteLine("Columns:");

                    foreach (var column in eventEntry.Value)
                    {
                        Console.WriteLine($"  - {column.Name} ({column.DataType.Name}){(column.IsArray ? "[]" : "")}");
                    }

                    Console.WriteLine();
                }

                // Example parsing of blob data
                // This would come from actual ETW event data
                byte[] sampleBlobData = new byte[100]; // Simulated blob data

                var firstEventColumns = schema.GetEventColumns(schema.EventIds.First());
                var parsedData = ParseUserDataBlob(sampleBlobData, firstEventColumns);
                foreach (var item in parsedData)
                {
                    Console.WriteLine($"{item.Key}: {item.Value}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}