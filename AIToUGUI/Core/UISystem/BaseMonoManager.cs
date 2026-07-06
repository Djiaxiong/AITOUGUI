using UnityEngine;

public abstract class BaseMonoManager<T> : MonoBehaviour where T : BaseMonoManager<T>
{
    private static T _instance;
    private static bool _isQuitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetStatics()
    {
        _instance = null;
        _isQuitting = false;
    }

    public static T Instance
    {
        get
        {
            if (_isQuitting)
            {
                return null;
            }

            if (_instance != null)
            {
                return _instance;
            }

#if UNITY_6000_0_OR_NEWER
            _instance = FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            _instance = FindObjectOfType<T>(true);
#endif
            if (_instance != null)
            {
                return _instance;
            }

            var go = new GameObject($"{typeof(T).Name} (Singleton)");
            _instance = go.AddComponent<T>();
            DontDestroyOnLoad(go);
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = (T)this;
            DontDestroyOnLoad(gameObject);
            Init();
            return;
        }

        if (_instance != this)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError($"[Singleton] {typeof(T).Name} duplicate instance detected. Keep '{_instance.gameObject.name}', destroy component on '{gameObject.name}'.");
#endif
            Destroy(this);
        }
    }

    protected virtual void OnApplicationQuit()
    {
        _isQuitting = true;
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public abstract void Init();
}
