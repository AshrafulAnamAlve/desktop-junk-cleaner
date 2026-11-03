namespace SafeClean.App.ViewModels;

/// <summary>One option in a row of choice buttons (for example "30 days").</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}
