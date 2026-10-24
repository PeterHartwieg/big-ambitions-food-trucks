#nullable enable
using System;
using FoodTrucks.Core.Saves;

namespace FoodTrucks
{
    /// <summary>
    /// Optional, versioned read-only integration. Call on the game's main thread.
    /// This does not settle held takings, fire save events or change the save count.
    /// Null means the current session cannot supply a compatible snapshot.
    /// </summary>
    public static class BigCopilotSnapshot
    {
        public static string? CaptureV1(GameInstance instance)
        {
            if (!FoodTrucksSession.IsAttachedTo(instance) || !FoodTrucksSession.CanWrite)
                return null;
            return new SaveCodec().Serialize(FoodTrucksSession.CurrentState!);
        }

        public static bool IsCrewV1(GameInstance instance, string employeeId)
        {
            if (!FoodTrucksSession.IsAttachedTo(instance) || !FoodTrucksSession.CanWrite)
                throw new InvalidOperationException("Street Food session is unavailable");
            foreach (var vehicle in FoodTrucksSession.CurrentState!.Vehicles)
                if (vehicle.Crew.Contains(employeeId)) return true;
            return false;
        }
    }
}
