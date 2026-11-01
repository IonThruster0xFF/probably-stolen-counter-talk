using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace CounterTalk
{
    // Records native transactions even before F6 is first opened. No API request is triggered here.
    public static class TradeJournal
    {
        private sealed class Journal
        {
            public readonly List<JObject> Entries = new List<JObject>();
            public int Revision;
            public long PlayerPaid, CustomerPaid;
        }
        private static readonly ConditionalWeakTable<StoreClientInstance, Journal> journals = new ConditionalWeakTable<StoreClientInstance, Journal>();
        public sealed class Capture
        {
            internal PlayerStore Store;
            internal StoreClientInstance Visit;
            internal GameItem Item;
            internal bool Selling, Finished;
            internal int CashBefore, ItemId, Units;
            internal string Name;
        }
        public static Capture Begin(PlayerStore store, GameItem item, bool playerSelling)
        {
            var visit = store?.currentClientInstance;
            if (visit == null || item == null || !ClientEligibility.CanChat(visit.GetClientBlueprint()) ||
                GeneralHelper.IsItemOwned(item) != playerSelling) return null;
            return new Capture { Store = store, Visit = visit, Item = item, Selling = playerSelling,
                CashBefore = store.playerCash, ItemId = item.GetUniqueID(), Units = item.unitCount, Name = item.name };
        }
        public static void Complete(Capture capture, bool failed)
        {
            if (capture == null || capture.Finished) return;
            capture.Finished = true;
            if (failed || GeneralHelper.IsItemOwned(capture.Item) == capture.Selling) return;
            long paid = capture.Selling ? (long)capture.Store.playerCash - capture.CashBefore : (long)capture.CashBefore - capture.Store.playerCash;
            if (paid < 0) return; // Unexpected callback accounting must not be reported as a normal completed sale.
            var journal = journals.GetValue(capture.Visit, _ => new Journal());
            journal.Revision++;
            if (capture.Selling) journal.CustomerPaid += paid; else journal.PlayerPaid += paid;
            journal.Entries.Add(new JObject {
                ["sequence"] = journal.Revision, ["status"] = "completed",
                ["direction"] = capture.Selling ? "player_sells" : "player_buys",
                ["payer"] = capture.Selling ? "customer" : "player", ["payee"] = capture.Selling ? "player" : "customer",
                ["item_id"] = capture.ItemId, ["item_name"] = capture.Name, ["units"] = capture.Units,
                ["actual_paid"] = paid, ["owner_after"] = capture.Selling ? "customer" : "player",
                ["player_cash_after"] = capture.Store.playerCash,
                ["customer_budget_after"] = capture.Visit.GetClientBlueprint().useClientBudget ?
                    new JValue(capture.Visit.GetClientBlueprint().GetBudget()) : JValue.CreateNull()
            });
            if (journal.Entries.Count > 50) journal.Entries.RemoveAt(0);
        }
        public sealed class BatchCapture
        {
            internal readonly List<Capture> Items = new List<Capture>();
            internal bool Finished;
        }
        public static BatchCapture BeginBatch(PlayerStore store, IEnumerable<GameItem> items)
        {
            var batch = new BatchCapture();
            foreach (var item in items)
            {
                var capture = Begin(store, item, false);
                if (capture != null) batch.Items.Add(capture);
            }
            return batch;
        }
        public static void CompleteBatch(BatchCapture batch, bool failed)
        {
            if (batch == null || batch.Finished || failed || batch.Items.Count == 0) return;
            batch.Finished = true;
            var first = batch.Items[0];
            var transferred = new JArray();
            foreach (var item in batch.Items)
                if (GeneralHelper.IsItemOwned(item.Item)) transferred.Add(new JObject {
                    ["item_id"] = item.ItemId, ["item_name"] = item.Name, ["units"] = item.Units
                });
            long paid = (long)first.CashBefore - first.Store.playerCash;
            if (transferred.Count == 0 || paid < 0) return;
            var journal = journals.GetValue(first.Visit, _ => new Journal());
            journal.Revision++; journal.PlayerPaid += paid;
            journal.Entries.Add(new JObject { ["sequence"] = journal.Revision, ["status"] = "completed",
                ["direction"] = "player_buys", ["payer"] = "player", ["payee"] = "customer",
                ["items"] = transferred, ["actual_paid"] = paid, ["owner_after"] = "player",
                ["player_cash_after"] = first.Store.playerCash });
            if (journal.Entries.Count > 50) journal.Entries.RemoveAt(0);
        }
        public static int Revision(StoreClientInstance visit) => visit != null && journals.TryGetValue(visit, out var j) ? j.Revision : 0;
        public static JArray Since(StoreClientInstance visit, int revision)
        {
            var entries = new JArray();
            if (visit != null && journals.TryGetValue(visit, out var journal))
                foreach (var entry in journal.Entries)
                    if (entry.Value<int>("sequence") > revision) entries.Add(entry.DeepClone());
            return entries;
        }
        public static JObject Read(StoreClientInstance visit)
        {
            if (visit == null || !journals.TryGetValue(visit, out var j))
                return new JObject { ["revision"] = 0, ["completed_transactions"] = new JArray(), ["player_paid_total"] = 0, ["customer_paid_total"] = 0 };
            var entries = new JArray();
            foreach (var entry in j.Entries) entries.Add(entry.DeepClone());
            return new JObject { ["revision"] = j.Revision, ["completed_transactions"] = entries,
                ["player_paid_total"] = j.PlayerPaid, ["customer_paid_total"] = j.CustomerPaid };
        }
    }
}
