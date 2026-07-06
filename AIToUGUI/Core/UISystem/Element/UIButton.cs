using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UIButton : BaseElement
{
    [BindField] private Button _button; // "." 代表自身
    [BindField("Label")] private TMP_Text _label; // 子节点 Label
    [BindField("Icon")] private Image _icon; // 子节点 Icon

    public event Action Clicked;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (_button != null) _button.onClick.AddListener(OnUnityClick);
    }

    protected override void OnDisable()
    {
        if (_button != null) _button.onClick.RemoveListener(OnUnityClick);
        base.OnDisable();
    }

    private void OnUnityClick()
    {
        if (!canClick) return;
        Clicked?.Invoke();
    }

    public void SetText(string text)
    {
        if (_label) _label.text = text;
    }

    public void SetIcon(Sprite sp)
    {
        if (_icon) _icon.sprite = sp;
    }

    public void SetInteractable(bool v)
    {
        if (_button) _button.interactable = v;
        canClick = v;
    }
}