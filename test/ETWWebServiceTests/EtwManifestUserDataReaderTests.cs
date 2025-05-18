// <copyright file="EtwManifestUserDataReaderTests.cs" company="Wayne Walter Berry">
// Copyright (c) Wayne Walter Berry. All rights reserved.
// </copyright>

namespace ETWWebService.Tests
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;
    using System.Linq;

    [TestClass]
    public class EtwManifestUserDataReaderTests
    {
        [TestInitialize]
        public void TestSetup()
        {
            // Verify tdh.dll is available before tests run
            var dllPath = Path.Combine(Environment.SystemDirectory, "tdh.dll");
            Assert.IsTrue(File.Exists(dllPath), "tdh.dll not found");
        }

        [TestMethod]
        public void GetUserDataSchema_MicrosoftWindowsShellCore_ReturnsSchema()
        {
            Guid providerGuid = new Guid("30336ED4-E327-447C-9DE0-51B652C86108");
            GetAndVerifyUserDataSchema(providerGuid);
        }

        [TestMethod]
        public void GetUserDataSchema_MicrosoftWindowsWdiGuidProvider_ReturnsSchema()
        {
            Guid providerGuid = new Guid("C6C5265F-EAE8-4650-AAE4-9D48603D8510");
            GetAndVerifyUserDataSchema(providerGuid);
        }

        [TestMethod]
        public void GetUserDataSchema_WithInvalidGuid_ReturnsEmptySchema()
        {
            // Arrange
            var reader = new EtwManifestUserDataReader();
            Guid invalidGuid = Guid.NewGuid(); // Random GUID that almost certainly doesn't exist
            
            // Act
            EtwUserDataSchema schema = null;
            try
            {
                schema = reader.GetUserDataSchema(invalidGuid);
            }
            catch (Exception ex)
            {
                Assert.Fail($"GetUserDataSchema should not throw for invalid GUIDs: {ex.Message}");
            }
            
            // Assert
            Assert.IsNotNull(schema, "Schema should not be null even for invalid provider");
            Assert.AreEqual(invalidGuid, schema.ProviderGuid, "Provider GUID should match input");
            Assert.AreEqual(0, schema.EventCount, "Event count should be 0 for invalid provider");
            Assert.IsFalse(schema.EventIds.Any(), "There should be no event IDs for invalid provider");
        }

        private void GetAndVerifyUserDataSchema(Guid providerGuid)
        {
            // Arrange
            var reader = new EtwManifestUserDataReader();

            // Act
            EtwUserDataSchema schema = null;
            try
            {
                schema = reader.GetUserDataSchema(providerGuid);
            }
            catch (Exception ex)
            {
                Assert.Fail($"GetUserDataSchema threw an exception: {ex.Message}");
            }

            // Assert
            Assert.IsNotNull(schema, "Schema should not be null");
            Assert.AreEqual(providerGuid, schema.ProviderGuid, "Provider GUID should match input");

            // The ETW provider might have events or not depending on the system
            // So we'll check the structure but not expect specific values
            if (schema.EventCount > 0)
            {
                // Verify we can enumerate the events
                var events = schema.EnumerateEvents().ToList();
                Assert.IsTrue(events.Count > 0, "EnumerateEvents should return events");

                // Verify we can get event columns
                var firstEventId = schema.EventIds.First();
                var columns = schema.GetEventColumns(firstEventId);

                Assert.IsNotNull(columns, $"Columns for event {firstEventId} should not be null");

                // If we have columns, verify we can read their properties
                if (columns.Count > 0)
                {
                    var firstColumn = columns.First();
                    Assert.IsNotNull(firstColumn.Name, "Column name should not be null");
                    Assert.IsNotNull(firstColumn.DataType, "Column data type should not be null");
                }

                // Verify TryGetEventColumns works
                bool success = schema.TryGetEventColumns(firstEventId, out var outColumns);
                Assert.IsTrue(success, "TryGetEventColumns should return true for valid event ID");
                Assert.IsNotNull(outColumns, "TryGetEventColumns should return non-null columns");
                Assert.AreEqual(columns.Count, outColumns.Count, "Column counts should match");

                for (int i = 0; i < columns.Count; i++)
                {
                    Assert.AreEqual(UserDataColumnStatus.Valid, columns[i].Status, "Column Didn't Parse Correctly.");
                }
            }
        }
    }
}