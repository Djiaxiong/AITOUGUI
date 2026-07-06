using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class BaseUIController : MonoBehaviour
{
    private bool _inited;
    private bool _actionsBound;
#if UNITY_EDITOR
    private bool _editorRebindQueued;
#endif

    protected virtual void Awake()
    {
        InternalInit();
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            QueueEditorRebind();
        }
#endif
    }

    private void OnTransformChildrenChanged()
    {
        if (!_inited && Application.isPlaying)
        {
            return;
        }

        if (Application.isPlaying)
        {
            RefreshFieldBindings();
            OnBindingsRefreshed();
            return;
        }

#if UNITY_EDITOR
        QueueEditorRebind();
#endif
    }

#if UNITY_EDITOR
    private void Reset()
    {
        QueueEditorRebind();
    }

    protected virtual void OnValidate()
    {
        if (Application.isPlaying || transform == null)
        {
            return;
        }

        QueueEditorRebind();
    }

    private void QueueEditorRebind()
    {
        if (_editorRebindQueued)
        {
            return;
        }

        _editorRebindQueued = true;
        UnityEditor.EditorApplication.delayCall += PerformEditorRebind;
    }

    private void PerformEditorRebind()
    {
        UnityEditor.EditorApplication.delayCall -= PerformEditorRebind;
        _editorRebindQueued = false;

        if (this == null || transform == null || Application.isPlaying)
        {
            return;
        }

        RefreshFieldBindings();
        OnBindingsRefreshed();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    internal void InternalInit()
    {
        if (_inited)
        {
            return;
        }

        _inited = true;
        RefreshFieldBindings();
        BindActionsOnce();
        OnInit();
    }

    internal void InternalShow() => OnShow();
    internal void InternalHide() => OnHide();

    internal void InternalDestroy()
    {
        UIAutoBinder.ReleaseIndex(transform);
        OnDestroyUI();
    }

    protected virtual void OnInit()
    {
    }

    protected virtual void OnShow()
    {
    }

    protected virtual void OnHide()
    {
    }

    protected virtual void OnDestroyUI()
    {
    }

    protected virtual void OnBindingsRefreshed()
    {
    }

    protected T Get<T>(string name) where T : Component
    {
        return UIAutoBinder.ResolveComponent<T>(transform, name);
    }

    [ContextMenu("Rebind Now")]
    public void RebindNow()
    {
        RefreshFieldBindings();
        if (!_actionsBound)
        {
            BindActionsOnce();
        }

        OnBindingsRefreshed();
    }

    private void RefreshFieldBindings()
    {
        UIAutoBinder.RebuildIndex(transform);
        UIAutoBinder.BindFields(this, transform);
    }

    private void BindActionsOnce()
    {
        if (_actionsBound)
        {
            return;
        }

        _actionsBound = true;
        AutoBindActions();
    }

    private void AutoBindActions()
    {
        var methods = GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (var i = 0; i < methods.Length; i++)
        {
            var method = methods[i];
            var attrs = method.GetCustomAttributes<BindActionAttribute>(true);
            foreach (var attr in attrs)
            {
                BindOne(method, attr.BehaviourType, attr.BindField);
            }
        }
    }

    private void BindOne(MethodInfo method, EUIBehaviourType type, string bindName)
    {
        switch (type)
        {
            case EUIBehaviourType.Button:
                BindButton(method, bindName);
                break;
            case EUIBehaviourType.Toggle:
                BindToggle(method, bindName);
                break;
            case EUIBehaviourType.Slider:
                BindSlider(method, bindName);
                break;
            case EUIBehaviourType.Scrollbar:
                BindScrollbar(method, bindName);
                break;
            case EUIBehaviourType.Dropdown:
                BindDropdown(method, bindName);
                break;
            case EUIBehaviourType.InputField:
                BindInputField(method, bindName);
                break;
        }
    }

    private void BindButton(MethodInfo method, string name)
    {
        var ex = Get<UIButton>(name);
        if (ex != null)
        {
            var action = (UnityAction)method.CreateDelegate(typeof(UnityAction), this);
            ex.Clicked += () => action.Invoke();
            return;
        }

        var button = Get<Button>(name);
        if (button != null)
        {
            var action = (UnityAction)method.CreateDelegate(typeof(UnityAction), this);
            button.onClick.AddListener(action);
        }
    }

    private void BindToggle(MethodInfo method, string name)
    {
        var ex = Get<UIToggle>(name);
        if (ex != null)
        {
            var action = (UnityAction<bool>)method.CreateDelegate(typeof(UnityAction<bool>), this);
            ex.ValueChanged += value => action.Invoke(value);
            return;
        }

        var toggle = Get<Toggle>(name);
        if (toggle != null)
        {
            var action = (UnityAction<bool>)method.CreateDelegate(typeof(UnityAction<bool>), this);
            toggle.onValueChanged.AddListener(action);
        }
    }

    private void BindSlider(MethodInfo method, string name)
    {
        var ex = Get<UISlider>(name);
        if (ex != null)
        {
            var action = (UnityAction<float>)method.CreateDelegate(typeof(UnityAction<float>), this);
            ex.ValueChanged += value => action.Invoke(value);
            return;
        }

        var slider = Get<Slider>(name);
        if (slider != null)
        {
            var action = (UnityAction<float>)method.CreateDelegate(typeof(UnityAction<float>), this);
            slider.onValueChanged.AddListener(action);
        }
    }

    private void BindScrollbar(MethodInfo method, string name)
    {
        var ex = Get<UIScrollbar>(name);
        if (ex != null)
        {
            var action = (UnityAction<float>)method.CreateDelegate(typeof(UnityAction<float>), this);
            ex.ValueChanged += value => action.Invoke(value);
            return;
        }

        var scrollbar = Get<Scrollbar>(name);
        if (scrollbar != null)
        {
            var action = (UnityAction<float>)method.CreateDelegate(typeof(UnityAction<float>), this);
            scrollbar.onValueChanged.AddListener(action);
        }
    }

    private void BindDropdown(MethodInfo method, string name)
    {
        var ex = Get<UIDropdown>(name);
        if (ex != null)
        {
            var action = (UnityAction<int>)method.CreateDelegate(typeof(UnityAction<int>), this);
            ex.ValueChanged += value => action.Invoke(value);
            return;
        }

        var dropdown = Get<TMP_Dropdown>(name);
        if (dropdown != null)
        {
            var action = (UnityAction<int>)method.CreateDelegate(typeof(UnityAction<int>), this);
            dropdown.onValueChanged.AddListener(action);
        }
    }

    private void BindInputField(MethodInfo method, string name)
    {
        var ex = Get<UIInputField>(name);
        if (ex != null)
        {
            var action = (UnityAction<string>)method.CreateDelegate(typeof(UnityAction<string>), this);
            ex.ValueChanged += value => action.Invoke(value);
            return;
        }

        var input = Get<TMP_InputField>(name);
        if (input != null)
        {
            var action = (UnityAction<string>)method.CreateDelegate(typeof(UnityAction<string>), this);
            input.onValueChanged.AddListener(action);
        }
    }
}
