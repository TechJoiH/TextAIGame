using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameMgr
{
    private static GameMgr instance = new GameMgr();
    public static GameMgr Instance => instance;
    private GameMgr() { }
    public void Init()
    {
        Debug.Log("GameMgr Init");
    }
}
