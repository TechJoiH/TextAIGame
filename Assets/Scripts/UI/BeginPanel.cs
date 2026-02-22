using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BeginPanel : BasePanel
{
    [Header("UI Buttons")]
    public Button btnBegin;
    public Button btnExit;
    public Button btnSet;

    public override void Init()
    {
        BindButton(btnBegin, OnClickBegin);
        BindButton(btnSet, OnClickSet);
        BindButton(btnExit, OnClickExit);
    }

    private static void BindButton(Button btn, UnityEngine.Events.UnityAction onClick)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onClick);
    }

    private void OnClickBegin()
    {
        HideMe();

        if (GameLoop.Instance != null)
            GameLoop.Instance.StartNewGame();
        else
            Debug.LogError("[BeginPanel] 未找到 GameLoop.Instance，无法开始游戏。");
    }

    private void OnClickSet()
    {
        var panel = UIMgr.Instance.ShowPanel<SetPanel>();
        if (panel == null)
        {
            Debug.LogWarning("[BeginPanel] 打开 SetPanel 失败：请确认预制体路径为 Resources/UI/SetPanel，且根物体挂有 SetPanel 组件。");
            return;
        }

        panel.SetOpenFromGame(false);
    }

    private void OnClickExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
