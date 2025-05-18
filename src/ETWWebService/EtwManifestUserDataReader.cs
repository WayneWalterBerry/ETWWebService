// <copyright file="EtwManifestUserDataReader.cs" company="Wayne Walter Berry">
// Copyright (c) Wayne Walter Berry. All rights reserved.
// </copyright>

namespace ETWWebService
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Data.Common;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text;
    using Microsoft.Diagnostics.Tracing;
    using Microsoft.Win32;

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

        /// <summary>
        /// Represents information about a property in an ETW event.
        /// This structure follows the Windows EVENT_PROPERTY_INFO layout and is part of the data
        /// returned by TdhGetManifestEventInformation.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_PROPERTY_INFO
        {
            /// <summary>
            /// Flags that specify characteristics of the property, such as whether it's a struct,
            /// has a length parameter, or has a count parameter.
            /// </summary>
            public PropertyFlags Flags;

            /// <summary>
            /// Offset from the beginning of the TRACE_EVENT_INFO buffer to the null-terminated 
            /// Unicode string that contains the property name.
            /// </summary>
            public uint NameOffset;

            /// <summary>
            /// The input type of the property as defined in the manifest.
            /// </summary>
            public EVENT_FIELD_TYPE InType;

            /// <summary>
            /// The output type of the property after any type conversion is performed.
            /// </summary>
            public EVENT_FIELD_TYPE OutType;

            /// <summary>
            /// Offset from the beginning of the TRACE_EVENT_INFO buffer to the null-terminated 
            /// Unicode string that contains the name of a value map. Only valid if the property
            /// has a value map; otherwise, this member is 0.
            /// </summary>
            public uint MapNameOffset;

            /// <summary>
            /// The number of elements in an array if the property is an array type,
            /// or the count of a variable-length property.
            /// </summary>
            public uint Count;

            /// <summary>
            /// The length of the property in bytes, if it's a fixed-length property.
            /// </summary>
            public uint Length;

            /// <summary>
            /// Reserved for future use.
            /// </summary>
            public uint Reserved;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct PROVIDER_EVENT_INFO
        {
            public uint NumberOfEvents;
            public uint Reserved;
            // Followed by an array of EVENT_DESCRIPTOR structures
        }

        #endregion


        /// <summary>
        /// Retrieves UserData schema from an ETW manifest by provider GUID.
        /// </summary>
        public EtwUserDataSchema GetUserDataSchema(Guid providerGuid)
        {
            var schemaByEventId = new Dictionary<string, List<UserDataColumn>>();

            // Get list of events in the provider
            int bufferSize = 0;
            int lResult = TdhEnumerateManifestProviderEvents(ref providerGuid, IntPtr.Zero, ref bufferSize);
            switch (lResult)
            {
                case 2:
                    // ERROR_FILE_NOT_FOUND
                    throw new FileNotFoundException($"Provider {providerGuid} not found.");
                default:
                    break;
            }

            if (bufferSize > 0)
            {
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    int result = TdhEnumerateManifestProviderEvents(ref providerGuid, buffer, ref bufferSize);
                    switch (result)
                    {
                        case 2:
                            // ERROR_INSUFFICIENT_BUFFER
                            throw new OutOfMemoryException("Buffer size insufficient for provider events.");
                        case 87:
                            // ERROR_INVALID_PARAMETER
                            throw new ArgumentException("Invalid parameters for TdhEnumerateManifestProviderEvents.");
                        default:
                            break;
                    }

                    // Parse the buffer to get event IDs
                    // The buffer contains a PROVIDER_EVENT_INFO structure followed by EVENT_DESCRIPTOR array
                    PROVIDER_EVENT_INFO providerEventInfo = Marshal.PtrToStructure<PROVIDER_EVENT_INFO>(buffer);
                    int eventCount = (int)providerEventInfo.NumberOfEvents;

                    // Move pointer past the PROVIDER_EVENT_INFO structure to the EVENT_DESCRIPTOR array
                    IntPtr eventDescriptorsPtr = IntPtr.Add(buffer, Marshal.SizeOf<PROVIDER_EVENT_INFO>());

                    // Process each event
                    for (int i = 0; i < eventCount; i++)
                    {
                        // Extract the EVENT_DESCRIPTOR for this event
                        IntPtr currentEventDescPtr = IntPtr.Add(eventDescriptorsPtr, i * Marshal.SizeOf<EVENT_DESCRIPTOR>());
                        EVENT_DESCRIPTOR eventDesc = Marshal.PtrToStructure<EVENT_DESCRIPTOR>(currentEventDescPtr);

                        // Get the event ID
                        ushort eventId = eventDesc.Id;

                        // Skip events with ID 0 (these are often metadata events)
                        if (eventId == 0)
                        {
                            continue;
                        }

                        // Get columns for this event
                        var columns = GetUserDataColumnsForEvent(providerGuid, eventId);

                        // Only add events that have valid columns
                        if (columns != null && columns.Count > 0)
                        {
                            schemaByEventId[eventId.ToString()] = columns;
                        }
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

            // Get the Buffer Size So the Buffer Can Be Allocated
            int result = TdhGetManifestEventInformation(provGuidPtr, eventDescPtr, IntPtr.Zero, ref bufferSize);
            switch (result)
            {
                case 122:
                    // ERROR_INSUFFICIENT_BUFFER
                    break;
                case 87:
                    // ERROR_INVALID_PARAMETER
                    throw new ArgumentException("Invalid parameters for TdhGetManifestEventInformation.");
                case 1168:
                    // ERROR_NOT_FOUND
                    throw new FileNotFoundException($"Manifest for {providerGuid} not found.");
                default:
                    throw new Win32Exception(result, "Failed to get manifest event information.");
            }

            if (bufferSize > 0)
            {
                // Allocate buffer for event info
                IntPtr eventInfoBuffer = Marshal.AllocHGlobal(bufferSize);

                try
                {
                    result = TdhGetManifestEventInformation(provGuidPtr, eventDescPtr, eventInfoBuffer, ref bufferSize);
                    if (result != 0)
                    {
                        throw new Win32Exception(result, $"Failed to get manifest event information : LRESULT: {result}.");
                    }

                    // Parse the TRACE_EVENT_INFO structure to get property info
                    TRACE_EVENT_INFO eventInfo = Marshal.PtrToStructure<TRACE_EVENT_INFO>(eventInfoBuffer);

                    // Value The Provider Guid In the eventInfo
                    if (eventInfo.ProviderGuid != providerGuid)
                    {
                        throw new InvalidOperationException("Provider GUID mismatch.");
                    }

                    // eventInfo is already defined as TRACE_EVENT_INFO
                    EVENT_DESCRIPTOR eventDescriptor = eventInfo.EventDescriptor;

                    if (eventDescriptor.Id != eventId)
                    {
                        throw new InvalidOperationException($"Event ID mismatch: expected {eventId}, got {eventDescriptor.Id}.");
                    }

                    // Get property count
                    uint propertyCount = eventInfo.PropertyCount;

                    if (propertyCount > 0)
                    {
                        int eventTraceInfoBaseSize = Marshal.SizeOf<TRACE_EVENT_INFO>();

                        // The TRACE_EVENT_INFO structure is followed by an array of EVENT_PROPERTY_INFO structures, but the structure is not always tightly
                        // packed due to alignment requirements.On 64 - bit systems, the actual start of the property array may be aligned to an 8 - byte
                        // boundary after the end of TRACE_EVENT_INFO.
                        long propertyArrayOffset = (eventTraceInfoBaseSize + 7) & ~7L;

                        IntPtr propertyArrayPtrStart = IntPtr.Add(eventInfoBuffer, (int)propertyArrayOffset);

                        // Parse each property
                        for (uint i = 0; i < propertyCount; i++)
                        {
                            UserDataColumn column = new UserDataColumn();

                            int eventPropertyInfoBaseSize = Marshal.SizeOf<EVENT_PROPERTY_INFO>();

                            // 24 is right, this works.  Don't ASK me why.
                            eventPropertyInfoBaseSize = 24;

                            int propertyOffset = (int)(i * eventPropertyInfoBaseSize);
                            IntPtr propertyInfoPtr = IntPtr.Add(propertyArrayPtrStart, propertyOffset);
                            EVENT_PROPERTY_INFO propInfo = Marshal.PtrToStructure<EVENT_PROPERTY_INFO>(propertyInfoPtr);

                            try
                            {
                                string name = GetStringFromOffset(eventInfoBuffer, propInfo.NameOffset);
                                var dataType = MapEtwTypeToNetType(propInfo.InType);

                                column.Name = name;
                                column.DataType = dataType;
                                column.Length = (int)propInfo.Length;
                                column.IsArray = (propInfo.Flags & PropertyFlags.ParamCount) == PropertyFlags.ParamCount;
                                column.ArrayCount = propInfo.Count;

                                // Check if it has a map (enum values)
                                if (propInfo.MapNameOffset != 0)
                                {
                                    string mapName = GetStringFromOffset(eventInfoBuffer, propInfo.MapNameOffset);
                                    column.EnumValues = GetEnumValuesFromMap(providerGuid, eventId, mapName);
                                }

                                column.Status = UserDataColumnStatus.Valid;
                            }
                            catch (Exception ex)
                            {
                                column.Status = UserDataColumnStatus.Invalid;

                                // Handle any exceptions that occur while processing properties
                                Console.WriteLine($"Error processing event id: {eventId} property {i}: {ex.Message}");
                            }

                            columns.Add(column);
                        }

                        if (columns.All(c => c.Status == UserDataColumnStatus.Valid))
                        {
                            Console.WriteLine($"Successfully processing event id: {eventId}: Properties: {propertyCount}: [{string.Join(",", columns.Select(c => c.Name))}]");
                        }
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
    }
}