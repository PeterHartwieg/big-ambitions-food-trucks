namespace FoodTrucks.Core.Demand
{
    // In day order from 05:00. Late runs past midnight; Night covers the dead hours before Early.
    public enum Daypart
    {
        Early,
        Morning,
        Lunch,
        Afternoon,
        Evening,
        Late,
        Night
    }

    public enum Season
    {
        Winter,
        Spring,
        Summer,
        Autumn
    }
}
