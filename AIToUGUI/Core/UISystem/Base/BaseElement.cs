using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public abstract class BaseElement :
    UIBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    public Action<PointerEventData> PointEnter;
    public Action<PointerEventData> PointerExit;
    public Action<PointerEventData> PointerClick;
    public Action<PointerEventData> PointerDown;
    public Action<PointerEventData> PointerUp;

    public Action<PointerEventData> BeginDrag;
    public Action<PointerEventData> Drag;
    public Action<PointerEventData> EndDrag;

    [Tooltip("为 false 时仍会接收 Enter/Exit，但 Click/Down/Up 会被忽略（用于统一交互开关）")]
    public bool canClick = true;

    // 缓存：childName -> Transform
    private Dictionary<string, Transform> _childCache;

    protected override void Awake()
    {
        base.Awake();
        UIAutoBinder.BindFields(this, transform);
        OnAfterBind(); // 给子类一个“绑定完成后”的钩子（可选）
    }
 
    protected virtual void OnAfterBind() { }

    protected Transform FindChild(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        _childCache ??= new Dictionary<string, Transform>(16);

        if (_childCache.TryGetValue(name, out var t) && t != null) return t;

        // 注意：Transform.Find 支持路径 "A/B/C"
        t = transform.Find(name);
        _childCache[name] = t;
        return t;
    }

    protected T GetChildComponent<T>(string childName) where T : Component
    {
        var t = FindChild(childName);
        return t ? t.GetComponent<T>() : null;
    }

    // ===== pointer events =====

    public virtual void OnPointerEnter(PointerEventData eventData) => PointEnter?.Invoke(eventData);
    public virtual void OnPointerExit(PointerEventData eventData) => PointerExit?.Invoke(eventData);

    public virtual void OnPointerClick(PointerEventData eventData)
    {
        if (!canClick) return;
        PointerClick?.Invoke(eventData);
    }

    public virtual void OnPointerDown(PointerEventData eventData)
    {
        if (!canClick) return;
        PointerDown?.Invoke(eventData);
    }

    public virtual void OnPointerUp(PointerEventData eventData)
    {
        if (!canClick) return;
        PointerUp?.Invoke(eventData);
    }

    public virtual void OnBeginDrag(PointerEventData eventData) => BeginDrag?.Invoke(eventData);
    public virtual void OnDrag(PointerEventData eventData) => Drag?.Invoke(eventData);
    public virtual void OnEndDrag(PointerEventData eventData) => EndDrag?.Invoke(eventData);
}
