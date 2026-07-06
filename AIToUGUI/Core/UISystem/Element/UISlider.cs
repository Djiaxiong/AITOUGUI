using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UISlider : BaseElement
{
    [BindField] private Slider _slider;
    private TMP_Text _valueTxt;

    public event Action<float> ValueChanged;

    protected override void OnAfterBind()
    {
        base.OnAfterBind();

        _valueTxt = GetChildComponent<TMP_Text>("Value");
        if (_valueTxt == null)
        {
            var valueRoot = FindChild("Value");
            _valueTxt = valueRoot != null ? valueRoot.GetComponentInChildren<TMP_Text>(true) : null;
        }

        UpdateValueText(_slider != null ? _slider.value : 0f);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (_slider != null)
        {
            _slider.onValueChanged.AddListener(OnValueChanged);
        }
    }

    protected override void OnDisable()
    {
        if (_slider != null)
        {
            _slider.onValueChanged.RemoveListener(OnValueChanged);
        }

        base.OnDisable();
    }

    private void OnValueChanged(float value)
    {
        UpdateValueText(value);
        if (!canClick)
        {
            return;
        }

        ValueChanged?.Invoke(value);
    }

    public float Value => _slider != null ? _slider.value : 0f;

    public void SetValue(float value, bool notify = false)
    {
        if (_slider == null)
        {
            return;
        }

        if (notify)
        {
            _slider.value = value;
        }
        else
        {
            _slider.SetValueWithoutNotify(value);
        }

        UpdateValueText(value);
    }

    public void SetRange(float min, float max)
    {
        if (_slider == null)
        {
            return;
        }

        _slider.minValue = min;
        _slider.maxValue = max;
    }

    public void SetInteractable(bool value)
    {
        if (_slider != null)
        {
            _slider.interactable = value;
        }

        canClick = value;
    }

    private void UpdateValueText(float value)
    {
        if (_valueTxt != null)
        {
            _valueTxt.text = value.ToString("0.##");
        }
    }
}
