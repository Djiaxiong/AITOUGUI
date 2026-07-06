using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class AIToUGUIBaseElementMotionBridge : MonoBehaviour
{
    private BaseElement _element;
    private AIToUGUI.AIToUGUIAnimationBinder _binder;

    private void Awake()
    {
        _element = GetComponent<BaseElement>();
        _binder = GetComponent<AIToUGUI.AIToUGUIAnimationBinder>();

        if (_element == null || _binder == null)
        {
            Debug.LogWarning("[AIToUGUIBaseElementMotionBridge] BaseElement or AIToUGUIAnimationBinder is missing.", this);
            enabled = false;
            return;
        }

        _binder.SetListenToPointerEvents(false);
        _element.PointEnter += OnPointerEnter;
        _element.PointerExit += OnPointerExit;
        _element.PointerDown += OnPointerDown;
        _element.PointerUp += OnPointerUp;
    }

    private void OnDestroy()
    {
        if (_element == null)
        {
            return;
        }

        _element.PointEnter -= OnPointerEnter;
        _element.PointerExit -= OnPointerExit;
        _element.PointerDown -= OnPointerDown;
        _element.PointerUp -= OnPointerUp;
    }

    private void OnPointerEnter(PointerEventData _)
    {
        _binder?.HandlePointerEnter();
    }

    private void OnPointerExit(PointerEventData _)
    {
        _binder?.HandlePointerExit();
    }

    private void OnPointerDown(PointerEventData _)
    {
        _binder?.HandlePointerDown();
    }

    private void OnPointerUp(PointerEventData _)
    {
        _binder?.HandlePointerUp();
    }
}
