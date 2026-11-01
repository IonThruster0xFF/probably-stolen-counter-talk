using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CounterTalk
{
    // Keep the native tablet's rows, totals and buttons unchanged.
    [HarmonyPatch(typeof(GameItem), nameof(GameItem.GetNegociatedValue))]
    public static class NegotiatedPrice
    {
        public static void Postfix(GameItem __instance, ref long __result)
        {
            var bundle = ChatController.Current?.Bridge?.Bundle;
            if (bundle != null) __result = bundle.Price(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.BuyMultipleItem))]
    public static class NativeBuyBundle
    {
        public static bool Prefix(List<GameItem> items, ref NativeBundle __state) => Begin(items, false, ref __state);
        public static bool Begin(List<GameItem> items, bool selling, ref NativeBundle state)
        {
            var bundle = ChatController.Current?.Bridge?.Bundle;
            if (bundle?.Active != true) return true;
            if (NegociationUIManager.IsTradeLocked() || !bundle.Begin(items, selling)) return false;
            if (bundle.Executing) state = bundle;
            return true;
        }
        public static Exception Finalizer(Exception __exception, NativeBundle __state)
        {
            if (__state != null) ChatController.Current?.OnNativeTrade(__state.Finish(__exception != null));
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.SellMultiple))]
    public static class NativeSellBundle
    {
        public static bool Prefix(List<GameItem> items, ref NativeBundle __state) => NativeBuyBundle.Begin(items, true, ref __state);
        public static Exception Finalizer(Exception __exception, NativeBundle __state) => NativeBuyBundle.Finalizer(__exception, __state);
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.BuyItem))]
    public static class NativeBuyGuard
    {
        public static bool Prefix(PlayerStore __instance, GameItem item, ref TradeJournal.Capture __state)
        {
            if (ChatController.Current?.Bridge?.Bundle.AllowTransfer(item, false) == false) return false;
            __state = TradeJournal.Begin(__instance, item, false);
            return true;
        }
        public static Exception Finalizer(Exception __exception, TradeJournal.Capture __state)
        {
            TradeJournal.Complete(__state, __exception != null);
            return __exception;
        }
    }
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SellItem))]
    public static class NativeSellGuard
    {
        public static bool Prefix(PlayerStore __instance, GameItem item, ref TradeJournal.Capture __state)
        {
            if (ChatController.Current?.Bridge?.Bundle.AllowTransfer(item, true) == false) return false;
            __state = TradeJournal.Begin(__instance, item, true);
            return true;
        }
        public static Exception Finalizer(Exception __exception, TradeJournal.Capture __state) => NativeBuyGuard.Finalizer(__exception, __state);
    }

    // These native paths change money/ownership directly without calling PlayerStore.BuyItem.
    [HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.OnWholesaleClick))]
    public static class WholesaleJournal
    {
        public static void Prefix(ref TradeJournal.BatchCapture __state)
        {
            if (PlayerStore.HasInstance && EmporiumEntry.Instance != null)
                __state = TradeJournal.BeginBatch(PlayerStore.Instance, EmporiumEntry.Instance.GetAllNonOwnedItem());
        }
        public static Exception Finalizer(Exception __exception, TradeJournal.BatchCapture __state)
        {
            TradeJournal.CompleteBatch(__state, __exception != null);
            return __exception;
        }
    }
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.BuyAllItemFromTable))]
    public static class LegacyBuyAllJournal
    {
        public static void Prefix(PlayerStore __instance, ref TradeJournal.BatchCapture __state)
        {
            if (EmporiumEntry.Instance != null) __state = TradeJournal.BeginBatch(__instance, EmporiumEntry.Instance.GetBackInvItems());
        }
        public static Exception Finalizer(Exception __exception, TradeJournal.BatchCapture __state) => WholesaleJournal.Finalizer(__exception, __state);
    }

    // Wholesale and legacy single-item shortcuts must not bypass a complete package quote.
    [HarmonyPatch]
    public static class BundleShortcutGuard
    {
        public static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(NegociationUIManager), nameof(NegociationUIManager.OnWholesaleClick));
            yield return AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.BuyAllItemFromTable));
            yield return AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.BuyItemFromTable));
            yield return AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.SellItemFromTable));
        }
        public static bool Prefix() => ChatController.Current?.Bridge?.Bundle.Ready != true;
    }

    [HarmonyPatch(typeof(NegociationUIManager), "BuildSignature")]
    public static class TabletOfferSignature
    {
        public static void Postfix(ref string __result) => __result += "|counter_talk:" + ChatController.Current?.Bridge?.Bundle.Id;
    }
}
