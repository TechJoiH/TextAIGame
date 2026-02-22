using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CanvasGroup))]
public abstract class BasePanel : MonoBehaviour
{
    protected CanvasGroup canvasGroup;
    public bool isShow { get; private set; }

    protected virtual void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    protected virtual void Start() { Init(); }
    public abstract void Init();

    // ================= 核心流程控制 =================

    public void ShowMe()
    {
        isShow = true;
        gameObject.SetActive(true);
        canvasGroup.blocksRaycasts = true; // 开启交互

        // 调用虚方法：具体怎么显示，子类自己定
        OnShowAnimation();
    }

    public void HideMe(UnityAction onComplete = null)
    {
        isShow = false;
        canvasGroup.blocksRaycasts = false; // 关闭交互，防止动画时误触

        // 调用虚方法：具体怎么隐藏，子类定。
        // 关键点：传一个 lambda 进去，子类动画播完必须调用这个 callback！
        OnHideAnimation(() =>
        {
            gameObject.SetActive(false);
            onComplete?.Invoke();
        });
    }


    /// <summary>
    /// 钩子：入场动画。
    /// 默认实现：瞬间显示（Alpha=1）。如果你想用 DOTween 或 Animation，请重写此方法。
    /// </summary>
    protected virtual void OnShowAnimation()
    {
        canvasGroup.alpha = 1;
    }

    /// <summary>
    /// 钩子：离场动画。
    /// 默认实现：瞬间隐藏，并立即调用结束回调。
    /// </summary>
    /// <param name="onAnimationEnd">动画结束时必须调用的回调！</param>
    protected virtual void OnHideAnimation(UnityAction onAnimationEnd)
    {
        canvasGroup.alpha = 0;
        // 必须调用这个，否则 HideMe 里的 SetActive(false) 永远不会执行
        onAnimationEnd?.Invoke();
    }
}