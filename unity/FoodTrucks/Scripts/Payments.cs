#nullable enable
using System;
using System.Linq;

namespace FoodTrucks
{
    // Pays money in through the game and reports whether it was actually credited.
    // GameManager.ChangeMoney credits the balance and books the transaction before it fires
    // the money-change event, whose listeners are not guarded. If one of them throws, the payment
    // has happened anyway. Whether the transaction was booked decides, not the exception: the float
    // balance can't show a few dollars on a balance of tens of millions.
    internal static class Payments
    {
        public static bool PayIn(long cents, TransactionInfo info) => PayIn(cents, info, null);

        public static bool PayIn(long cents, TransactionInfo info, int? day)
        {
            if (cents <= 0) throw new ArgumentOutOfRangeException(nameof(cents));
            var transactions = SaveGameManager.Current.Transactions;
            var lastBefore = transactions.Count > 0 ? transactions.Last() : null;
            try
            {
                return GameManager.ChangeMoneySafe(cents / 100f, info, day);
            }
            catch (Exception ex)
            {
                var credited = transactions.Count > 0 && !ReferenceEquals(transactions.Last(), lastBefore);
                Log.Error($"Payment of {CartSelling.Money(cents)} threw ({(credited ? "credited" : "not credited")}): {ex}");
                return credited;
            }
        }

        public static bool PayOut(long cents, TransactionInfo info, int day)
        {
            if (cents <= 0) throw new ArgumentOutOfRangeException(nameof(cents));
            var transactions = SaveGameManager.Current.Transactions;
            var lastBefore = transactions.Count > 0 ? transactions.Last() : null;
            try
            {
                return GameManager.ChangeMoneySafe(-cents / 100f, info, day, null, force: true);
            }
            catch (Exception ex)
            {
                var booked = transactions.Count > 0 && !ReferenceEquals(transactions.Last(), lastBefore);
                Log.Error($"Payment out of {CartSelling.Money(cents)} threw ({(booked ? "booked" : "not booked")}): {ex}");
                return booked;
            }
        }

        public static bool TryPayOut(long cents, TransactionInfo info, bool showNotification)
        {
            if (cents <= 0) throw new ArgumentOutOfRangeException(nameof(cents));
            var transactions = SaveGameManager.Current.Transactions;
            var lastBefore = transactions.Count > 0 ? transactions.Last() : null;
            try
            {
                return GameManager.ChangeMoneySafe(-cents / 100f, info, null, null, force: false, showNotification: showNotification);
            }
            catch (Exception ex)
            {
                var booked = transactions.Count > 0 && !ReferenceEquals(transactions.Last(), lastBefore);
                Log.Error($"Payment out of {CartSelling.Money(cents)} threw ({(booked ? "booked" : "not booked")}): {ex}");
                return booked;
            }
        }
    }
}