using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace CounterTalk
{
    public static class ClientEligibility
    {
        private static readonly ConditionalWeakTable<StoreClient, object> protectedClients = new ConditionalWeakTable<StoreClient, object>();
        private static readonly HashSet<string> storyFactories = new HashSet<string>(StringComparer.Ordinal) {
            "StoreClientListStory", "StoreClientUniqueList", "StoreClientListWanted",
            "StoreClientListCartel", "StoreClientListChurch", "StoreClientListMorning"
        };
        private static readonly HashSet<string> scriptedMethods = new HashSet<string>(StringComparer.Ordinal) {
            "CreateInventor", "CreateInventorStorage", "CreateSupplierIntro", "CreateLandlordClient",
            "CreateLandlordVendingMachineClient", "CreateLoanSharkIntro", "CreateLoanShark2", "CreateLoanSharkCollect",
            "CreateBusinessPermit", "CreateRetiredJunkerIntro", "CreateBlackmailerIntro", "CreateBlackmailer",
            "CreateChemicalFeedbackClient", "CreateHirelingClientReturn", "CreateRevokePermitOfficer"
        };

        public static bool IsProtectedFactory(MethodInfo method) => method != null &&
            (storyFactories.Contains(method.DeclaringType?.Name ?? "") || scriptedMethods.Contains(method.Name));

        public static void Protect(StoreClient client)
        {
            if (client != null) protectedClients.GetValue(client, _ => new object());
        }

        public static bool CanChat(StoreClient client)
        {
            if (client == null || client.isWanted || client.isStealthWanted ||
                !string.IsNullOrEmpty(client.wantedPosterId) || client.isSentenceOfficer || !client.dismissable ||
                protectedClients.TryGetValue(client, out _)) return false;
            // Registry fallback also covers instances restored without running their original factory.
            if (!string.IsNullOrEmpty(client.identifier) &&
                StoreClientListDict.storeClientDict.TryGetValue(client.identifier, out var factory) &&
                IsProtectedFactory(factory?.Method)) return false;
            if (client.tag != null && client.tag.Any(t =>
                string.Equals(t, "story", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "quest", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t, "counter_talk.no_chat", StringComparison.OrdinalIgnoreCase))) return false;
            switch (client.clientIntent)
            {
                case StoreClient.ClientIntent.UNDEFINED:
                case StoreClient.ClientIntent.INSPECTION:
                case StoreClient.ClientIntent.RENT:
                case StoreClient.ClientIntent.LOAN_SHARK:
                case StoreClient.ClientIntent.SPECIAL: return false;
                default: return true;
            }
        }

        public static bool CanTrade(StoreClient client) => CanChat(client) && !client.isBusinessComplete &&
            !client.isRegularSellDisable && (client.clientIntent == StoreClient.ClientIntent.BUY ||
                client.clientIntent == StoreClient.ClientIntent.SELL || client.clientIntent == StoreClient.ClientIntent.SELLNBUY);
    }

    // Mark by creation provenance, including unregistered factory variants and later intent changes.
    [HarmonyPatch]
    public static class ProtectedClientFactories
    {
        public static IEnumerable<MethodBase> TargetMethods() => typeof(StoreClient).Assembly.GetTypes()
            .Where(t => t.Name.StartsWith("StoreClient", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(m => m.ReturnType == typeof(StoreClient) && ClientEligibility.IsProtectedFactory(m));
        public static void Postfix(StoreClient __result) => ClientEligibility.Protect(__result);
    }
}
