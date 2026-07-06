using UnityEngine;

public class BasePanel : BaseUIController
{
    private CanvasGroup _cg;

    protected CanvasGroup CG
    {
        get
        {
            if (_cg == null)
            {
                if (!TryGetComponent(out _cg))
                    _cg = gameObject.AddComponent<CanvasGroup>();
            }
            return _cg;
        }
    }

    public virtual void ShowPanel()
    {
        CG.alpha = 1;
        CG.interactable = true;
        CG.blocksRaycasts = true;
    }

    public virtual void HidePanel()
    {
        CG.alpha = 0;
        CG.interactable = false;
        CG.blocksRaycasts = false;
    }

    protected override void OnShow() => ShowPanel();
    protected override void OnHide() => HidePanel();
}