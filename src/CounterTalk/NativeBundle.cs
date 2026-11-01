using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace CounterTalk
{
    // A quote changes the native tablet's prices; only the native trade buttons transfer assets.
    public sealed class NativeBundle
    {
        public sealed class Line
        {
            public GameItem Item;
            public long BasePrice, Price;
            public GameInventory Source;
            public GridShape Shape;
        }
        private readonly GameBridge bridge;
        private List<Line> lines = new List<Line>();
        private List<Line> executionLines = new List<Line>();
        private readonly HashSet<GameItem> executed = new HashSet<GameItem>();
        private bool readingBase, executing;
        private bool playerSelling;
        public string Id { get; private set; }
        public bool Active => Id != null;
        public bool Executing => executing;
        public NativeBundle(GameBridge bridge) { this.bridge = bridge; }
        public JObject Describe() => !Active ? null : new JObject {
            ["quote_id"] = Id, ["tags_effective"] = Ready,
            ["direction"] = playerSelling ? "player_sells" : "player_buys",
            ["items"] = new JArray(lines.Select(l => new JObject { ["item_id"] = l.Item.GetUniqueID(), ["price"] = l.Price })),
            ["total"] = lines.Sum(l => l.Price)
        };

        public long BasePrice(GameItem item)
        {
            readingBase = true;
            try { return item.GetNegociatedValue(); }
            finally { readingBase = false; }
        }
        public long Price(GameItem item, long original)
        {
            if (readingBase || !Active) return original;
            var line = (executing ? executionLines : lines).Find(l => ReferenceEquals(l.Item, item));
            if (line == null) return original;
            if (executing) return line.Price;
            if (original != line.BasePrice || !Ready) return original;
            return line.Price;
        }
        // Moving a member away suspends every price tag, but does not forget the agreed item identities.
        public bool Ready => Active && bridge.Valid && ClientEligibility.CanTrade(bridge.Client) && !bridge.DeparturePending &&
            lines.All(l => Intact(l, BasePrice(l.Item)));
        private bool Intact(Line line, long original) =>
            line.Item.unitCount == 1 && line.Item.MayRemove() && !line.Item.FindAllChildItems().Any() &&
            GeneralHelper.IsItemOwned(line.Item) == playerSelling && original == line.BasePrice &&
            EmporiumEntry.Instance.GetBackInvItems().Contains(line.Item);

        public JObject Apply(JArray entries, long total, bool selling)
        {
            if (executing || bridge.DeparturePending || !bridge.Valid || !ClientEligibility.CanTrade(bridge.Client)) return GameBridge.Error("trade_unavailable");
            if ((selling && bridge.Client.clientIntent == StoreClient.ClientIntent.SELL) ||
                (!selling && bridge.Client.clientIntent == StoreClient.ClientIntent.BUY)) return GameBridge.Error("native_trade_direction_unavailable");
            if (entries == null || entries.Count < 1 || entries.Count > 12) return GameBridge.Error("bundle_requires_1_to_12_items");
            if (bridge.Client.isMultiBuyDisabled && entries.Count > 1) return GameBridge.Error("native_customer_disallows_bundle");
            var next = new List<Line>();
            foreach (var entry in entries)
            {
                int id = entry.Value<int?>("item_id") ?? 0;
                var item = bridge.ObservedItem(id);
                long price = entry.Value<long?>("price") ?? -1;
                if (item == null || next.Any(l => ReferenceEquals(l.Item, item)) ||
                    !EmporiumEntry.Instance.GetCountersItems().Contains(item)) return GameBridge.Error("unknown_duplicate_or_missing_item");
                if (item.unitCount != 1 || !item.MayRemove() || item.FindAllChildItems().Any() ||
                    GeneralHelper.IsItemOwned(item) != selling || bridge.IsReceivedGift(id)) return GameBridge.Error("invalid_item_or_owner");
                if (selling && !bridge.Client.IsClientBuyingThisItem(item)) return GameBridge.Error("native_customer_refuses_item");
                long baseline = BasePrice(item);
                if (baseline < 0 || baseline > int.MaxValue || price < 0 || price > int.MaxValue || (!selling && price > baseline)) return GameBridge.Error("invalid_price");
                next.Add(new Line { Item = item, Price = price, BasePrice = baseline, Source = item.parentInventory, Shape = item.modifiedShape });
            }
            if (total < 0 || total > int.MaxValue || next.Sum(l => l.Price) != total) return GameBridge.Error("bundle_total_mismatch");
            long concession = next.Sum(l => selling ? Math.Max(0, l.Price - l.BasePrice) : l.BasePrice - l.Price);
            if (concession > bridge.ConcessionRemaining) return GameBridge.Error("customer_concession_limit");
            if (selling && bridge.Client.useClientBudget && total > bridge.Client.GetBudget()) return GameBridge.Error("customer_budget_insufficient");
            var back = EmporiumEntry.Instance.backInvinvElement;
            Cancel();
            // Slot placement only. No BuyItem/SellItem, ownership changes, or warehouse fallback.
            var moved = new List<Line>();
            try
            {
                foreach (var line in next)
                {
                    if (ReferenceEquals(line.Item.parentInventory, back)) continue;
                    var slot = back.TryFindOneValidInventorySlot(line.Item);
                    if (slot == null || !ReferenceEquals(slot.inventory, back) || slot.itemGridShape == null || slot.numTransfer < 1)
                        throw new InvalidOperationException("no_free_trade_slot");
                    moved.Add(line);
                    if (slot.TryAcceptOnce(1) != 1 || !ReferenceEquals(line.Item.parentInventory, back))
                        throw new InvalidOperationException("trade_placement_failed");
                }
                // Native placement may add retail modifiers: re-read them, then validate the whole offer again.
                foreach (var line in next) line.BasePrice = BasePrice(line.Item);
                if ((!selling && next.Any(l => l.Price > l.BasePrice)) ||
                    next.Sum(l => selling ? Math.Max(0, l.Price - l.BasePrice) : l.BasePrice - l.Price) > bridge.ConcessionRemaining)
                    throw new InvalidOperationException("price_changed_on_trade_placement");
                lines = next; playerSelling = selling; Id = Guid.NewGuid().ToString("N");
                Validate();
                return new JObject { ["status"] = "native_quote_applied", ["total"] = total,
                    ["direction"] = selling ? "player_sells" : "player_buys", ["items"] = entries.DeepClone() };
            }
            catch
            {
                foreach (var line in next) line.Item.RemoveItemFeatureByID(GameBridge.NegotiatedFeatureId);
                bool restored = true;
                foreach (var line in moved.AsEnumerable().Reverse())
                {
                    if (ReferenceEquals(line.Item.parentInventory, line.Source)) continue;
                    if (line.Source == null || line.Shape == null) { restored = false; continue; }
                    try { restored &= new SlotMarker { item = line.Item, inventory = line.Source, itemGridShape = line.Shape, numTransfer = 1 }.TryAcceptOnce(1) == 1; }
                    catch { restored = false; }
                }
                if (!restored) bridge.LockTools();
                return GameBridge.Error(restored ? "could_not_stage_complete_bundle_no_trade_executed" : "placement_restore_failed_inspect_counter_tools_locked");
            }
        }

        public bool Begin(List<GameItem> items, bool selling)
        {
            if (!Active) return true;
            // Independent native trades must not consume or invalidate an unrelated quote.
            if (!executing && !items.Any(i => lines.Any(l => ReferenceEquals(l.Item, i))))
                return bridge.Valid && !bridge.DeparturePending;
            // An incomplete set trades normally at native prices. A completed partial sale invalidates the old quote.
            if (!executing && !Ready) return bridge.Valid && !bridge.DeparturePending;
            if (executing || !bridge.Valid || bridge.DeparturePending || selling != playerSelling ||
                !ClientEligibility.CanTrade(bridge.Client) || items.Distinct().Count() != items.Count ||
                lines.Any(l => !items.Contains(l.Item) || !Intact(l, BasePrice(l.Item)))) return false;
            if (items.Any(i => GeneralHelper.IsItemOwned(i) != selling || !EmporiumEntry.Instance.GetBackInvItems().Contains(i))) return false;
            var batch = items.Select(i => lines.Find(l => ReferenceEquals(l.Item, i)) ??
                new Line { Item = i, BasePrice = BasePrice(i), Price = BasePrice(i) }).ToList();
            if (batch.Any(l => l.Price < 0)) return false;
            long total = batch.Sum(l => l.Price);
            if ((!selling && PlayerStore.Instance.playerCash < total) ||
                (selling && bridge.Client.useClientBudget && bridge.Client.GetBudget() < total) ||
                (selling && items.Any(i => !bridge.Client.IsClientBuyingThisItem(i)))) return false;
            long concession = lines.Sum(l => selling ? Math.Max(0, l.Price - l.BasePrice) : l.BasePrice - l.Price);
            if (concession > bridge.ConcessionRemaining) return false;
            bridge.SpendConcession((int)concession);
            executionLines = batch;
            executed.Clear(); executing = true;
            return true;
        }
        public bool AllowTransfer(GameItem item, bool selling) => !Active ||
            (!executing && (!Ready || !lines.Any(l => ReferenceEquals(l.Item, item)))) ||
            (executing && selling == playerSelling && executionLines.Any(l => ReferenceEquals(l.Item, item)) && executed.Add(item));

        public JObject Finish(bool failed)
        {
            if (!executing) return null;
            bool complete = !failed && executionLines.All(l => executed.Contains(l.Item) && GeneralHelper.IsItemOwned(l.Item) != playerSelling);
            var result = complete ? bridge.RecordNativeTrade(Id, executionLines.Select(l => l.Item).ToList(), playerSelling, executionLines.Sum(l => l.Price)) :
                GameBridge.Error("native_bundle_incomplete_inspect_assets_tools_locked_no_retry");
            executing = false; Cancel();
            executionLines.Clear();
            if (!complete) bridge.LockTools();
            return result;
        }
        public void Cancel()
        {
            if (executing) return;
            foreach (var line in lines) line.Item.RemoveItemFeatureByID(GameBridge.NegotiatedFeatureId);
            lines.Clear(); Id = null;
        }
        public void Validate()
        {
            if (!Active || executing) return;
            if (!bridge.Valid || lines.Any(l => GeneralHelper.IsItemOwned(l.Item) != playerSelling || l.Item.unitCount != 1))
            {
                Cancel(); return;
            }
            bool ready = Ready;
            foreach (var line in lines)
            {
                if (!ready) { line.Item.RemoveItemFeatureByID(GameBridge.NegotiatedFeatureId); continue; }
                if (line.Item.FindItemFeatureByID(GameBridge.NegotiatedFeatureId) != null) continue;
                var feature = new ItemFeature(ItemFeature.FeatureType.ClientTemporary) {
                    identifier = GameBridge.NegotiatedFeatureId, category = GameBridge.NegotiatedFeatureId,
                    valueStage = ItemFeature.ValueStage.Final, isExposable = false
                };
                string label = (ModHelper.GetCurrentLocale().Identifier.Code == "zh" ? "谈价：" : "Negotiated: ") + line.BasePrice + " → " + line.Price;
                line.Item.AddItemFeature(feature.SetPublicDisplay(label).SetActualDisplay(label).SetPreExposedValueMod(0));
            }
        }
    }
}
