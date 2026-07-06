using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UIToggle : BaseElement
{
    [BindField] private Toggle _toggle;          // 自身
    [BindField("Label")] private TMP_Text _label; // 可选

    public event Action<bool> ValueChanged;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (_toggle != null) _toggle.onValueChanged.AddListener(OnValueChanged);
    }

    protected override void OnDisable()
    {
        if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnValueChanged);
        base.OnDisable();
    }

    private void OnValueChanged(bool v)
    {
        if (!canClick) return;
        ValueChanged?.Invoke(v);
    }

    public bool IsOn => _toggle != null && _toggle.isOn;

    public void SetIsOn(bool on, bool notify = false)
    {
        if (_toggle == null) return;
        if (notify) _toggle.isOn = on;
        else _toggle.SetIsOnWithoutNotify(on);
    }

    public void SetText(string t)
    {
        if (_label) _label.text = t;
    }

    public void SetInteractable(bool v)
    {
        if (_toggle) _toggle.interactable = v;
        canClick = v;
    }
}