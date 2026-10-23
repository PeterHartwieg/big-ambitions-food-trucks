// Optional socket-free xUnit host for the restricted local sandbox. Not compiled into normal tests.
#if CREW_DRIVE_RUNNER
using System;
using System.Threading;
using Xunit.Runners;

namespace FoodTrucks.Core.Tests;

internal static class CrewDrivingRunner
{
    private static int Main(string[] args)
    {
        using var done = new ManualResetEventSlim();
        using var runner = AssemblyRunner.WithoutAppDomain(args[0]);
        var all = args.Length > 1 && args[1] == "--all";
        if (!all) runner.TestCaseFilter = test => test.TestMethod.TestClass.Class.Name.StartsWith("FoodTrucks.Core.Tests.CrewDriving", StringComparison.Ordinal);
        var result = 1;
        var errors = 0;
        runner.OnTestFailed = info => Console.Error.WriteLine($"FAIL {info.TestDisplayName}: {info.ExceptionMessage}");
        runner.OnErrorMessage = info => { Interlocked.Increment(ref errors); Console.Error.WriteLine(info.ExceptionMessage); };
        runner.OnExecutionComplete = info =>
        {
            Console.WriteLine($"{(all ? "Core suite" : "CrewDriving")}: {info.TotalTests} tests, {info.TestsFailed} failed, {info.TestsSkipped} skipped.");
            result = info.TotalTests > 0 && info.TestsFailed == 0 && (all || info.TestsSkipped == 0) && errors == 0 ? 0 : 1;
            done.Set();
        };
        runner.Start();
        if (!done.Wait(TimeSpan.FromSeconds(55))) return 2;
        while (runner.Status != AssemblyRunnerStatus.Idle) Thread.Yield();
        return result;
    }
}
#endif
