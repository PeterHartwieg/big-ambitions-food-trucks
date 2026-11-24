using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FoodTrucks.Core.Vendor;
using FoodTrucks.ModApi;
using Xunit;

namespace FoodTrucks.Core.Tests.Vendor;

public class VendorTestBuildTests
{
    private static readonly System.Reflection.Assembly Core = typeof(VendorService).Assembly;

    private static bool HasFaultType() => Core.GetTypes().Any(t => t.Name.StartsWith("VendorFault", StringComparison.Ordinal));

    [Fact]
    public void OnlyTheVendorApiSellPathCallsTheFaultHook()
    {
        var root = RepoRoot();
        var allowed = new[] { "Core/Vendor/VendorFaultHook.cs", "Core/Vendor/VendorService.cs", "unity/FoodTrucks/Scripts/TestBridgeCommands.cs", "unity/FoodTrucks/Scripts/TestBridgeVendorFault.cs" };
        var users = new[] { "Core", "unity" }
            .SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), "VendorFault|FOODTRUCKS_TEST_FAULTS"))
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal);
        Assert.Equal(allowed.OrderBy(f => f, StringComparer.Ordinal), users);

        var service = File.ReadAllText(Path.Combine(root, "Core/Vendor/VendorService.cs"));
        var call = Assert.Single(Regex.Matches(service, @"VendorFaultHook\.AfterLedger\("));
        var sellOne = Assert.Single(Regex.Matches(service, @"cents = host\.SellOne\("));
        Assert.True(sellOne.Index < call.Index);
        Assert.DoesNotMatch(@"[;}]\s*\}", service.Substring(sellOne.Index, call.Index - sellOne.Index));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FoodTrucks.sln"))) return dir.FullName;
        throw new InvalidOperationException("FoodTrucks.sln not found above " + AppContext.BaseDirectory);
    }

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
