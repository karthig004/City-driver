using System;
using CityDriver.Save;

namespace CityDriver.Economy
{
    /// <summary>
    /// The player's money (rupees). Every change is a transaction with a unique id; applying the same id twice does nothing,
    /// so a reward or penalty can never be paid twice (e.g. after a scene reload). The balance may go negative (Shadow Run debt).
    /// Independent of XP.
    /// </summary>
    public sealed class WalletService
    {
        private const int MaxLedger = 200;
        private const int MaxRememberedIds = 1000;

        private readonly SaveData data;

        public WalletService(SaveData data) { this.data = data ?? throw new ArgumentNullException(nameof(data)); }

        /// <summary>Raised after a transaction was applied. Args: new balance, amount.</summary>
        public event Action<long, long> BalanceChanged;

        public long Balance => data.walletBalance;
        public bool IsInDebt => data.walletBalance < 0;

        public bool HasApplied(string transactionId) => data.appliedTransactionIds.Contains(transactionId);

        /// <summary>
        /// Applies a signed amount once. Returns false (and changes nothing) if this transaction id was applied before.
        /// </summary>
        public bool Apply(string transactionId, long amount, string reason)
        {
            if (string.IsNullOrEmpty(transactionId)) throw new ArgumentException("Transaction id is required.", nameof(transactionId));
            if (HasApplied(transactionId)) return false;

            data.walletBalance += amount;
            data.appliedTransactionIds.Add(transactionId);
            if (data.appliedTransactionIds.Count > MaxRememberedIds) data.appliedTransactionIds.RemoveRange(0, data.appliedTransactionIds.Count - MaxRememberedIds);

            data.transactions.Add(new TransactionRecord { id = transactionId, amount = amount, reason = reason, balanceAfter = data.walletBalance, utcTicks = DateTime.UtcNow.Ticks });
            if (data.transactions.Count > MaxLedger) data.transactions.RemoveRange(0, data.transactions.Count - MaxLedger);

            BalanceChanged?.Invoke(data.walletBalance, amount);
            return true;
        }

        public bool CanAfford(long cost) => cost >= 0 && data.walletBalance >= cost;

        /// <summary>Pays for a purchase. Fails (returns false) if it cannot be afforded or the id was already used.</summary>
        public bool TrySpend(string transactionId, long cost, string reason)
        {
            if (!CanAfford(cost) || HasApplied(transactionId)) return false;
            return Apply(transactionId, -cost, reason);
        }
    }
}
