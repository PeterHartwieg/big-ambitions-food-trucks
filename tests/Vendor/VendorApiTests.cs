using System.Linq;
using System.Reflection;
using FoodTrucks.Core.Vendor;
using FoodTrucks.ModApi;
using Xunit;
using static FoodTrucks.Core.Tests.Vendor.VendorTestHost;

namespace FoodTrucks.Core.Tests.Vendor;

// The public static API is one runtime for the process: these tests share it, so they never run in parallel.
[CollectionDefinition("VendorApi", DisableParallelization = true)]
public class VendorApiCollection
{
}

[Collection("VendorApi")]
public class VendorApiTests
{
    [Fact]
    public void WithoutStreetFoodRunningEveryCallIsOff()
    {
        VendorRuntime.Detach();
        Assert.Equal(1, VendorApi.ContractVersion);
        Assert.Equal(0, VendorApi.Revision);
        Assert.Equal(VendorApiStatus.Off, VendorApi.Read(new VendorApiVehicle[64], out var count));
        Assert.Equal(0, count);
        Assert.Equal(VendorApiStatus.Off, VendorApi.RequestOwner(Owner, "cart-a", true, out _));
        Assert.Equal(VendorApiStatus.Off, VendorApi.Renew(Owner, 1, 0, out _, out _));
        Assert.Equal(VendorApiStatus.Off, VendorApi.ReleaseAll(Owner));
        Assert.Equal(VendorApiStatus.Off, VendorApi.Sell(Owner, "cart-a", 1, "x", 1, 1, 0, out var sale, out var uncertain, out _));
        Assert.Null(sale);
        Assert.False(uncertain);
    }

    [Fact]
    public void TheStaticApiForwardsToTheAttachedService()
    {
        var host = new VendorTestHost { Now = At(3, 10, 5) };
        host.AddVehicle("cart-a");
        try
        {
            var service = VendorRuntime.Attach(host);
            Assert.False(service.Active);
            var revision = VendorApi.Revision;
            Assert.True(service.Active);
            Assert.Equal(VendorApiStatus.Ready, VendorApi.Renew(Owner, 3, 605, out var day, out var minute));
            Assert.Equal((3, 665), (day, minute));
            Assert.Equal(VendorApiStatus.Ready, VendorApi.RequestOwner(Owner, "cart-a", true, out _));
            var into = new VendorApiVehicle[64];
            Assert.Equal(VendorApiStatus.Ready, VendorApi.Read(into, out var count));
            Assert.Equal("cart-a", into.Take(count).Single().VehicleId);
            Assert.NotEqual(0, revision);
            Assert.Equal(VendorApiStatus.Ready, VendorApi.ReleaseAll(Owner));
        }
        finally { VendorRuntime.Detach(); }
    }

    // The members another mod binds by name (Persistent City plan E6, section 3.2): names, types and order of parameters.
    [Fact]
    public void TheContractMembersHaveTheirVersionOneShape()
    {
        var api = typeof(VendorApi);
        Assert.Equal("FoodTrucks.ModApi.VendorApi", api.FullName);
        Assert.True(api.IsAbstract && api.IsSealed);
        Assert.Equal(typeof(int), api.GetProperty("ContractVersion", BindingFlags.Public | BindingFlags.Static)!.PropertyType);
        Assert.Equal(typeof(long), api.GetProperty("Revision", BindingFlags.Public | BindingFlags.Static)!.PropertyType);

        string Shape(string name)
        {
            var method = api.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!;
            return method.ReturnType.Name + " " + string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
        }
        Assert.Equal("VendorApiStatus VendorApiVehicle[] into, Int32& count", Shape("Read"));
        Assert.Equal("VendorApiStatus String owner, String vehicleId, Boolean want, String& reason", Shape("RequestOwner"));
        Assert.Equal("VendorApiStatus String owner, Int32 nativeDay, Int32 nativeMinute, Int32& leaseUntilDay, Int32& leaseUntilMinute", Shape("Renew"));
        Assert.Equal("VendorApiStatus String owner", Shape("ReleaseAll"));
        Assert.Equal("VendorApiStatus String owner, String vehicleId, Int64 openingId, String item, Int64 priceCents, Int32 nativeDay, Int32 nativeMinute, " +
                     "VendorApiSale& sale, Boolean& uncertain, String& reason", Shape("Sell"));

        Assert.Equal(new[] { "Ready", "Off", "NotYet", "Refused", "Failed" }, System.Enum.GetNames(typeof(VendorApiStatus)));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, System.Enum.GetValues(typeof(VendorApiStatus)).Cast<VendorApiStatus>().Select(s => (int)s));

        Assert.Equal(new[]
        {
            "VehicleId String", "Kind String", "Open Boolean", "OpeningId Int64", "OpeningOwner String", "OpenedByCrew Boolean",
            "EventOrCatering Boolean", "OpenedDay Int32", "OpenedMinute Int32", "ClosesDay Int32", "ClosesMinute Int32",
            "X Single", "Y Single", "Z Single", "QueueX Single", "QueueY Single", "QueueZ Single", "QueueHeading Single",
            "MealsPerHour Int32", "MenuOverflow Boolean", "Items String[]", "PriceCents Int64[]", "Units Int32[]"
        }, Fields(typeof(VendorApiVehicle)));
        Assert.Equal(new[] { "BookedDay Int32", "Cents Int64", "UnitsBefore Int32", "UnitsAfter Int32", "Sequence Int64" }, Fields(typeof(VendorApiSale)));
        Assert.True(typeof(VendorApiVehicle).IsSealed && typeof(VendorApiSale).IsSealed);
    }

    private static string[] Fields(System.Type type) => type.GetFields(BindingFlags.Public | BindingFlags.Instance)
        .Where(f => f.IsInitOnly).OrderBy(f => f.MetadataToken).Select(f => f.Name + " " + f.FieldType.Name).ToArray();
}
