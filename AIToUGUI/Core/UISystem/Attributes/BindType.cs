using System;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class BindFieldAttribute : Attribute
{
    public string BindName { get; private set; }

    public BindFieldAttribute(string evt = "")
    {
        BindName = evt;
    }
}