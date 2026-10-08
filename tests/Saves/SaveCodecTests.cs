using System;
using System.Collections.Generic;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class SaveCodecTests
{
    private readonly SaveCodec codec = new();

    public static TheoryData<string, string> UnusableStates => new()
    {
        { "{\"schemaVersion\":1,\"licence\":null}", "Licence" },
        { "{\"schemaVersion\":1,\"vehicles\":[null]}", "Vehicles" },
        { "{\"schemaVersion\":1,\"dailySales\":[null]}", "DailySales" },
        { "{\"schemaVersion\":1,\"dailySales\":[{\"day\":1,\"revenueCents\":-1}]}", "RevenueCents" },
        { "{\"schemaVersion\":1,\"dailySales\":[{\"day\":1},{\"day\":1}]}", "duplicate Day 1" },
        { "{\"schemaVersion\":1,\"dailySales\":[{\"day\":1,\"revenueCents\":9223372036854775807},{\"day\":2,\"revenueCents\":1}]}", "overflow" },
        { "{\"schemaVersion\":1,\"writtenByModVersion\":\"99.0\"}", "WrittenByModVersion" },
        { "{\"schemaVersion\":1,\"writtenByModVersion\":\"garbage\"}", "WrittenByModVersion" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"lifetimeSales\":-1}]}", "LifetimeSales" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"lifetimeRevenueCents\":-1}]}", "LifetimeRevenueCents" },
        { "{\"schemaVersion\":1,\"saveCount\":-1}", "SaveCount" },
        { "{\"schemaVersion\":1,\"vehicles\":[{}]}", "VehicleId" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":null}]}", "VehicleId" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"\"}]}", "VehicleId" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart-1\"},{\"vehicleId\":\"cart-1\"}]}", "duplicate VehicleId" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart-1\",\"saleProgress\":-0.1}]}", "SaleProgress" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart-1\",\"saleProgress\":1}]}", "SaleProgress" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart-1\",\"saleProgress\":2}]}", "SaleProgress" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart-1\",\"saleProgress\":\"NaN\"}]}", "SaleProgress" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart-1\",\"saleProgress\":\"Infinity\"}]}", "SaleProgress" },
        { "{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart-1\",\"saleProgress\":\"-Infinity\"}]}", "SaleProgress" }
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void EmptyInputCreatesFreshState(string? json)
    {
        var result = codec.Load(json);

        Assert.Equal(LoadStatus.Fresh, result.Status);
        var state = Assert.IsType<SaveState>(result.State);
        Assert.Equal(1, state.SchemaVersion);
        Assert.Equal(string.Empty, state.WrittenByModVersion);
        Assert.Equal(0, state.SaveCount);
        Assert.False(state.Licence.HasVendorLicence);
        Assert.Equal(0, state.Licence.LicenceExpiresOnDay);
        Assert.Empty(state.Vehicles);
        Assert.Empty(state.ExtensionData);
        Assert.Empty(state.Licence.ExtensionData);
        Assert.Null(result.Error);
        Assert.Null(result.RawJson);
    }

    [Fact]
    public void FreshStatesDoNotShareMutableDefaults()
    {
        var first = codec.Load(null).State!;
        var second = codec.Load(null).State!;
        first.Licence.HasVendorLicence = true;
        first.Vehicles.Add(new VehicleState());

        Assert.False(second.Licence.HasVendorLicence);
        Assert.Empty(second.Vehicles);
        Assert.Equal(string.Empty, first.Vehicles[0].VehicleId);
        Assert.Empty(first.Vehicles[0].ExtensionData);
    }

    [Fact]
    public void RoundTripPreservesEveryField()
    {
        var state = new SaveState
        {
            WrittenByModVersion = "1.0.2",
            SaveCount = 7,
            Licence = new LicenceState { HasVendorLicence = true, LicenceExpiresOnDay = 60 },
            Vehicles = new List<VehicleState>
            {
                new() { VehicleId = "cart-1", IsOpen = true, SaleProgress = 0.75, LifetimeSales = 42, LifetimeRevenueCents = 5_000_000_123L },
                new() { VehicleId = "truck-2", IsOpen = false, LifetimeSales = 9, LifetimeRevenueCents = 1800 }
            }
        };

        var json = codec.Serialize(state);
        var result = codec.Load(json);

        Assert.Equal(LoadStatus.Loaded, result.Status);
        var loaded = Assert.IsType<SaveState>(result.State);
        Assert.Equal(state.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(state.WrittenByModVersion, loaded.WrittenByModVersion);
        Assert.Equal(state.SaveCount, loaded.SaveCount);
        Assert.Equal(state.Licence.HasVendorLicence, loaded.Licence.HasVendorLicence);
        Assert.Equal(state.Licence.LicenceExpiresOnDay, loaded.Licence.LicenceExpiresOnDay);
        Assert.Equal(state.Vehicles.Count, loaded.Vehicles.Count);
        for (var i = 0; i < state.Vehicles.Count; i++)
        {
            Assert.Equal(state.Vehicles[i].VehicleId, loaded.Vehicles[i].VehicleId);
            Assert.Equal(state.Vehicles[i].IsOpen, loaded.Vehicles[i].IsOpen);
            Assert.Equal(state.Vehicles[i].SaleProgress, loaded.Vehicles[i].SaleProgress);
            Assert.Equal(state.Vehicles[i].LifetimeSales, loaded.Vehicles[i].LifetimeSales);
            Assert.Equal(state.Vehicles[i].LifetimeRevenueCents, loaded.Vehicles[i].LifetimeRevenueCents);
        }
        Assert.Null(result.Error);
        Assert.Null(result.RawJson);
        Assert.Equal(json, codec.Serialize(loaded));
    }

    [Fact]
    public void SerializationUsesCurrentVersionAndCompactCamelCaseWithoutMutatingState()
    {
        var state = new SaveState { SchemaVersion = 0 };

        var json = codec.Serialize(state);
        var document = JObject.Parse(json);

        Assert.Equal(1, document["schemaVersion"]!.Value<int>());
        Assert.NotNull(document["writtenByModVersion"]);
        Assert.NotNull(document["saveCount"]);
        Assert.NotNull(document["licence"]!["hasVendorLicence"]);
        Assert.NotNull(document["licence"]!["licenceExpiresOnDay"]);
        Assert.NotNull(document["vehicles"]);
        Assert.Null(document["SchemaVersion"]);
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\r", json);
        Assert.Equal(0, state.SchemaVersion);
    }

    [Fact]
    public void UnknownPropertiesSurviveAtEveryLevel()
    {
        const string json = """
            {"schemaVersion":1,"FutureRoot":{"MixedCaseKey":[1,true,null]},
             "futureDate":"2026-10-07T12:34:56+02:00","$type":"future-state",
             "licence":{"FutureLicence":["a","b"]},
             "vehicles":[{"vehicleId":"cart-1","FutureVehicle":{"grade":"A"}},
                         {"vehicleId":"truck-2","futureNull":null}]}
            """;

        var result = codec.Load(json);

        Assert.Equal(LoadStatus.Loaded, result.Status);
        var saved = JObject.Parse(codec.Serialize(result.State!));
        var original = JObject.Parse(json);
        Assert.True(JToken.DeepEquals(original["FutureRoot"], saved["FutureRoot"]));
        Assert.Equal("2026-10-07T12:34:56+02:00", result.State!.ExtensionData["futureDate"].Value<string>());
        Assert.Contains("\"futureDate\":\"2026-10-07T12:34:56+02:00\"", codec.Serialize(result.State));
        Assert.Equal("future-state", saved["$type"]!.Value<string>());
        Assert.True(JToken.DeepEquals(original["licence"]!["FutureLicence"], saved["licence"]!["FutureLicence"]));
        Assert.True(JToken.DeepEquals(original["vehicles"]![0]!["FutureVehicle"], saved["vehicles"]![0]!["FutureVehicle"]));
        Assert.Equal(JTokenType.Null, saved["vehicles"]![1]!["futureNull"]!.Type);
    }

    [Fact]
    public void VersionOneBeforeSaleProgressStillLoadsWithZeroProgress()
    {
        const string json = """
            {"schemaVersion":1,"writtenByModVersion":"0.0.1-spike","saveCount":3,
             "vehicles":[{"vehicleId":"cart-1","isOpen":true,"lifetimeSales":7,"lifetimeRevenueCents":875}]}
            """;

        var result = codec.Load(json);

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(1, result.State!.SchemaVersion);
        var vehicle = Assert.Single(result.State.Vehicles);
        Assert.Equal(0, vehicle.SaleProgress);
        Assert.Equal(7, vehicle.LifetimeSales);
        Assert.Equal(875, vehicle.LifetimeRevenueCents);
    }

    [Fact]
    public void FutureVehicleFieldSurvivesAlongsideSaleProgress()
    {
        const string json = """
            {"schemaVersion":1,"vehicles":[{"vehicleId":"cart-1","saleProgress":0.5,
             "FutureServing":{"Mode":"fast","slots":[1,2]}}]}
            """;

        var result = codec.Load(json);

        Assert.Equal(LoadStatus.Loaded, result.Status);
        var saved = JObject.Parse(codec.Serialize(result.State!));
        Assert.Equal(0.5, saved["vehicles"]![0]!["saleProgress"]!.Value<double>());
        Assert.True(JToken.DeepEquals(JObject.Parse(json)["vehicles"]![0]!["FutureServing"],
            saved["vehicles"]![0]!["FutureServing"]));
        Assert.Equal(LoadStatus.Loaded, codec.Load(saved.ToString()).Status);
    }

    [Fact]
    public void VehicleIdUniquenessUsesOrdinalComparison()
    {
        var result = codec.Load("{\"schemaVersion\":1,\"vehicles\":[{\"vehicleId\":\"cart-1\"},{\"vehicleId\":\"Cart-1\"}]}");

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(2, result.State!.Vehicles.Count);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 8)]
    [InlineData(int.MaxValue - 1, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void SerializeForSaveWritesAndAppliesNextCountAndModVersion(int initialCount, int expectedCount)
    {
        var state = new SaveState { SchemaVersion = 0, SaveCount = initialCount, WrittenByModVersion = "0.0.1" };
        state.ExtensionData["FutureRoot"] = new JObject { ["key"] = true };
        state.Vehicles.Add(new VehicleState { VehicleId = "cart-1", SaleProgress = 0.5 });

        var json = codec.SerializeForSave(state, "0.1.0");
        var result = codec.Load(json);

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(expectedCount, state.SaveCount);
        Assert.Equal("0.1.0", state.WrittenByModVersion);
        Assert.Equal(expectedCount, result.State!.SaveCount);
        Assert.Equal("0.1.0", result.State.WrittenByModVersion);
        Assert.Equal(1, result.State.SchemaVersion);
        Assert.Equal(0, state.SchemaVersion);
        Assert.Equal(0.5, Assert.Single(result.State.Vehicles).SaleProgress);
        Assert.True(JToken.DeepEquals(state.ExtensionData["FutureRoot"], result.State.ExtensionData["FutureRoot"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("garbage")]
    public void SerializeForSaveRejectsInvalidModVersionWithoutChangingState(string? version)
    {
        var state = new SaveState { SaveCount = 7, WrittenByModVersion = "0.0.1" };
        var before = codec.Serialize(state);

        Assert.Throws<ArgumentException>(() => codec.SerializeForSave(state, version!));

        Assert.Equal(before, codec.Serialize(state));
    }

    [Fact]
    public void SerializeForSaveRejectsNullState()
    {
        Assert.Throws<ArgumentNullException>(() => codec.SerializeForSave(null!, "0.1.0"));
    }

    [Fact]
    public void FailedSerializationLeavesSaveCountAndModVersionUnchanged()
    {
        var state = new ThrowingState { SchemaVersion = 0, SaveCount = 7, WrittenByModVersion = "0.0.1" };

        Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => codec.SerializeForSave(state, "0.1.0"));

        Assert.Equal(7, state.SaveCount);
        Assert.Equal("0.0.1", state.WrittenByModVersion);
        Assert.Equal(0, state.SchemaVersion);
        Assert.Empty(state.Vehicles);
        Assert.Empty(state.ExtensionData);
    }

    private sealed class ThrowingState : SaveState
    {
        public string BrokenProperty => throw new InvalidOperationException("Cannot serialize this property.");
    }

    [Fact]
    public void NewerVersionKeepsOriginalJsonWithoutDeserializingState()
    {
        const string json = " {\"schemaVersion\":2,\"vehicles\":\"future format\"} \n";

        var result = codec.Load(json);

        Assert.Equal(LoadStatus.NewerVersion, result.Status);
        Assert.Null(result.State);
        Assert.Equal(json, result.RawJson);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"schemaVersion\":1")]
    [InlineData("{\"schemaVersion\":1} trailing")]
    [InlineData("{\"schemaVersion\":1}{\"schemaVersion\":1}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":1,\"vehicles\":42}")]
    [InlineData("{\"schemaVersion\":1,\"saveCount\":\"invalid\"}")]
    public void CorruptInputDoesNotThrowAndKeepsOriginalJson(string json)
    {
        LoadResult? result = null;

        Assert.Null(Record.Exception(() => result = codec.Load(json)));
        Assert.Equal(LoadStatus.Corrupt, result!.Status);
        Assert.Null(result.State);
        Assert.Equal(json, result.RawJson);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":null}")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    [InlineData("{\"schemaVersion\":1.0}")]
    [InlineData("{\"schemaVersion\":true}")]
    [InlineData("{\"schemaVersion\":-1}")]
    [InlineData("{\"schemaVersion\":2147483648}")]
    public void MissingOrInvalidSchemaVersionIsCorrupt(string json)
    {
        var result = codec.Load(json);

        Assert.Equal(LoadStatus.Corrupt, result.Status);
        Assert.Null(result.State);
        Assert.Equal(json, result.RawJson);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void NullOptionalPropertiesRetainNonNullDefaults()
    {
        var result = codec.Load("{\"schemaVersion\":1,\"writtenByModVersion\":null,\"vehicles\":null,\"dailySales\":null}");
        var state = Assert.IsType<SaveState>(result.State);
        Assert.Equal(string.Empty, state.WrittenByModVersion);
        Assert.NotNull(state.Licence);
        Assert.Empty(state.Vehicles);
        Assert.Empty(state.DailySales);
    }

    [Theory]
    [MemberData(nameof(UnusableStates))]
    public void UnusableStateIsCorruptWithClearError(string json, string error)
    {
        var result = codec.Load(json);

        Assert.Equal(LoadStatus.Corrupt, result.Status);
        Assert.Null(result.State);
        Assert.Equal(json, result.RawJson);
        Assert.Contains(error, result.Error);
    }

    [Theory]
    [MemberData(nameof(UnusableStates))]
    public void UnusableMigrationResultIsCorruptWithOriginalJson(string migratedJson, string error)
    {
        const string json = "{\"schemaVersion\":1}";
        var migratingCodec = new SaveCodec(2, new Dictionary<int, Func<JObject, JObject>>
        {
            [1] = _ => JObject.Parse(migratedJson)
        });

        var result = migratingCodec.Load(json);

        Assert.Equal(LoadStatus.Corrupt, result.Status);
        Assert.Null(result.State);
        Assert.Equal(json, result.RawJson);
        Assert.Contains(error, result.Error);
    }

    [Fact]
    public void NewerModVersionWithCurrentSchemaStillLoads()
    {
        var state = new SaveState { WrittenByModVersion = "99.0.0" };
        Assert.True(ModVersion.IsNewerThan(state.WrittenByModVersion, "0.0.1-spike"));

        var result = codec.Load(codec.Serialize(state));

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(SaveState.CurrentSchemaVersion, result.State!.SchemaVersion);
        Assert.Equal(state.WrittenByModVersion, result.State.WrittenByModVersion);
        Assert.Null(result.RawJson);
        Assert.Null(result.Error);
    }

    [Fact]
    public void EmptyWrittenByModVersionLoads()
    {
        var result = codec.Load("{\"schemaVersion\":1,\"writtenByModVersion\":\"\"}");

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(string.Empty, result.State!.WrittenByModVersion);
        Assert.Null(result.RawJson);
        Assert.Null(result.Error);
    }

    [Fact]
    public void DailySalesTotalAtTheLimitLoads()
    {
        var result = codec.Load("{\"schemaVersion\":1,\"dailySales\":[{\"day\":1,\"revenueCents\":9223372036854775806},{\"day\":2,\"revenueCents\":1}]}");

        Assert.Equal(LoadStatus.Loaded, result.Status);
        Assert.Equal(long.MaxValue, SalesLedger.SumCents(result.State, 1, 2));
    }

    [Fact]
    public void MigrationsRunInOrderFromVersionOneToThree()
    {
        var calls = new List<int>();
        var migratingCodec = new SaveCodec(3, new Dictionary<int, Func<JObject, JObject>>
        {
            [1] = document =>
            {
                calls.Add(document["schemaVersion"]!.Value<int>());
                document["saveCount"] = 4;
                return document;
            },
            [2] = document =>
            {
                calls.Add(document["schemaVersion"]!.Value<int>());
                Assert.Equal(4, document["saveCount"]!.Value<int>());
                var replacement = (JObject)document.DeepClone();
                replacement["writtenByModVersion"] = "3.0.0";
                return replacement;
            }
        });

        var result = migratingCodec.Load("{\"schemaVersion\":1,\"futureFlag\":true}");

        Assert.Equal(LoadStatus.Migrated, result.Status);
        Assert.Equal(new[] { 1, 2 }, calls);
        var state = Assert.IsType<SaveState>(result.State);
        Assert.Equal(3, state.SchemaVersion);
        Assert.Equal(4, state.SaveCount);
        Assert.Equal("3.0.0", state.WrittenByModVersion);
        Assert.True(state.ExtensionData["futureFlag"].Value<bool>());
        Assert.Equal(3, JObject.Parse(migratingCodec.Serialize(state))["schemaVersion"]!.Value<int>());
        Assert.Equal(LoadStatus.Loaded, migratingCodec.Load(migratingCodec.Serialize(state)).Status);
    }

    [Fact]
    public void MigrationGapIsCorruptWithClearError()
    {
        const string json = "{\"schemaVersion\":1}";
        var migratingCodec = new SaveCodec(3, new Dictionary<int, Func<JObject, JObject>>
        {
            [1] = document => document
        });

        var result = migratingCodec.Load(json);

        Assert.Equal(LoadStatus.Corrupt, result.Status);
        Assert.Null(result.State);
        Assert.Equal(json, result.RawJson);
        Assert.Contains("Missing migration from schema version 2 to 3", result.Error);
    }

    [Fact]
    public void VersionOneHasNoRegisteredMigrations()
    {
        var result = codec.Load("{\"schemaVersion\":0}");

        Assert.Equal(LoadStatus.Corrupt, result.Status);
        Assert.Contains("Missing migration from schema version 0 to 1", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailingMigrationReturnsCorruptWithoutThrowing(bool returnsNull)
    {
        const string json = "{\"schemaVersion\":1}";
        var migratingCodec = new SaveCodec(2, new Dictionary<int, Func<JObject, JObject>>
        {
            [1] = _ => returnsNull ? null! : throw new InvalidOperationException("Migration failed.")
        });

        var result = migratingCodec.Load(json);

        Assert.Equal(LoadStatus.Corrupt, result.Status);
        Assert.Null(result.State);
        Assert.Equal(json, result.RawJson);
        Assert.Contains(returnsNull ? "returned null" : "Migration failed", result.Error);
    }
}
