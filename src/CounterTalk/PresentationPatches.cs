using System.Collections.Generic;
using HarmonyLib;
using TMPro;

namespace CounterTalk
{
    [HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
    public static class MaskKeyPatch
    {
        public static void Postfix(LoadedMod mod, CustomUIWindow __result)
        {
            if (mod?.Manifest.ID != ModEntry.Id || __result == null) return;
            var input = __result.Get("api_key")?.input;
            if (input == null) return;
            input.contentType = TMP_InputField.ContentType.Password;
            input.ForceLabelUpdate();
        }
    }

    // Capture dialogue that really started, never traverse unrevealed dialogue branches.
    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.UpdateDialogHistory))]
    public static class SpokenDialogue
    {
        private static StoreClientInstance visit;
        private static readonly List<string> lines = new List<string>();
        public static void Postfix(string text)
        {
            var current = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance : null;
            if (!ReferenceEquals(current, visit)) { visit = current; lines.Clear(); }
            if (current == null || string.IsNullOrWhiteSpace(text) || text.StartsWith("[Counter Talk]")) return;
            lines.Add(DialogUIManager.RemoveTag(text));
            if (lines.Count > 40) lines.RemoveAt(0);
        }
        public static string Read(StoreClientInstance current) =>
            ReferenceEquals(current, visit) ? string.Join("\n", lines) : "";
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.OnAdvancePressed))]
    public static class ChatInputAdvanceGuard
    {
        public static bool Prefix() => ChatController.Current?.InputFocused != true;
    }

    [HarmonyPatch(typeof(TMP_InputField), "KeyPressed")]
    public static class ChatReturnKey
    {
        // Skipping returns the default EditState.Continue, preserving focus.
        public static bool Prefix(TMP_InputField __instance, UnityEngine.Event evt) =>
            ChatController.Current?.HandleInputKey(__instance, evt) != true;
    }
}
