using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class HistorySlotItem : MonoBehaviour
{
    public TMP_Text timeText;
    public TMP_Text summaryText;
    public Button button;

    private string saveId;
    private UnityAction<string> onClick;

    public void Bind(string saveId, string timeDisplay, string summary, UnityAction<string> onClick)
    {
        this.saveId = saveId;
        this.onClick = onClick;

        if (timeText != null) timeText.text = timeDisplay ?? "";
        if (summaryText != null) summaryText.text = summary ?? "";

        if (button != null)
        {
            button.onClick.RemoveListener(OnButtonClicked);
            button.onClick.AddListener(OnButtonClicked);
        }
    }

    private void OnButtonClicked()
    {
        onClick?.Invoke(saveId);
    }
}