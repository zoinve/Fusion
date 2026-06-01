namespace YPM.Core.Models;

public sealed class NameValuePair
{
    public string Name { get; }
    public string Value { get; }

    public NameValuePair(string name, string value)
    {
        Name = name;
        Value = value;
    }
}
