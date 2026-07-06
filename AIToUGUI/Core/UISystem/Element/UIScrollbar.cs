using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UIScrollbar : BaseElement
{
    [BindField] private Scrollbar _scrollbar; // 自身

    public event Action<float> ValueChanged;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (_scrollbar != null) _scrollbar.onValueChanged.AddListener(OnValueChanged);
    }

    protected override void OnDisable()
    {
        if (_scrollbar != null) _scrollbar.onValueChanged.RemoveListener(OnValueChanged);
        base.OnDisable();
    }

    private void OnValueChanged(float v)
    {
        if (!canClick) return;
        ValueChanged?.Invoke(v);
    }

    public float Value => _scrollbar != null ? _scrollbar.value : 0f;

    public void SetValue(float v, bool notify = false)
    {
        if (_scrollbar == null) return;
        v = Mathf.Clamp01(v);
        if (notify) _scrollbar.value = v;
        else _scrollbar.SetValueWithoutNotify(v);
    }

    public void SetSize(float size01)
    {
        if (_scrollbar == null) return;
        _scrollbar.size = Mathf.Clamp01(size01);
    }

    public void SetInteractable(bool v)
    {
        if (_scrollbar) _scrollbar.interactable = v;
        canClick = v;
    }
}