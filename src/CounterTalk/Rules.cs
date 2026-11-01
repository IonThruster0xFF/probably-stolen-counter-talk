using System;

namespace CounterTalk
{
    // Pure policy functions: deliberately independent of Unity and model output.
    public static class Rules
    {
        public static bool CanOffer(long basePrice, long price, long spent, long budget) =>
            basePrice >= 0 && price >= 0 && price <= basePrice && spent >= 0 &&
            budget >= 0 && basePrice - price <= Math.Max(0, budget - spent);

        public static int RelationshipDelta(int current, int requested) =>
            Math.Max(-2, Math.Min(2, Math.Max(-20 - current, Math.Min(20 - current, requested))));

        public static bool CanRecommend(int dayUsed, int lifetimeUsed, int currentReputation) =>
            dayUsed < 1 && lifetimeUsed < 5 && currentReputation < 20;

        public static bool ValidEndpoint(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var u) &&
            u.Scheme == "https" && string.IsNullOrEmpty(u.UserInfo) && string.IsNullOrEmpty(u.Query) &&
            string.IsNullOrEmpty(u.Fragment);
    }
}
