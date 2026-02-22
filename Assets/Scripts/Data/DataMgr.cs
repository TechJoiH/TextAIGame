using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DataMgr 
{
    // 单例实例（静态初始化，线程安全的简单实现）
    private static DataMgr instance = new DataMgr();

    // 对外的单例访问属性
    public static DataMgr Instance => instance;
    private DataMgr() 
    {
        // 私有构造函数，防止外部实例化
    }
}
