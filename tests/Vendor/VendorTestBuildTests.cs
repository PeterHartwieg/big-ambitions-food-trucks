using System.Linq;
using FoodTrucks.Core.Vendor;
using FoodTrucks.ModApi;
using Xunit;

namespace FoodTrucks.Core.Tests.Vendor;

public class VendorTestBuildTests
{
    private static readonly System.Reflection.Assembly Core = typeof(VendorService).Assembly;

    private static bool HasFaultType() => Core.GetTypes().Any(t => t.Name.Contains("Fault"));

#if FOODTRUCKS_TEST_CONTRACT2
    [Fact]
    public void TheContractTwoTestBuildReportsTwoAndHasNoFaultHook()
    {
        Assert.Equal(2, VendorApi.ContractVersion);
        Assert.Null(Core.GetType("FoodTrucks.VendorFaultHook"));
        Assert.False(HasFaultType());
    }
#elif FOODTRUCKS_TEST_FAULTS
    [Fact]
    public void TheFaultTestBuildHasTheHookAndStaysOnContractOne()
    {
        Assert.Equal(1, VendorApi.ContractVersion);
        Assert.NotNull(Core.GetType("FoodTrucks.VendorFaultHook"));
    }
#else
    [Fact]
    public void TheDefaultBuildIsContractOneWithNoFaultHook()
    {
        Assert.Equal(1, VendorApi.ContractVersion);
        Assert.Null(Core.GetType("FoodTrucks.VendorFaultHook"));
        Assert.False(HasFaultType());
    }
#endif
}
