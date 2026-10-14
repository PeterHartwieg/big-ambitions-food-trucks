namespace FoodTrucks.Balance;

public enum RestockMode
{
    // Push the cart to the nearest wholesaler, buy, load, push back (the design's restock).
    Wholesaler,
    // Stand at the cart with the rest of a box in hand and reload it on the spot: no travel. The current
    // loading rule allows this if the player may hold a box while the cart sells.
    BoxInHand,
    // No restock during the day: a crew loads in the commissary's bay before it leaves and sells until everything is
    // out (Core CrewRestock: only in the bay of the commissary where the vehicle rents its space).
    None
}

public sealed class Stay
{
    public Spot Spot = null!;
    public int From, To;
    public int Restocks;
    public int RestockMinutes;
    public StopResult Result = new();
    // Stock left when the stay ends.
    public Dictionary<string, double> Left = new();
}

public sealed class DayResult
{
    public List<Stay> Stays { get; } = new();
    public int MoveMinutes;
    public StopResult Total { get; } = new();
    public int Start => Stays.Count > 0 ? Stays[0].From : 0;
    public int End => Stays.Count > 0 ? Stays[^1].To : 0;
    public double Hours => (End - Start) / 60.0;
    public int Refills;
    public int Restocks => Stays.Sum(s => s.Restocks) + Refills;
}

public sealed class Planner
{
    private readonly Cart cart;
    private readonly City city;
    private readonly Travel travel;
    public RestockMode Mode { get; init; } = RestockMode.Wholesaler;
    // Where a Wholesaler-mode trip goes: the city's wholesalers when null, or the commissaries.
    public IReadOnlyList<Building>? Depots { get; init; }

    public Planner(Cart cart, City city, Travel travel)
    {
        this.cart = cart; this.city = city; this.travel = travel;
    }

    // Sell at one spot from `from` to `to`, restocking whenever an item sells out (its piece is empty) and there is
    // time to come back and sell for at least half an hour.
    private readonly Dictionary<(Spot, int, int, Conditions), Stay> cache = new();

    // SellAt with a full cart, remembered: route searches ask for the same pitch and arrival time often.
    public Stay SellFull(Spot spot, int from, int to, Conditions c)
    {
        if (!cache.TryGetValue((spot, from, to, c), out var stay)) cache[(spot, from, to, c)] = stay = SellAt(spot, from, to, c);
        return stay;
    }

    public Stay SellAt(Spot spot, int from, int to, Conditions c, Dictionary<string, double>? stock = null)
    {
        var stay = new Stay { Spot = spot, From = from, To = to };
        stock ??= Selling.Load(cart, spot, from, to, c);
        var t = from;
        if (Mode == RestockMode.None)
        {
            stay.Result.Add(Selling.Run(cart, spot, from, to, c, stock));
            stay.Left = new Dictionary<string, double>(stock);
            return stay;
        }
        while (t < to)
        {
            var r = Selling.Run(cart, spot, t, to, c, stock, untilOneOut: true);
            stay.Result.Add(r);
            if (r.SoldOutAt == null) break;
            var trip = Mode == RestockMode.BoxInHand ? 2 : travel.Restock(city, spot, spot, Depots).Minutes;
            if (trip == int.MaxValue || r.SoldOutAt.Value + trip + 30 > to)
            {
                // No time for a trip: the other pieces keep selling what they hold until closing.
                stay.Result.Add(Selling.Run(cart, spot, r.SoldOutAt.Value, to, c, stock));
                break;
            }
            stay.Restocks++;
            stay.RestockMinutes += trip;
            t = r.SoldOutAt.Value + trip;
            // As Core's Restock: what is on board stays, each piece takes only what fits in its room.
            stock = Selling.TopUp(cart, spot, t, to, c, stock);
        }
        stay.Left = new Dictionary<string, double>(stock);
        return stay;
    }

    public DayResult SingleSpot(Spot spot, int from, int to, Conditions c)
    {
        var day = new DayResult();
        var stay = SellAt(spot, from, to, c);
        day.Stays.Add(stay);
        day.Total.Add(stay.Result);
        return day;
    }

    // Pitches in order, each with a window. The cart arrives full at the first. Between pitches it keeps
    // its stock and goes straight on when every piece still holds at least half its room; otherwise it goes
    // through the nearest wholesaler and tops every piece up (for pieces with one food that is a full load).
    // Staying at the same pitch costs no walk. A walk that runs into the next window starts that pitch late.
    public DayResult? Route(IReadOnlyList<Spot> spots, IReadOnlyList<(int From, int To)> windows, Conditions c)
    {
        var day = new DayResult();
        var t = windows[0].From;
        Dictionary<string, double>? stock = null;
        for (var i = 0; i < spots.Count; i++)
        {
            var full = true;
            if (i > 0)
            {
                var move = 0;
                if (cart.PieceLoads(stock!).Values.All(l => l >= cart.PerPiece / 2.0))
                {
                    full = false;
                    if (spots[i] != spots[i - 1]) move = travel.Minutes(spots[i - 1].Distance(spots[i]));
                }
                else
                {
                    move = Mode == RestockMode.BoxInHand
                        ? travel.Minutes(spots[i - 1].Distance(spots[i]))
                        : travel.Restock(city, spots[i - 1], spots[i], Depots).Minutes;
                    if (move == int.MaxValue) return null;
                    day.Refills++;
                }
                day.MoveMinutes += move;
                t += move;
            }
            // Arrival rounded up to the 5-minute selling step.
            var from = Math.Max((t + 4) / 5 * 5, windows[i].From);
            if (from >= windows[i].To - 30) return null;
            Stay stay;
            // A refill tops up what is left; only a shared piece's split can differ from a full load.
            if (full && i > 0 && cart.HasSharedPiece)
                stay = SellAt(spots[i], from, windows[i].To, c, Selling.TopUp(cart, spots[i], from, windows[i].To, c, stock!));
            else if (full)
                stay = SellFull(spots[i], from, windows[i].To, c);
            else
                stay = SellAt(spots[i], from, windows[i].To, c, new Dictionary<string, double>(stock!));
            // What the stay ended with, after any restock inside it: the next pitch judges this, never the stock it began with.
            stock = new Dictionary<string, double>(stay.Left);
            day.Stays.Add(stay);
            day.Total.Add(stay.Result);
            t = windows[i].To;
        }
        return day;
    }

    // The best route over the given windows: each window's best `top` spots on their own, then every
    // combination within one map region. A pitch may repeat (staying put and restocking in between).
    public DayResult? BestRoute(IReadOnlyList<Spot> candidates, IReadOnlyList<(int From, int To)> windows, Conditions c, int top = 20,
        IReadOnlyList<Spot>? alwaysTry = null)
    {
        var shortlists = windows.Select(w => candidates
            .Select(s => (s, p: SellFull(s, w.From, w.To, c).Result.Profit))
            .OrderByDescending(x => x.p).Take(top).Select(x => x.s)
            .Concat(alwaysTry ?? Array.Empty<Spot>()).Distinct().ToList()).ToList();
        DayResult? best = null;
        void Walk(int i, List<Spot> chosen)
        {
            if (i == windows.Count)
            {
                var r = Route(chosen, windows, c);
                if (r != null && (best == null || r.Total.Profit > best.Total.Profit)) best = r;
                return;
            }
            foreach (var s in shortlists[i])
            {
                if (chosen.Count > 0 && s.Region != chosen[0].Region) continue;
                chosen.Add(s);
                Walk(i + 1, chosen);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }
        Walk(0, new List<Spot>());
        return best;
    }
}
