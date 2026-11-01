using UnityEngine;

namespace CounterTalk
{
    public sealed class ModEntry : IMod
    {
        public const string Id = "counter_talk";
        internal static ConfigEntry<string> Endpoint, Model, Key;
        internal static ConfigEntry<int> Budget, RequestTimeout;
        private GameObject host;

        public void Init(ModManifest manifest)
        {
            var config = ModHelper.GetConfig(Id);
            Endpoint = config.Define("endpoint", "https://api.deepseek.com/chat/completions", "API", () => "API endpoint (HTTPS)");
            Model = config.Define("model", "deepseek-flash", "API", () => "Model / 模型");
            Key = config.Define("api_key", "", "API", () => "API Key (local plaintext / 本地明文保存)");
            RequestTimeout = config.Define("request_timeout_seconds", 90, "API", () => "Request timeout (seconds) / 请求超时秒数", 30, 180);
            Budget = config.Define("daily_concession", 20, "Gameplay", () => "Per-customer concession allowance / 每位顾客让利额度", 0, 200);
            new ModLog(manifest).Log("Counter Talk 0.3.11: F6 input; Enter sends, Alt+Enter newline; native bubbles and negotiation tablet.");
        }

        public void OnEnable()
        {
            host = new GameObject("CounterTalk");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<ChatController>();
            ModHelper.AddDebugFunction(Id, ModHelper.DEBUG_CATEGORY_MODDING, "Counter Talk", () => host.GetComponent<ChatController>().Open());
        }

        public void OnDisable()
        {
            ModHelper.RemoveDebugFunctions(Id);
            if (host != null) Object.Destroy(host);
        }
    }
}
