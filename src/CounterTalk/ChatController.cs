using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace CounterTalk
{
    public sealed class ChatController : MonoBehaviour
    {
        private CustomUIWindow window;
        private GameBridge bridge;
        private PlayerStore lastStore;
        private StoreClientInstance lastVisit;
        private readonly List<JObject> history = new List<JObject>();
        private readonly List<string> transcript = new List<string>();
        private bool busy;
        private bool submitQueued;
        private FullScreenMode inputScreenMode;
        private int inputScreenWidth, inputScreenHeight;
        private bool screenCaptured;
        private float restoreScreenUntil;
        private readonly Queue<Dialogue> bubbles = new Queue<Dialogue>();
        private Dialogue farewell;
        private float farewellShownAt = -1;
        public static ChatController Current { get; private set; }
        public GameBridge Bridge => bridge;
        public bool InputFocused => window != null && window.IsAlive && window.IsVisible && window.Get("input")?.input?.isFocused == true;
        private UnityWebRequest activeRequest;
        private int epoch;
        private string input = "";
        private bool Zh => ModHelper.GetCurrentLocale()?.Identifier.Code == "zh";
        private string L(string zh, string en) => Zh ? zh : en;

        private void Update()
        {
            // Instance is a lazy factory, NOT a nullable read. Never construct a save in the menu.
            var store = PlayerStore.HasInstance && EmporiumEntry.Instance != null && StoreUIManager.Instance != null
                ? PlayerStore.Instance : null;
            var visit = store?.currentClientInstance;
            if (visit != null && !ClientEligibility.CanChat(visit.GetClientBlueprint()))
            {
                if (bridge != null || window != null) Reset();
                lastStore = store; lastVisit = visit;
                return;
            }
            if (!ReferenceEquals(store, lastStore) || !ReferenceEquals(visit, lastVisit))
            {
                Reset(); lastStore = store; lastVisit = visit;
            }
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (window != null && window.IsAlive && window.IsVisible) window.Hide(); else Open();
            }
            bridge?.Bundle.Validate();
            if (submitQueued) { submitQueued = false; Send(); }
            if (InputFocused && !screenCaptured)
            {
                inputScreenMode = Screen.fullScreenMode;
                inputScreenWidth = Screen.width; inputScreenHeight = Screen.height;
                screenCaptured = true;
            }
            if (!InputFocused && Time.unscaledTime > restoreScreenUntil) screenCaptured = false;
            if (bridge != null && bridge.Valid && bubbles.Count > 0 && DialogUIManager.Instance != null)
            {
                var dialog = DialogUIManager.Instance;
                var line = dialog.currentDialog;
                bool safe = line == null || (line.nextDialogue == null && line.choices.Count == 0 &&
                    (line.endAction == null || line.isEndActionExecuted) && !dialog.IsLineStillDisplaying(line));
                if (safe && dialog.StartDialogue(bubbles.Peek()))
                {
                    var shown = bubbles.Dequeue();
                    if (ReferenceEquals(shown, farewell)) farewellShownAt = Time.realtimeSinceStartup;
                }
            }
            if (bridge != null && bridge.DeparturePending && farewellShownAt >= 0 && DialogUIManager.Instance?.currentDialog != null &&
                !ReferenceEquals(DialogUIManager.Instance.currentDialog, farewell))
            {
                bridge.CancelDeparture(); farewell = null; farewellShownAt = -1;
            }
            if (bridge != null && bridge.Valid && bridge.DeparturePending && farewellShownAt >= 0 &&
                Time.realtimeSinceStartup - farewellShownAt >= 3f && DialogUIManager.Instance != null &&
                !DialogUIManager.Instance.IsLineStillDisplaying(farewell))
            {
                var result = bridge.CompleteDeparture();
                farewell = null; farewellShownAt = -1;
                if ((string)result["status"] != "departed") Note(L("顾客离店被游戏流程阻止。", "Departure blocked by game state."));
            }
        }

        private void Awake() { Current = this; }

        private void LateUpdate()
        {
            if (screenCaptured && Time.unscaledTime < restoreScreenUntil && Screen.fullScreenMode != inputScreenMode)
                Screen.SetResolution(inputScreenWidth, inputScreenHeight, inputScreenMode);
        }

        public bool HandleInputKey(TMP_InputField field, Event evt)
        {
            if (!InputFocused || window.Get("input").input != field ||
                (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)) return false;
            if (!string.IsNullOrEmpty(Input.compositionString)) return false;
            if (evt.alt)
            {
                int start = Math.Min(field.selectionStringAnchorPosition, field.selectionStringFocusPosition);
                int end = Math.Max(field.selectionStringAnchorPosition, field.selectionStringFocusPosition);
                if (field.text.Length - (end - start) < field.characterLimit)
                {
                    field.text = field.text.Substring(0, start) + "\n" + field.text.Substring(end);
                    field.stringPosition = field.selectionStringFocusPosition = start + 1;
                }
                restoreScreenUntil = Time.unscaledTime + 1f;
            }
            else submitQueued = true;
            evt.Use();
            return true;
        }

        private void Reset()
        {
            epoch++;
            activeRequest?.Abort();
            StopAllCoroutines();
            activeRequest?.Dispose(); activeRequest = null;
            bridge?.Cancel();
            busy = false; bridge = null; history.Clear(); transcript.Clear(); bubbles.Clear(); input = ""; submitQueued = false;
            farewell = null; farewellShownAt = -1;
            window?.Close(); window = null;
        }
        private void OnDestroy() { Reset(); if (Current == this) Current = null; }

        public void Open()
        {
            if (!PlayerStore.HasInstance || EmporiumEntry.Instance == null || StoreUIManager.Instance == null ||
                PlayerStore.Instance.currentClientInstance == null || ModHelper.IsOutsideStore()) return;
            if (!ClientEligibility.CanChat(PlayerStore.Instance.currentClientInstance.GetClientBlueprint())) return;
            if (!ReferenceEquals(lastStore, PlayerStore.Instance) || !ReferenceEquals(lastVisit, PlayerStore.Instance.currentClientInstance))
            {
                Reset(); lastStore = PlayerStore.Instance; lastVisit = lastStore.currentClientInstance;
            }
            if (bridge == null) bridge = new GameBridge();
            if (window != null && window.IsAlive) { window.Show(); window.BringToFront(); return; }
            var b = CustomUIManager.Window(ModEntry.Id + ".chat", L("回车发送 · Alt+Enter 换行", "Enter: send · Alt+Enter: newline"))
                .SetSize(540, 155).SetPosition(new Vector2(0, -280))
                .AddInput(L("和顾客说些什么…", "Talk to the customer…"), input, v => input = v, "input", v => input = v);
            window = b.Show();
            window.Get("input").input.characterLimit = 2000;
            window.Get("input").input.lineType = TMP_InputField.LineType.MultiLineNewline;
            window.Get("input").SetPreferredSize(-1, 85);
            window.Get("input").input.onValidateInput = (text, index, ch) =>
            {
                if (ch != '\n' && ch != '\r') return ch;
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                if (alt) return '\n';
                // Pasted newlines remain newlines; IME confirmation is not a send action.
                if (!(Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter))) return ch;
                if (string.IsNullOrEmpty(Input.compositionString)) submitQueued = true;
                return '\0';
            };
            window.Get("input").input.ActivateInputField();
            Refresh();
        }

        private void Refresh()
        {
            if (window == null || !window.IsAlive) return;
            window.SetTitle(busy ? L("顾客正在思考…", "Thinking…") : L("回车发送 · Alt+Enter 换行", "Enter: send · Alt+Enter: newline"));
        }
        private void Note(string text)
        {
            // Technical errors go to the native dialogue history, not into the NPC's mouth.
            if (DialogUIManager.Instance != null && PlayerStore.HasInstance)
                DialogUIManager.Instance.UpdateDialogHistory("[Counter Talk] " + Plain(text));
            Refresh();
        }
        private static string Plain(string text) => (text ?? "").Replace("<", "〈").Replace(">", "〉");
        private void Speak(string speaker, string text)
        {
            if (bridge == null || !bridge.Valid) return;
            bubbles.Enqueue(new Dialogue().SetText(speaker, Plain(text)));
        }
        private static JObject Message(string role, string text) => new JObject { ["role"] = role, ["content"] = text };

        private void Send()
        {
            if (busy || bridge == null || !bridge.Valid || bridge.DeparturePending || string.IsNullOrWhiteSpace(input)) return;
            bridge.BeginPlayerTurn();
            string text = input.Trim(); input = ""; window?.Get("input").SetString("");
            Speak("PLAYER", text);
            var timeline = bridge.TurnTradeContext();
            if (((JArray)timeline["completed_since_previous_player_message_before_current_message"]).Count > 0)
                history.Add(Message("system", "TRADE TIMELINE BEFORE THE FOLLOWING PLAYER MESSAGE: " + timeline.ToString(Formatting.None)));
            history.Add(Message("user", text));
            StartCoroutine(Run());
        }

        public void OnNativeTrade(JObject result)
        {
            if (result == null) return;
            history.Add(Message("system", "AUTHORITATIVE NATIVE TRADE RESULT: " + result.ToString(Formatting.None)));
            if (result["error"] != null) Note(L("交易未完整完成，请检查物品；本次会话工具已锁定。", "Trade incomplete; inspect items. Tools locked for this visit."));
        }
        private void CancelRequest()
        {
            epoch++; activeRequest?.Abort(); StopAllCoroutines();
            activeRequest?.Dispose(); activeRequest = null; busy = false;
            bridge?.Cancel();
            history.Add(Message("system", "Request cancelled. Unconfirmed proposals are cancelled; any completed game events remain real."));
            Note(L("请求已取消，已完成的交易不会撤销。", "Request cancelled; completed transactions are not undone."));
        }

        private IEnumerator Run()
        {
            var routine = RunCore();
            try
            {
                while (true)
                {
                    bool next;
                    try { next = routine.MoveNext(); }
                    catch (Exception exception)
                    {
                        Debug.LogWarning("[Counter Talk] Request exception type=" + exception.GetType().Name);
                        bridge?.Cancel();
                        Note(L("请求处理异常，未确认提案已取消。", "Request processing failed; unconfirmed proposal cancelled."));
                        break;
                    }
                    if (!next) break;
                    yield return routine.Current;
                }
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
                activeRequest?.Dispose(); activeRequest = null; busy = false; Refresh();
            }
        }

        private IEnumerator RunCore()
        {
            string endpoint = ModEntry.Endpoint.Value.Trim();
            string key = ModEntry.Key.Value.Trim();
            if (!Rules.ValidEndpoint(endpoint) || string.IsNullOrWhiteSpace(key))
            {
                Note(L("请在模组设置中填写 HTTPS 接口地址、模型名和 API Key。", "Configure an HTTPS endpoint, model and API key in Mods settings."));
                yield break;
            }
            busy = true; Refresh(); int token = epoch;
            // Trim only at complete turn boundaries. Every request also includes a fresh authoritative snapshot.
            if (history.Count > 36) history.RemoveRange(0, history.Count - 36);
            for (int step = 0; step < 5; step++)
            {
                if (!bridge.Valid || token != epoch) break;
                JObject snapshot = null;
                int tradeRevision = bridge.TradeRevision;
                try { snapshot = bridge.Snapshot(); } catch { }
                if (snapshot == null) { Note(L("读取游戏状态失败。", "Could not read game state.")); break; }
                var messages = new JArray { Message("system", Prompt.Text) };
                foreach (var h in history) messages.Add(h.DeepClone());
                messages.Add(Message("system", "CURRENT GAME STATE (latest; overrides stale numeric facts in all earlier messages): " + snapshot.ToString(Formatting.None)));
                var body = new JObject { ["model"] = ModEntry.Model.Value.Trim(), ["messages"] = messages,
                    ["response_format"] = new JObject { ["type"] = "json_object" }, ["max_tokens"] = 1200,
                    ["temperature"] = 0.8, ["stream"] = false };
                if (RequestPolicy.IsDeepSeek(endpoint)) body["thinking"] = new JObject { ["type"] = "disabled" };
                string raw = null;
                bool ok = false;
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    if (token != epoch || !bridge.Valid) break;
                    float started = Time.realtimeSinceStartup;
                    activeRequest = new UnityWebRequest(endpoint, "POST");
                    activeRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None)));
                    activeRequest.downloadHandler = new DownloadHandlerBuffer();
                    activeRequest.SetRequestHeader("Content-Type", "application/json");
                    activeRequest.SetRequestHeader("Authorization", "Bearer " + key);
                    activeRequest.timeout = Math.Max(30, Math.Min(180, ModEntry.RequestTimeout.Value));
                    activeRequest.redirectLimit = 0;
                    yield return activeRequest.SendWebRequest();
                    if (token != epoch || !bridge.Valid) break;
                    var transport = activeRequest.result;
                    long status = activeRequest.responseCode;
                    bool timedOut = RequestPolicy.IsTimeout(activeRequest.error);
                    ok = transport == UnityWebRequest.Result.Success;
                    raw = ok ? activeRequest.downloadHandler.text : null;
                    // Never log credentials, request/response bodies, or arbitrary server error text.
                    Debug.Log("[Counter Talk] request step=" + step + " attempt=" + attempt +
                        " http=" + status + " transport=" + transport + " timeout=" + timedOut +
                        " seconds=" + (Time.realtimeSinceStartup - started).ToString("F1") +
                        " bytes=" + activeRequest.downloadedBytes);
                    activeRequest.Dispose(); activeRequest = null;
                    if (ok) break;
                    string reason = timedOut ? L("等待模型回复超时", "Timed out waiting for model") :
                        status == 503 ? L("模型服务暂时过载（HTTP 503）", "Model service overloaded (HTTP 503)") :
                        transport == UnityWebRequest.Result.ConnectionError ? L("连接中断或网络不可达", "Connection interrupted or unavailable") :
                        transport == UnityWebRequest.Result.DataProcessingError ? L("响应下载或解码失败", "Response download or decoding failed") :
                        L("接口返回错误 HTTP ", "API returned HTTP ") + status;
                    if (!RequestPolicy.Retry(status, transport == UnityWebRequest.Result.ConnectionError, attempt))
                    {
                        Note(reason + L("。本轮停止，可稍后重发；原版交易仍可用。", ". Turn stopped; retry later. Native trading remains available."));
                        break;
                    }
                    Note(reason + L("，稍后自动重试一次。", "; retrying once shortly."));
                    int delay = 3;
                    // Only repeat the HTTP inference. No game action is executed inside this retry loop.
                    yield return new WaitForSecondsRealtime(delay);
                }
                if (token != epoch || !bridge.Valid) break;
                if (!ok) break;
                if (bridge.TradeRevision != tradeRevision)
                {
                    Debug.Log("[Counter Talk] native trade changed during inference; regenerating with completed transaction context");
                    continue;
                }
                JObject reply = null;
                bool truncated = false;
                try
                {
                    var envelope = JObject.Parse(raw);
                    truncated = (string)envelope["choices"]?[0]?["finish_reason"] == "length";
                    string content = (string)envelope["choices"]?[0]?["message"]?["content"];
                    if (!truncated && !string.IsNullOrWhiteSpace(content) && content.Length < 12000) reply = JObject.Parse(content);
                }
                catch { }
                if (reply == null)
                {
                    Debug.LogWarning("[Counter Talk] invalid_model_response truncated=" + truncated);
                    Note(truncated ? L("模型输出达到长度上限，未执行此动作。", "Model output truncated; this action was not executed.") :
                        L("模型响应为空或 JSON 无效，未执行此动作。", "Empty or invalid JSON response; this action was not executed."));
                    break;
                }
                history.Add(Message("assistant", reply.ToString(Formatting.None)));
                string action = reply.Value<string>("action");
                if (action == "say")
                {
                    Debug.Log("[Counter Talk] action=say quote_active=" + bridge.Bundle.Active + " tags_effective=" + bridge.Bundle.Ready);
                    string speech = reply.Value<string>("speech") ?? "";
                    if (SpeechPolicy.PricedAcceptance(speech) && !bridge.Bundle.Ready)
                    {
                        Debug.LogWarning("[Counter Talk] priced_acceptance_without_quote blocked");
                        history.Add(Message("system", "Your priced acceptance was NOT spoken: no effective quote exists. First check native_transaction_history: if you were referring to a COMPLETED trade, acknowledge it naturally without repeating a priced agreement (e.g. '钱货两清了，想聊什么？') and do NOT quote or transfer it again. For a NEW offer, submit propose_bundle with all actual item IDs, allocated prices and total BEFORE accepting. You may include speech in that tool action. If you cannot apply a new offer, decline without claiming agreement. Do not repeat a say priced acceptance."));
                        if (step == 4) Note(L("本轮未能生成有效报价，未更改交易价格。", "No valid quote was produced; trade prices were not changed."));
                        continue;
                    }
                    if (speech.Length > 2000) speech = speech.Substring(0, 2000);
                    Speak("CLIENT", speech); break;
                }
                JObject result;
                try { result = bridge.Tool(reply); }
                catch { result = GameBridge.Error("invalid_tool_arguments"); }
                // Only locally generated status codes; no raw model text or private request bodies.
                Debug.Log("[Counter Talk] tool status=" + ((string)result["status"] ?? "error") +
                    " error=" + ((string)result["error"] ?? "none") +
                    " direction=" + ((string)result["resolved_direction"] ?? "none") +
                    " quote_active=" + bridge.Bundle.Active + " tags_effective=" + bridge.Bundle.Ready);
                history.Add(Message("system", "AUTHORITATIVE TOOL RESULT: " + result.ToString(Formatting.None)));
                if ((string)result["status"] == "budget_adjusted") NegociationUIManager.Instance?.RefreshUI();
                if ((string)result["status"] == "departure_pending")
                {
                    farewell = new Dialogue().SetText("CLIENT", Plain(result.Value<string>("speech")));
                    farewellShownAt = -1; bubbles.Enqueue(farewell);
                    window?.Hide();
                    NegociationUIManager.Instance?.RefreshUI();
                    break;
                }
                if (bridge.Bundle.Active)
                {
                    NegociationUIManager.Instance?.OpenUI();
                    NegociationUIManager.Instance?.RefreshUI();
                }
                if ((string)result["status"] == "native_quote_applied" && !string.IsNullOrWhiteSpace(reply.Value<string>("speech")))
                {
                    string speech = reply.Value<string>("speech");
                    Speak("CLIENT", speech.Length > 2000 ? speech.Substring(0, 2000) : speech);
                    break;
                }
                Refresh();
                if (step == 4) Note(L("本轮工具调用已达上限，请继续对话。", "Tool limit reached for this turn; continue the conversation."));
            }
            activeRequest?.Dispose(); activeRequest = null;
            busy = false; Refresh();
        }
    }
}
