using System.Collections.Generic;
using UnityEngine;

public class UIMgr : MonoSingleton<UIMgr>
{
    // 缓存已加载的面板
    private Dictionary<string, BasePanel> panelDic = new Dictionary<string, BasePanel>();
    private Transform canvasTransform;

    // 确保 Canvas 存在
    private Transform GetCanvas()
    {
        if (canvasTransform == null)
        {
            // 尝试找场景里的 Canvas
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                // 如果没有，加载预制体（你需要把 Canvas 预制体放在 Resources/UI/Canvas）
                GameObject canvasObj = Instantiate(Resources.Load<GameObject>("UI/Canvas"));
                canvas = canvasObj.GetComponent<Canvas>();
            }
            canvasTransform = canvas.transform;
            DontDestroyOnLoad(canvasTransform.gameObject);
        }
        return canvasTransform;
    }

    // 显示面板
    public T ShowPanel<T>() where T : BasePanel
    {
        string panelName = typeof(T).Name;

        // 如果已经加载过
        if (panelDic.ContainsKey(panelName))
        {
            panelDic[panelName].ShowMe();
            return panelDic[panelName] as T;
        }

        // 如果还没加载，去 Resources/UI 文件夹加载
        GameObject res = Resources.Load<GameObject>("UI/" + panelName);
        if (res == null)
        {
            Debug.LogError($"[UIManager] 找不到面板预制体: Resources/UI/{panelName}");
            return null;
        }

        GameObject panelObj = Instantiate(res, GetCanvas(), false);
        T panel = panelObj.GetComponent<T>();
        panelDic.Add(panelName, panel);
        panel.ShowMe();
        return panel;
    }

    // 隐藏面板
    public void HidePanel<T>(bool isDestroy = false) where T : BasePanel
    {
        string panelName = typeof(T).Name;
        if (panelDic.ContainsKey(panelName))
        {
            if (isDestroy)
            {
                panelDic[panelName].HideMe(() =>
                {
                    Destroy(panelDic[panelName].gameObject);
                    panelDic.Remove(panelName);
                });
            }
            else
            {
                panelDic[panelName].HideMe();
            }
        }
    }

    // 获取面板
    public T GetPanel<T>() where T : BasePanel
    {
        string panelName = typeof(T).Name;
        if (panelDic.ContainsKey(panelName)) return panelDic[panelName] as T;
        return null;
    }
}