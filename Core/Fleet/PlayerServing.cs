namespace FoodTrucks.Core.Fleet
{
    public static class PlayerServing
    {
        public static bool Serves(string kind, bool atHatch, double? distanceMetres, double workingDistance, bool atCart = true)
        {
            if (kind == "truck") return atHatch;
            return atCart && distanceMetres.HasValue && distanceMetres.Value <= workingDistance;
        }
    }
}
