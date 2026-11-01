using System;

namespace CounterTalk
{
    public static class RequestPolicy
    {
        public static bool IsTimeout(string error) => error != null &&
            (error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
             error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0);
        public static bool Retry(long status, bool connectionError, int attempt) => attempt < 2 &&
            (status == 429 || status == 500 || status == 502 || status == 503 || status == 504 ||
             (connectionError && (status <= 0 || status == 200)));
        public static bool IsDeepSeek(string endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            uri.Scheme == "https" && uri.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase);
    }
}
