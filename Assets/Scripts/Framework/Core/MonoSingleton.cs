using UnityEngine;

// 用于需要挂载、需要生命周期的管理器
public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
{
    private static T _instance;
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                // 尝试在场景中查找
                _instance = FindObjectOfType<T>();

                // 如果没找到，自动创建一个
                if (_instance == null)
                {
                    GameObject obj = new GameObject(typeof(T).Name);
                    _instance = obj.AddComponent<T>();
                }
            }
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = (T)this;
            DontDestroyOnLoad(gameObject); // 保证切场景不销毁
        }
        else if (_instance != this)
        {
            Destroy(gameObject); // 防止重复存在
        }
    }
}