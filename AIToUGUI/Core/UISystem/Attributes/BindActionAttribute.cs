using System;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class BindActionAttribute : Attribute
{
    public EUIBehaviourType BehaviourType { get; }
    public string BindField { get; }

    public BindActionAttribute(EUIBehaviourType type, string bindField)
    {
        BehaviourType = type;
        BindField = bindField;
    }
}