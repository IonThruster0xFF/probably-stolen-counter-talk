using System;
using System.Collections.Generic;
using CounterTalk;

// Behavioral test doubles. These tests exercise the actual bridge but do not claim Unity integration coverage.
namespace HarmonyLib { public class HarmonyPatch : Attribute { public HarmonyPatch() {} public HarmonyPatch(Type t, string n) {} } }
namespace CounterTalk {
    public static class ModEntry { public const string Id = "counter_talk"; public static Config Budget = new Config(); }
    public class Config { public int Value = 20; }
    public static class SpokenDialogue { public static string Read(StoreClientInstance c) => ""; }
}
public class Dialogue { public string plainText = "Hello"; }
public class StoreClient {
    public enum ClientIntent { UNDEFINED, BUY, SELL, SELLNBUY, INSPECTION, DIALOGUE, BARTER, RENT, LOAN_SHARK, SPECIAL, GUNSMITH }
    public string identifier = "waterSellerScav", displayName = "Miner", clientFaction = "miners", wantedPosterId = "";
    public string spriteName = ""; public ClientIntent clientIntent = ClientIntent.SELL;
    public bool isWanted, isStealthWanted, isSentenceOfficer, isBusinessComplete, isRegularSellDisable, isMultiBuyDisabled;
    public bool IsClientBuyingThisItem(GameItem item) => !item.Refused;
    public bool dismissable = true; public List<string> tag = new List<string>();
    public Dialogue mainDialogue = new Dialogue();
    public bool useClientBudget;
    public int Budget;
    public int GetBudget() => Budget;
    public void ModBudget(int delta) { Budget += delta; }
    public Func<bool> tryInterceptDismiss;
}
public class StoreClientInstance {
    public StoreClient Client = new StoreClient(); public StoreClient GetClientBlueprint() => Client;
}
public class PlayerStore {
    public static PlayerStore Instance = new PlayerStore();
    public static bool HasInstance => Instance != null;
    public StoreClientInstance currentClientInstance = new StoreClientInstance();
    public List<StoreReputation> storeReputations = new List<StoreReputation> { new StoreReputation() };
    public int playerCash = 100, Commits;
    public bool ThrowOnBuy;
    public bool isClientBeingArrested, isHandlingShootout;
    public int Departures;
    public void TryPlayerDismissCurrentClient(bool callNextWhenPossible)
    {
        if (currentClientInstance?.Client.tryInterceptDismiss?.Invoke() == true) return;
        Departures++; currentClientInstance = null;
        EmporiumEntry.Instance.Items.RemoveAll(i => !i.Owned);
    }
    public void SellItem(GameItem item) {
        if (!FakeContext.Bridge.Bundle.AllowTransfer(item, true)) return;
        Commits++; int price = (int)item.GetNegociatedValue(); playerCash += price;
        currentClientInstance.Client.Budget -= price; item.Owned = false;
    }
    public void BuyItem(GameItem item) {
        if (!FakeContext.Bridge.Bundle.AllowTransfer(item, false)) return;
        Commits++; if (ThrowOnBuy) throw new Exception("simulated native failure");
        playerCash -= (int)item.GetNegociatedValue(); item.Owned = true;
        EmporiumEntry.Instance.Items.Remove(item);
    }
}
public class StoreStation { public static StoreStation Instance = new StoreStation(); public int dayCounter = 1; public StoreEventManager storeEventManager = new StoreEventManager(); }
public class EmporiumEntry {
    public static EmporiumEntry Instance = new EmporiumEntry(); public List<GameItem> Items = new List<GameItem>();
    public GameInventory backInvinvElement = new GameInventory(), Front = new GameInventory();
    public GameInventory frontInvinvElement => Front;
    public List<GameItem> GetCountersItems() => Items;
    public List<GameItem> GetBackInvItems() => Items.FindAll(i => i.parentInventory == backInvinvElement);
}
public class GameItem {
    public TagSystem state = new TagSystem(), modifiedState = new TagSystem();
    public HashSet<string> Tags = new HashSet<string>();
    public bool IsTag(string tag) => Tags.Contains(tag);
    public int LiquidUL, CapacityUL;
    public List<ItemFeature> itemFeatures => Features;
    public int Id, unitCount = 1; public string name = "water", shortDescription = "bottled water";
    public bool Owned, Locked, Refused; public long Price = 10;
    public GameInventory parentInventory; public GridShape modifiedShape = new GridShape();
    public List<GameItem> Children = new List<GameItem>();
    public List<ItemFeature> Features = new List<ItemFeature>();
    public void AddItemFeature(ItemFeature f) => Features.Add(f);
    public void RemoveItemFeatureByID(string id) => Features.RemoveAll(f => f.identifier == id);
    public ItemFeature FindItemFeatureByID(string id) => Features.Find(f => f.identifier == id);
    public int GetUniqueID() => Id;
    public long GetNegociatedValue() => FakeContext.Bridge?.Bundle.Price(this, Price) ?? Price;
    public bool MayRemove() => !Locked;
    public List<GameItem> FindAllChildItems() => Children;
}
public class ItemFeature {
    public bool isDisabled, Hidden, Exposed, Shown = true;
    public string PublicLabel = "Public", SecretLabel = "Secret";
    public bool IsInitiallyShown() => Shown;
    public bool GetPublicHidden() => Hidden;
    public bool IsExposed() => Exposed;
    public string GetPublicDisplay() => PublicLabel;
    public string GetActualDisplay() => SecretLabel;
    public string GetExposedString() => SecretLabel;
    public enum FeatureType { ClientTemporary }
    public enum ValueStage { Final }
    public string identifier, category; public ValueStage valueStage; public bool isExposable;
    public ItemFeature(FeatureType t) {}
    public ItemFeature SetPublicDisplay(string s) => this;
    public ItemFeature SetActualDisplay(string s) => this;
    public ItemFeature SetPreExposedValueMod(int i) => this;
}
public static class GeneralHelper { public static bool IsItemOwned(GameItem item) => item.Owned; }
public class StoreReputation {
    public string factionId = "miners"; public int Value;
    public int GetReputation() => Value;
    public static void ModReputationRaw(string faction, int v, bool notify) {
        PlayerStore.Instance.storeReputations.Find(r => r.factionId == faction).Value += v;
    }
}
public class Locale { public LocaleCode Identifier = new LocaleCode(); }
public class LocaleCode { public string Code = "zh"; }
public static class ModHelper {
    public static Dictionary<string, int> Data = new Dictionary<string, int>();
    public static bool Outside;
    public static bool IsOutsideStore() => Outside;
    public static int GetModDataInt(string mod, string key) => Data.TryGetValue(key, out int v) ? v : 0;
    public static void SetModDataInt(string mod, string key, int v) => Data[key] = v;
    public static Locale GetCurrentLocale() => new Locale();
}

