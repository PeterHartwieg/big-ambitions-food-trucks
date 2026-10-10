namespace FoodTrucks.Core.Fleet
{
    // Whether the player counts as one of the people serving at a window (Peter, 10 October 2026). A truck is
    // served from inside: only the player standing at its hatch serves, and one who stepped out serves no more,
    // however close they stay. A cart is served from beside it: the player within workingDistance serves. Riding
    // in or pushing the vehicle, indoors or in a time skip, nobody does.
    public static class PlayerServing
    {
        public static bool Serves(string kind, bool atHatch, double? distanceMetres, double workingDistance)
        {
            if (kind == "truck") return atHatch;
            return distanceMetres.HasValue && distanceMetres.Value <= workingDistance;
        }
    }
}
