using System;
using System.Collections.Generic;
using UnityEngine;

public class EventCenter 
{
    private static EventCenter eventCenter=new EventCenter();
    public static EventCenter Instance=> eventCenter;
    private EventCenter() { }
    // 字典存储所有的事件监听器
    // Key: 事件名字 (如 "MonsterDie")
    // Value: 对应的一堆函数 (Delegate)
    private Dictionary<string, Delegate> eventDic = new Dictionary<string, Delegate>();

    // ================= 1. 添加监听 (Sub) =================

    // 无参数
    public void AddListener(string name, Action action)
    {
        if (eventDic.ContainsKey(name)) eventDic[name] = (Action)eventDic[name] + action;
        else eventDic.Add(name, action);
    }

    // 1个参数 (泛型)
    public void AddListener<T>(string name, Action<T> action)
    {
        if (eventDic.ContainsKey(name)) eventDic[name] = (Action<T>)eventDic[name] + action;
        else eventDic.Add(name, action);
    }

    // (如果需要2个、3个参数，依此类推写 AddListener<T, K>...)

    // ================= 2. 移除监听 (UnSub) =================
    // ⚠️ 记得在 OnDestroy 里移除，否则会报错！

    public void RemoveListener(string name, Action action)
    {
        if (eventDic.ContainsKey(name))
        {
            eventDic[name] = (Action)eventDic[name] - action;
            if (eventDic[name] == null) eventDic.Remove(name);
        }
    }

    public void RemoveListener<T>(string name, Action<T> action)
    {
        if (eventDic.ContainsKey(name))
        {
            eventDic[name] = (Action<T>)eventDic[name] - action;
            if (eventDic[name] == null) eventDic.Remove(name);
        }
    }

    // ================= 3. 触发事件 (Pub) =================

    public void Broadcast(string name)
    {
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as Action)?.Invoke();
        }
    }

    public void Broadcast<T>(string name, T info)
    {
        if (eventDic.ContainsKey(name))
        {
            (eventDic[name] as Action<T>)?.Invoke(info);
        }
    }

    // 切换场景时清空
    public void Clear()
    {
        eventDic.Clear();
    }
}