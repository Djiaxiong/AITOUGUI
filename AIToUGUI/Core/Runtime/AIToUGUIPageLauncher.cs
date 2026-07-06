using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AIToUGUIPageLauncher : MonoBehaviour
{
    [SerializeField] private string _runtimePageId = "page_id";
    [SerializeField] private bool _showOnStart = true;
    [SerializeField] private bool _showAsync;
    [SerializeField] private bool _clearLayerBeforeShow;
    [SerializeField] private bool _hideOnDisable;

    private Coroutine _showRoutine;

    private void Start()
    {
        if (_showOnStart)
        {
            ShowConfiguredPage();
        }
    }

    private void OnDisable()
    {
        StopShowRoutine();

        if (_hideOnDisable)
        {
            HideConfiguredPage();
        }
    }

    [ContextMenu("Show Configured Page")]
    public void ShowConfiguredPage()
    {
        if (string.IsNullOrWhiteSpace(_runtimePageId))
        {
            Debug.LogWarning("[AIToUGUIPageLauncher] runtimePageId is empty.", this);
            return;
        }

        StopShowRoutine();
        ClearLayerIfNeeded();

        if (_showAsync)
        {
            _showRoutine = StartCoroutine(ShowConfiguredPageAsyncCoroutine());
            return;
        }

        AIToUGUIRuntimeSystem.Instance.ShowPage(_runtimePageId);
    }

    [ContextMenu("Hide Configured Page")]
    public void HideConfiguredPage()
    {
        if (string.IsNullOrWhiteSpace(_runtimePageId))
        {
            return;
        }

        AIToUGUIRuntimeSystem.Instance.HidePage(_runtimePageId);
    }

    [ContextMenu("Reload Configured Page")]
    public void ReloadConfiguredPage()
    {
        HideConfiguredPage();
        ShowConfiguredPage();
    }

    private IEnumerator ShowConfiguredPageAsyncCoroutine()
    {
        var task = AIToUGUIRuntimeSystem.Instance.ShowPageAsync(_runtimePageId);
        while (!task.IsCompleted)
        {
            yield return null;
        }

        _ = task.GetResult();
        _showRoutine = null;
    }

    private void ClearLayerIfNeeded()
    {
        if (!_clearLayerBeforeShow)
        {
            return;
        }

        if (AIToUGUIRuntimeSystem.Instance.TryGetPageEntry(_runtimePageId, out var entry) && entry != null)
        {
            AIToUGUIRuntimeSystem.Instance.ClearLayer(entry.targetLayer);
        }
    }

    private void StopShowRoutine()
    {
        if (_showRoutine == null)
        {
            return;
        }

        StopCoroutine(_showRoutine);
        _showRoutine = null;
    }
}
