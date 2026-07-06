using System;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class UIInputField : BaseElement
{
    [BindField] private TMP_InputField _input; // 自身

    public event Action<string> ValueChanged;
    public event Action<string> EndEdit;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (_input != null)
        {
            _input.onValueChanged.AddListener(OnValueChanged);
            _input.onEndEdit.AddListener(OnEndEdit);
        }
    }

    protected override void OnDisable()
    {
        if (_input != null)
        {
            _input.onValueChanged.RemoveListener(OnValueChanged);
            _input.onEndEdit.RemoveListener(OnEndEdit);
        }
        base.OnDisable();
    }

    private void OnValueChanged(string s)
    {
        if (!canClick) return;
        ValueChanged?.Invoke(s);
    }

    private void OnEndEdit(string s)
    {
        if (!canClick) return;
        EndEdit?.Invoke(s);
    }

    public string Text => _input != null ? _input.text : string.Empty;

    public void SetText(string t, bool notify = false)
    {
        if (_input == null) return;
        if (notify) _input.text = t;
        else _input.SetTextWithoutNotify(t);
    }

    public void SetInteractable(bool v)
    {
        if (_input) _input.interactable = v;
        canClick = v;
    }
}