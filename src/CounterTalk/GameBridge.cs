using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace CounterTalk
{
    public sealed class GameBridge
    {
        private sealed class VisitIdentity { public readonly string Id = Guid.NewGuid().ToString("N"); }
        private static readonly ConditionalWeakTable<StoreClientInstance, VisitIdentity> visitIds = new ConditionalWeakTable<StoreClientInstance, VisitIdentity>();
        private readonly string visitId;
        private readonly PlayerStore store;
        private readonly StoreClientInstance visit;
        private readonly int day;
        private readonly Dictionary<int, GameItem> observed = new Dictionary<int, GameItem>();

        private readonly HashSet<int> receivedGifts = new HashSet<int>();
        private readonly Dictionary<string, bool> events = new Dictionary<string, bool>();
        public readonly StoreClient Client;
        public NativeBundle Bundle { get; }
        public bool Faulted { get; private set; }
        public bool DeparturePending { get; private set; }
        public JObject LastTradeToolResult { get; private set; }
        private readonly JArray quoteHistory = new JArray();
        private int quoteSequence;
        private JObject RememberQuote(JObject result)
        {
            if ((string)result["status"] != "native_quote_applied") return result;
            var quote = Bundle.Describe();
            if (quote == null) return result;
            var record = (JObject)quote.DeepClone();
            record.Remove("tags_effective");
            record["sequence"] = ++quoteSequence;
            record["status"] = "historical_applied_quote_not_proof_of_completion_or_current_availability";
            var lines = (JArray)record["items"];
            foreach (JObject line in lines)
            {
                var item = ObservedItem(line.Value<int>("item_id"));
                if (item != null) { line["name"] = item.name; line["native_base_price_at_quote"] = Bundle.BasePrice(item); }
            }
            record["item_count"] = lines.Count;
            record["average_price_per_item"] = lines.Count > 0 ? record.Value<decimal>("total") / lines.Count : 0;
            quoteHistory.Add(record);
            if (quoteHistory.Count > 12) quoteHistory.RemoveAt(0);
            return result;
        }
        public int TradeRevision => TradeJournal.Revision(visit);
        private int playerTurn, lastBudgetTurn = -1;
        private int previousTurnTradeRevision, turnTradeRevision;
        private JArray tradesBeforeMessage = new JArray();
        public void BeginPlayerTurn()
        {
            playerTurn++;
            turnTradeRevision = TradeRevision;
            tradesBeforeMessage = TradeJournal.Since(visit, previousTurnTradeRevision);
            previousTurnTradeRevision = turnTradeRevision;
        }
        public JObject TurnTradeContext() => new JObject {
            ["completed_since_previous_player_message_before_current_message"] = tradesBeforeMessage.DeepClone(),
            ["completed_after_current_player_message"] = TradeJournal.Since(visit, turnTradeRevision),
            ["interpretation"] = "These are completed handovers, not offers. A matching reference in the player's message can refer to these goods even when they are no longer on the counter. Do not infer a second purchase without evidence."
        };
        private string BudgetKey => "budget." + day + "." + visitId + ".";
        private int Data(string key) => ModHelper.GetModDataInt(ModEntry.Id, key);
        private void Save(string key, int value) => ModHelper.SetModDataInt(ModEntry.Id, key, value);

        private int BudgetBaseline
        {
            get
            {
                if (Data(BudgetKey + "initialized") == 0)
                {
                    Save(BudgetKey + "baseline", Math.Max(0, Client.GetBudget()));
                    Save(BudgetKey + "initialized", 1);
                }
                return Data(BudgetKey + "baseline");
            }
        }
        private bool CanAdjustBudget => ClientEligibility.CanTrade(Client) && Client.useClientBudget &&
            (Client.clientIntent == StoreClient.ClientIntent.BUY || Client.clientIntent == StoreClient.ClientIntent.SELLNBUY);
        private int RaiseRemaining => Math.Max(0, Math.Min(30, BudgetBaseline / 5) - Data(BudgetKey + "raised"));
        private int LowerRemaining => Math.Max(0, Math.Min(40, (int)((long)BudgetBaseline * 30 / 100)) - Data(BudgetKey + "lowered"));
        private bool CanLeave => Valid && !store.isClientBeingArrested && !store.isHandlingShootout &&
            Client.dismissable && Client.tryInterceptDismiss == null;
        public GameBridge()
        {
            store = PlayerStore.Instance;
            visit = store.currentClientInstance;
            visitId = visitIds.GetValue(visit, _ => new VisitIdentity()).Id;
            Client = visit.GetClientBlueprint();
            day = StoreStation.Instance.dayCounter;
            Bundle = new NativeBundle(this);
        }
        public bool Valid => !Faulted && PlayerStore.HasInstance && ReferenceEquals(PlayerStore.Instance, store) &&
            ReferenceEquals(store.currentClientInstance, visit) && StoreStation.Instance != null &&
            StoreStation.Instance.dayCounter == day && !ModHelper.IsOutsideStore() && ClientEligibility.CanChat(Client);
        private string DailyKey => "day." + day + "." + visitId + ".concession";
        private int Spent => ModHelper.GetModDataInt(ModEntry.Id, DailyKey);
        private List<GameItem> Counter => EmporiumEntry.Instance.GetCountersItems();
        private string RelationKey => "relation.archetype." + Client.identifier;

        private JObject IdentityFacts()
        {
            // Read the already selected portrait, never reroll possibleSprites or infer from pixels.
            string sprite = Client.spriteName ?? "";
            var gender = LocGenderHelper.GetGenderFromSpriteName(sprite);
            return new JObject {
                ["display_name"] = Client.displayName,
                ["faction_id"] = Client.clientFaction,
                ["portrait_id"] = sprite,
                ["gender"] = gender == LocGenderHelper.LocGender.Male ? "male" :
                    gender == LocGenderHelper.LocGender.Female ? "female" : "unspecified",
                ["gender_source"] = "Native LocGenderHelper mapping of the selected portrait; Neutral means unspecified, not a confirmed identity.",
                ["speech_register"] = LocGenderHelper.GetClientRegister(Client).ToString(),
                ["birthplace"] = JValue.CreateNull(),
                ["occupation"] = JValue.CreateNull(),
                ["background_evidence"] = "Public display name, world_context.public_role_tags, faction, opening and actual original dialogue establish background. Null occupation means no detailed job title; explicit public role tags still apply. Faction is not birthplace; buying water is not proof of mining work. Portrait IDs are technical references, not biographies."
            };
        }

        public JObject Snapshot()
        {
            if (!Valid) return Error("conversation_unavailable");
            var items = new JArray();
            var customerItems = new JArray();
            var playerItems = new JArray();
            foreach (var item in Counter)
            {
                int id = item.GetUniqueID();
                observed[id] = item;
                bool playerOwned = GeneralHelper.IsItemOwned(item);
                (playerOwned ? playerItems : customerItems).Add(id);
                string location = ReferenceEquals(item.parentInventory, EmporiumEntry.Instance.frontInvinvElement) ? "customer_side_counter" :
                    ReferenceEquals(item.parentInventory, EmporiumEntry.Instance.backInvinvElement) ? "trade_area" : "counter_other";
                items.Add(new JObject {
                    ["instance_id"] = id, ["name"] = item.name, ["description"] = item.shortDescription,
                    ["units"] = item.unitCount, ["owner"] = playerOwned ? "player" : "customer",
                    ["owner_from_your_character_perspective"] = playerOwned ? "belongs_to_the_shopkeeper_not_you" : "belongs_to_you_the_customer",
                    ["location"] = location,
                    ["ownership_fact"] = playerOwned ? "The shopkeeper owns this item. It is not your possession." :
                        "You, the speaking customer, own this item. Do not describe it as the shopkeeper's stock or offer.",
                    ["sale_money_flow"] = playerOwned ? "customer pays player; check customer purchasing budget" : "player pays customer; customer purchasing budget does not limit receiving money",
                    ["quote_direction"] = playerOwned ? "player_sells" : "player_buys",
                    ["facts"] = ItemFacts.Read(item),
                    ["quote"] = item.GetNegociatedValue(), ["gift_received_this_visit"] = receivedGifts.Contains(id)
                });
            }
            var rep = store.storeReputations?.Find(r => r.factionId == Client.clientFaction);
            return new JObject {
                ["name"] = Client.displayName, ["faction"] = Client.clientFaction,
                ["identity"] = IdentityFacts(),
                ["world_context"] = WorldLore.ForClient(Client),
                ["recent_news"] = NewsContext.Read(),
                ["speaking_role"] = "You are customer/NPC. The player is the shopkeeper. In dialogue: I/my = customer; you/your = player.",
                ["your_customer_owned_item_ids"] = customerItems,
                ["shopkeeper_owned_item_ids"] = playerItems,
                ["intent"] = Client.clientIntent.ToString(), ["opening"] = Client.mainDialogue?.plainText ?? "",
                ["already_spoken_dialogue"] = SpokenDialogue.Read(visit),
                ["faction_reputation"] = rep?.GetReputation() ?? 0,
                ["archetype_relationship"] = ModHelper.GetModDataInt(ModEntry.Id, RelationKey),
                ["relationship_scope"] = "Prototype aggregates this customer archetype, not a verified recurring individual",
                ["language"] = ModHelper.GetCurrentLocale().Identifier.Code,
                ["trade_tools_available"] = ClientEligibility.CanTrade(Client),
                ["negotiated_item_group"] = Bundle.Describe(),
                ["last_trade_tool_result"] = LastTradeToolResult?.DeepClone(),
                ["previous_applied_quotes"] = quoteHistory.DeepClone(),
                ["native_transaction_history"] = TradeJournal.Read(visit),
                ["current_turn_trade_context"] = TurnTradeContext(),
                ["trade_directions"] = new JObject {
                    ["player_buys"] = "Player pays player_cash to customer and receives customer-owned items. Customer budget is NOT required.",
                    ["player_sells"] = "Customer spends customer_budget_remaining and receives player-owned items. Player receives the money.",
                    ["native_intent_perspective"] = "intent BUY/SELL is from the CUSTOMER perspective, not the player perspective. SELLNBUY supports both."
                },
                // Same flag/getter as the native tablet's customer-budget footer. Never use clientCash or opening text.
                ["customer_budget_enabled"] = Client.useClientBudget,
                ["customer_budget_remaining"] = Client.useClientBudget ? new JValue(Client.GetBudget()) : JValue.CreateNull(),
                ["player_cash"] = store.playerCash,
                ["budget_adjustment_available"] = CanAdjustBudget && !DeparturePending && Data(BudgetKey + "changes") < 3 && lastBudgetTurn != playerTurn,
                ["budget_raise_remaining"] = CanAdjustBudget ? RaiseRemaining : 0,
                ["budget_lower_remaining"] = CanAdjustBudget ? Math.Min(LowerRemaining, Math.Max(0, Client.GetBudget())) : 0,
                ["can_leave"] = CanLeave && !DeparturePending,
                ["concession_remaining"] = Math.Max(0, ModEntry.Budget.Value - Spent),
                ["counter"] = items
            };
        }

        public JObject Tool(JObject action)
        {
            if (!Valid) return Error("customer_or_save_changed");
            if (DeparturePending) return Error("customer_is_leaving");
            string name = (string)action["action"];
            if (name == "inspect_counter") return Snapshot();
            if (name == "adjust_budget") return AdjustBudget(action);
            if (name == "leave")
            {
                if (!CanLeave) return Error("departure_unavailable");
                string speech = action.Value<string>("speech");
                if (string.IsNullOrWhiteSpace(speech) || speech.Length > 400) return Error("brief_farewell_required");
                Cancel(); DeparturePending = true;
                return new JObject { ["status"] = "departure_pending", ["speech"] = speech };
            }
            if (name == "react_to_event") return React(action);
            if (name == "propose_bundle")
            {
                return LastTradeToolResult = RememberQuote(ApplyOwnedBundle(action));
            }
            if (name == "propose_sale" || name == "accept_player_gift")
            {
                bool gift = name == "accept_player_gift";
                long price = gift ? 0 : action.Value<long?>("price") ?? -1;
                return LastTradeToolResult = RememberQuote(Bundle.Apply(new JArray(new JObject { ["item_id"] = action["item_id"], ["price"] = price }), price, gift));
            }
            return Error("unknown_action");
        }

        private JObject ApplyOwnedBundle(JObject action)
        {
            var entries = action["items"] as JArray;
            if (entries == null || entries.Count == 0) return Error("missing_bundle_items");
            bool? playerOwns = null;
            foreach (var entry in entries)
            {
                var item = ObservedItem(entry.Value<int?>("item_id") ?? 0);
                if (item == null || !Counter.Contains(item)) return Error("unknown_or_missing_item");
                bool owned = GeneralHelper.IsItemOwned(item);
                if (playerOwns.HasValue && playerOwns.Value != owned) return Error("mixed_ownership_requires_separate_trades_not_barter");
                playerOwns = owned;
            }
            // Direction is authoritative game state; a model's perspective error cannot reverse the payer.
            var result = Bundle.Apply(entries, action.Value<long?>("total") ?? -1, playerOwns.Value);
            result["resolved_direction"] = playerOwns.Value ? "player_sells" : "player_buys";
            result["payer"] = playerOwns.Value ? "customer" : "player";
            result["payee"] = playerOwns.Value ? "player" : "customer";
            return result;
        }
        public const string NegotiatedFeatureId = "counter_talk.negotiated";
        private JObject AdjustBudget(JObject action)
        {
            if (!CanAdjustBudget || store.isClientBeingArrested || store.isHandlingShootout) return Error("budget_adjustment_unavailable");
            int current = Client.GetBudget();
            if (action.Value<int?>("expected_budget") != current) return Error("budget_changed_read_current_state");
            int delta = action.Value<int?>("delta") ?? 0;
            if (delta == 0 || lastBudgetTurn == playerTurn || Data(BudgetKey + "changes") >= 3)
                return Error("budget_adjustment_limit");
            if ((delta > 0 && delta > RaiseRemaining) || (delta < 0 && -(long)delta > LowerRemaining) ||
                (long)current + delta < 0 || (long)current + delta > int.MaxValue)
                return Error("budget_adjustment_out_of_bounds");
            // Reserve cumulative allowances before the native mutation. Reductions never refund increases.
            lastBudgetTurn = playerTurn;
            try
            {
                Save(BudgetKey + "changes", Data(BudgetKey + "changes") + 1);
                string direction = BudgetKey + (delta > 0 ? "raised" : "lowered");
                Save(direction, checked(Data(direction) + Math.Abs(delta)));

                Client.ModBudget(delta);
                return new JObject { ["status"] = "budget_adjusted", ["delta"] = delta,
                    ["customer_budget_remaining"] = Client.GetBudget() };
            }
            catch { Faulted = true; return Error("budget_update_failed_tools_locked_do_not_retry"); }
        }

        public JObject CompleteDeparture()
        {
            if (!DeparturePending || !CanLeave) { DeparturePending = false; return Error("departure_no_longer_available"); }
            DeparturePending = false; Cancel();
            try
            {
                store.TryPlayerDismissCurrentClient(false);
                return new JObject { ["status"] = ReferenceEquals(store.currentClientInstance, visit) ? "departure_intercepted" : "departed" };
            }
            catch { Faulted = true; return Error("departure_failed_tools_locked_do_not_retry"); }
        }

        public void CancelDeparture() { DeparturePending = false; }

        public void Cancel() { Bundle?.Cancel(); }
        public GameItem ObservedItem(int id) => observed.TryGetValue(id, out var item) ? item : null;
        public bool IsReceivedGift(int id) => receivedGifts.Contains(id);
        public int ConcessionRemaining => Math.Max(0, ModEntry.Budget.Value - Spent);
        public void SpendConcession(int amount) { Save(DailyKey, checked(Spent + amount)); }
        public void LockTools() { Faulted = true; }
        public JObject RecordNativeTrade(string id, List<GameItem> items, bool playerSold, long total)
        {
            events[id] = false;
            if (playerSold && total == 0) foreach (var item in items) receivedGifts.Add(item.GetUniqueID());
            return LastTradeToolResult = new JObject { ["status"] = "completed", ["event_id"] = id, ["total"] = total,
                ["item_count"] = items.Count, ["direction"] = playerSold ? "player_sells" : "player_buys" };
        }
        private JObject React(JObject action)
        {
            string id = action.Value<string>("event_id") ?? "";
            if (!events.TryGetValue(id, out bool used) || used) return Error("unknown_or_settled_event");
            events[id] = true;
            int current = ModHelper.GetModDataInt(ModEntry.Id, RelationKey);
            int delta = Rules.RelationshipDelta(current, action.Value<int?>("feeling") ?? 0);
            ModHelper.SetModDataInt(ModEntry.Id, RelationKey, current + delta);
            string daily = "recommend." + Client.clientFaction + "." + day;
            string total = "recommend.total." + Client.clientFaction;
            int repChange = 0;
            var rep = store.storeReputations?.Find(r => r.factionId == Client.clientFaction);
            if (action.Value<bool?>("recommend") == true && delta > 0 && rep != null &&
                Rules.CanRecommend(ModHelper.GetModDataInt(ModEntry.Id, daily), ModHelper.GetModDataInt(ModEntry.Id, total), rep.GetReputation()))
            {
                ModHelper.SetModDataInt(ModEntry.Id, daily, 1);
                ModHelper.SetModDataInt(ModEntry.Id, total, ModHelper.GetModDataInt(ModEntry.Id, total) + 1);
                StoreReputation.ModReputationRaw(Client.clientFaction, 1, false);
                repChange = 1;
            }
            return new JObject { ["status"] = "settled", ["relationship_delta"] = delta, ["reputation_delta"] = repChange };
        }
        public static JObject Error(string message) => new JObject { ["error"] = message };
    }

}
