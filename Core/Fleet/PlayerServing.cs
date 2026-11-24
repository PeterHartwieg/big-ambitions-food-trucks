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

        public static bool Serves(string kind, bool playerPresent, bool atHatch, Func<double?> distanceMetres, double workingDistance, bool atCart)
        {
            if (!playerPresent) return false;
            return Serves(kind, atHatch, distanceMetres(), workingDistance, atCart);
        }
    }
}
