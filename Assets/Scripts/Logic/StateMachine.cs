using System.Collections.Generic;
using UnityEngine;

// T 代表拥有者
public class StateMachine<T>
{
    private T owner;
    private Stack<IState<T>> stateStack = new Stack<IState<T>>();

    public StateMachine(T owner)
    {
        this.owner = owner;
    }

    // 压入新状态
    public void PushState(IState<T> newState)
    {
        if (stateStack.Count > 0)
            stateStack.Peek().Exit(owner);

        stateStack.Push(newState);
        newState.Enter(owner);
    }

    // 弹出当前状态
    public void PopState()
    {
        if (stateStack.Count > 0)
        {
            IState<T> oldState = stateStack.Pop();
            oldState.Exit(owner);
        }

        if (stateStack.Count > 0)
        {
            stateStack.Peek().Enter(owner);
        }
    }

    // 切换状态（完全替换，不保留历史）
    public void ChangeState(IState<T> newState)
    {
        // 清空栈
        while (stateStack.Count > 0)
        {
            stateStack.Pop().Exit(owner);
        }
        stateStack.Push(newState);
        newState.Enter(owner);
    }

    public IState<T> GetCurrentState()
    {
        return stateStack.Count > 0 ? stateStack.Peek() : null;
    }

    // 在 Owner 的 Update 中调用
    public void Update()
    {
        if (stateStack.Count > 0)
        {
            stateStack.Peek().Update(owner);
        }
    }
}