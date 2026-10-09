using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Saves
{
    // One vehicle's rows in the statement. Amounts are signed: income above 0, costs below.
    public sealed class StatementVehicle
    {
        public StatementVehicle(string vehicleId, int columns)
        {
            VehicleId = vehicleId;
            Total = new long[columns];
        }

        public string VehicleId { get; }
        public long[] Total { get; }
        // Only lines with an amount in some column, in LedgerLine order.
        public SortedDictionary<LedgerLine, long[]> Lines { get; } = new SortedDictionary<LedgerLine, long[]>();
    }

    // Econoview's street food rows over its day columns, worked out from the ledger and the game's stored summaries.
    //
    // The group's total for a day is what the day summary added to the game's total profit (SalesLedger.SummaryCents)
    // plus the crew wages and parking moved in from the game's own rows. Those two are taken off the game's
    // "Unassigned staff wages" and "Parking fees" rows by the same amounts (MovedWages, MovedParking), so the rows
    // still add up to the stored total profit. Anything in the summary that the per-vehicle lines don't cover
    // (days from before the breakdown) is the NotBrokenDown row.
    public sealed class StreetFoodStatement
    {
        // Rounding slack between the game's float sums and the ledger's cents.
        public const long ToleranceCents = 5;

        private StreetFoodStatement(int columns)
        {
            Total = new long[columns];
            Licence = new long[columns];
            EventPlaces = new long[columns];
            DeliveryFees = new long[columns];
            NotBrokenDown = new long[columns];
            MovedWages = new long[columns];
            MovedParking = new long[columns];
        }

        public long[] Total { get; }
        public List<StatementVehicle> Vehicles { get; } = new List<StatementVehicle>();
        public long[] Licence { get; }
        public long[] EventPlaces { get; }
        public long[] DeliveryFees { get; }
        public long[] NotBrokenDown { get; }
        // Costs to take off the game's rows, 0 or more.
        public long[] MovedWages { get; }
        public long[] MovedParking { get; }

        public bool IsEmpty =>
            Total.All(v => v == 0) && Vehicles.Count == 0 && Licence.All(v => v == 0) &&
            EventPlaces.All(v => v == 0) && DeliveryFees.All(v => v == 0) && NotBrokenDown.All(v => v == 0);

        // days: each column's day number (0 for a column without a summary). gameWagesCents and gameParkingCents:
        // the game's stored unassigned staff wages and parking fees for each column, as costs of 0 or more.
        // A day not marked in the summary shows nothing, since the stored total doesn't include it. Wages or
        // parking are only moved when the vehicles' share fits inside the game's row for that day; otherwise
        // that day's share stays in the game's row and the vehicle line shows nothing for it.
        public static StreetFoodStatement Build(SaveState? state, IReadOnlyList<int> days,
            IReadOnlyList<long> gameWagesCents, IReadOnlyList<long> gameParkingCents)
        {
            if (days == null) throw new ArgumentNullException(nameof(days));
            if (gameWagesCents == null || gameWagesCents.Count != days.Count) throw new ArgumentException("One wage figure per day.", nameof(gameWagesCents));
            if (gameParkingCents == null || gameParkingCents.Count != days.Count) throw new ArgumentException("One parking figure per day.", nameof(gameParkingCents));

            var columns = days.Count;
            var statement = new StreetFoodStatement(columns);
            if (state == null) return statement;
            var vehicles = new SortedDictionary<string, StatementVehicle>(StringComparer.Ordinal);

            for (var c = 0; c < columns; c++)
            {
                var entry = state.DailySales.FirstOrDefault(d => d != null && d.Day == days[c] && d.InSummary);
                if (entry == null) continue;
                var byVehicle = (entry.ByVehicle ?? new List<VehicleDay>()).Where(v => v != null).ToList();

                var wages = byVehicle.Aggregate(0L, (sum, v) => checked(sum + v.WagesCents));
                var parking = byVehicle.Aggregate(0L, (sum, v) => checked(sum + v.ParkingCents));
                var moveWages = wages > 0 && gameWagesCents[c] > 0 && wages <= gameWagesCents[c] + ToleranceCents;
                var moveParking = parking > 0 && gameParkingCents[c] > 0 && parking <= gameParkingCents[c] + ToleranceCents;
                if (moveWages) statement.MovedWages[c] = wages;
                if (moveParking) statement.MovedParking[c] = parking;

                long itemised = 0;
                foreach (var day in byVehicle)
                {
                    if (!vehicles.TryGetValue(day.VehicleId, out var vehicle))
                        vehicles[day.VehicleId] = vehicle = new StatementVehicle(day.VehicleId, columns);
                    itemised = checked(itemised + day.SalesCents + day.CateringCents - day.StockCents - day.PermitCents - day.CommissaryRentCents - day.OvernightFineCents);
                    Put(vehicle, LedgerLine.Sales, c, day.SalesCents);
                    Put(vehicle, LedgerLine.Catering, c, day.CateringCents);
                    Put(vehicle, LedgerLine.Stock, c, -day.StockCents);
                    if (moveWages) Put(vehicle, LedgerLine.Wages, c, -day.WagesCents);
                    if (moveParking) Put(vehicle, LedgerLine.Parking, c, -day.ParkingCents);
                    Put(vehicle, LedgerLine.Permit, c, -day.PermitCents);
                    Put(vehicle, LedgerLine.CommissaryRent, c, -day.CommissaryRentCents);
                    Put(vehicle, LedgerLine.OvernightFine, c, -day.OvernightFineCents);
                }

                statement.Licence[c] = -entry.LicenceCents;
                statement.EventPlaces[c] = -entry.EventPlacesCents;
                statement.DeliveryFees[c] = -entry.DeliveryFeesCents;
                itemised = checked(itemised - entry.LicenceCents - entry.EventPlacesCents - entry.DeliveryFeesCents);
                statement.NotBrokenDown[c] = checked(SalesLedger.SummaryCents(state, days[c]) - itemised);
            }

            foreach (var vehicle in vehicles.Values.Where(v => v.Lines.Count > 0))
            {
                for (var c = 0; c < columns; c++)
                    vehicle.Total[c] = vehicle.Lines.Values.Aggregate(0L, (sum, line) => checked(sum + line[c]));
                statement.Vehicles.Add(vehicle);
            }
            for (var c = 0; c < columns; c++)
            {
                var total = checked(statement.Licence[c] + statement.EventPlaces[c] + statement.DeliveryFees[c] + statement.NotBrokenDown[c]);
                foreach (var vehicle in statement.Vehicles) total = checked(total + vehicle.Total[c]);
                statement.Total[c] = total;
            }
            return statement;
        }

        private static void Put(StatementVehicle vehicle, LedgerLine line, int column, long cents)
        {
            if (cents == 0) return;
            if (!vehicle.Lines.TryGetValue(line, out var values))
                vehicle.Lines[line] = values = new long[vehicle.Total.Length];
            values[column] = checked(values[column] + cents);
        }
    }
}
