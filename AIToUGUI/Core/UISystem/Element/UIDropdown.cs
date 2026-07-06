using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class UIDropdown : BaseElement
{
    [BindField] private TMP_Dropdown _dropdown; // 自身

    public event Action<int> ValueChanged;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (_dropdown != null) _dropdown.onValueChanged.AddListener(OnValueChanged);
    }

    protected override void OnDisable()
    {
        if (_dropdown != null) _dropdown.onValueChanged.RemoveListener(OnValueChanged);
        base.OnDisable();
    }

    private void OnValueChanged(int v)
    {
        if (!canClick) return;
        ValueChanged?.Invoke(v);
    }

    public int Value => _dropdown != null ? _dropdown.value : 0;

    public void SetValue(int v, bool notify = false)
    {
        if (_dropdown == null) return;
        if (notify) _dropdown.value = v;
        else _dropdown.SetValueWithoutNotify(v);
    }

    public void SetOptions(List<string> options, bool keepValue = false)
    {
        if (_dropdown == null) return;

        var old = _dropdown.value;
        _dropdown.ClearOptions();
        _dropdown.AddOptions(options);

        if (keepValue)
            _dropdown.SetValueWithoutNotify(Mathf.Clamp(old, 0, options.Count - 1));
        else
            _dropdown.SetValueWithoutNotify(0);
    }

    public void SetInteractable(bool v)
    {
        if (_dropdown) _dropdown.interactable = v;
        canClick = v;
    }
}