public static class StoreClientListDict { public static Dictionary<string, Func<StoreClient>> storeClientDict = new Dictionary<string, Func<StoreClient>>(); }
public static class StoreClientListStory { public static StoreClient ScriptedSeller() => new StoreClient(); }

public static class FakeContext { public static GameBridge Bridge; }
public class GridShape {}
public class TagSystem { public Dictionary<string,TagState> dict = new Dictionary<string,TagState>(); }
public class TagState {
    public bool valueEnabled, valueBool; public string identifierName, valueString;
    public int valueInt; public long valueLong; public float valueFloat; public double valueDouble;
}
public static class WaterHelper {
    public static int GetCurrentCapacityUL(GameItem i) => i.LiquidUL;
    public static int GetTotalCapacity(GameItem i) => i.CapacityUL;
    public static bool IsEmpty(GameItem i) => i.LiquidUL == 0;
}
public static class DialogUIManager { public static string RemoveTag(string text) => text; }
public class GameInventory {
    public int FreeSlots = 100;
    public SlotMarker TryFindOneValidInventorySlot(GameItem item) => FreeSlots <= 0 ? null :
        new SlotMarker { item = item, inventory = this, itemGridShape = new GridShape(), numTransfer = 1 };
}
public class SlotMarker {
    public GameItem item; public GameInventory inventory; public GridShape itemGridShape; public int numTransfer;
    public int TryAcceptOnce(int amount) {
        if (inventory.FreeSlots <= 0) return 0;
        if (item.parentInventory != null) item.parentInventory.FreeSlots++;
        inventory.FreeSlots--; item.parentInventory = inventory; item.modifiedShape = itemGridShape; return 1;
    }
}

// Stub only tests bridge serialization. Production calls the native game's helper.
public static class LocGenderHelper {
    public enum LocGender { Neutral, Male, Female }
    public enum LocRegister { Formal, Informal }
    public static LocGender GetGenderFromSpriteName(string s) => s=="test_female" ? LocGender.Female : s=="test_male" ? LocGender.Male : LocGender.Neutral;
    public static LocRegister GetClientRegister(StoreClient c) => LocRegister.Informal;
}
public class StoreEvent { public string newsName, newsDescription; public bool isHiddenFromNewsPaper; }
public class StoreEventManager {
    public List<StoreEvent> Events = new List<StoreEvent>();
    public List<StoreEvent> GetActiveEvents() => new List<StoreEvent>(Events);
}