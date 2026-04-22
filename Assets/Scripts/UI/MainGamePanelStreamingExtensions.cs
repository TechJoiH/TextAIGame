using System.Runtime.CompilerServices;

public static class MainGamePanelStreamingExtensions
{
    private const string HiddenPlaceholder = "<alpha=#00>__ASSISTANT_STREAM_PLACEHOLDER__</alpha>";
    private static readonly ConditionalWeakTable<MainGamePanel, StreamState> StreamStates = new ConditionalWeakTable<MainGamePanel, StreamState>();

    public static void BeginAssistantStream(this MainGamePanel panel)
    {
        if (panel == null)
            return;

        StreamState state = StreamStates.GetOrCreateValue(panel);
        state.IsActive = true;
        state.LastVisibleText = string.Empty;
        panel.AppendText(HiddenPlaceholder, false);
    }

    public static void UpdateAssistantStream(this MainGamePanel panel, string text)
    {
        if (panel == null)
            return;

        StreamState state = StreamStates.GetOrCreateValue(panel);
        if (!state.IsActive)
            panel.BeginAssistantStream();

        string previousWrappedText = WrapAssistantText(string.IsNullOrEmpty(state.LastVisibleText) ? HiddenPlaceholder : state.LastVisibleText);
        string nextVisibleText = text ?? string.Empty;
        string nextWrappedText = WrapAssistantText(nextVisibleText);

        panel.ReplaceLastStreamContent(previousWrappedText, nextWrappedText);
        state.LastVisibleText = nextVisibleText;
        state.IsActive = true;
    }

    public static void CompleteAssistantStream(this MainGamePanel panel, string text)
    {
        if (panel == null)
            return;

        panel.UpdateAssistantStream(text);

        StreamState state = StreamStates.GetOrCreateValue(panel);
        state.IsActive = false;
        state.LastVisibleText = string.Empty;
    }

    private static string WrapAssistantText(string text)
    {
        return $"<color=#000000>{text}</color>";
    }

    private sealed class StreamState
    {
        public bool IsActive;
        public string LastVisibleText = string.Empty;
    }
}
