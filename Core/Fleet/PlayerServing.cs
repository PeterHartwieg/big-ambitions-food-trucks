using System;

namespace FoodTrucks.Core.Fleet
{
    public static class PlayerServing
    {
        public static bool Serves(string kind, bool atHatch, double? distanceMetres, double workingDistance, bool atCart = true)
        {
            if (kind == "truck") return atHatch;
            return atCart && distanceMetres.HasValue && distanceMetres.Value <= workingDistance;
        }

        public static bool Serves<T>(string kind, bool playerPresent, bool atHatch, T target, Func<T, double?> distanceMetres, double workingDistance, bool atCart)
        {
            if (!playerPresent) return false;
            return Serves(kind, atHatch, distanceMetres(target), workingDistance, atCart);
        }
    }
}
