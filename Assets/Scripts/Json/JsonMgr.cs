using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum JsonType
{
    JsonUtility,
    LitJson,
}
public class JsonMgr
{
    private static JsonMgr instance = new JsonMgr();
    public static JsonMgr Instance => instance ??= new JsonMgr();
    private JsonMgr() { }

    //序列化
    public void SaveData(string fileName, object data,JsonType type=JsonType.LitJson)
    {
        string path = Application.persistentDataPath + "/"+fileName+".json";
        string jsonStr ="";
        switch (type)
        {
            case JsonType.JsonUtility:
                 jsonStr=JsonUtility.ToJson(data);
                break;
            case JsonType.LitJson:
                 jsonStr=LitJson.JsonMapper.ToJson(data);
                break;
        }
        File.WriteAllText(path, jsonStr);

    }
    //反序列化
    public T LoadData<T>(string fileName,JsonType type=JsonType.LitJson) where T : new()
    {
        string path = Application.streamingAssetsPath + "/" + fileName + ".json";
        if(!File.Exists(path))
        {
            path= Application.persistentDataPath + "/" + fileName + ".json";
        }
        if(!File.Exists(path)) 
            return new T();
        string jsonStr = File.ReadAllText(path);
        T data = default(T);
        switch (type)
        {
            case JsonType.JsonUtility:
                data=JsonUtility.FromJson<T>(jsonStr);
                break;
            case JsonType.LitJson:
                data= LitJson.JsonMapper.ToObject<T>(jsonStr);
                break;
                default: 
                break;
        }
        return data; 
    }
}
