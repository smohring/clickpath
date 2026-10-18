using Clickpath.Localization;

namespace Clickpath.Models;

public sealed record Option<T>(T Value, string Label)
{
    /// <summary>The automation name of a list entry is its ToString; screen readers should hear the label.</summary>
    public override string ToString() => Label;
}

/// <summary>Localised lists for the combo boxes in the step editor.</summary>
public static class Options
{
    public static IReadOnlyList<Option<StepTarget>> Targets { get; } =
        Enum.GetValues<StepTarget>().Select(t => new Option<StepTarget>(t, Loc.T("Target." + t))).ToList();

    public static IReadOnlyList<Option<NewWindowMode>> NewWindowModes { get; } =
        Enum.GetValues<NewWindowMode>().Select(m => new Option<NewWindowMode>(m, Loc.T("NewWindow." + m))).ToList();

    /// <summary>The kinds a group step can be changed between (they all hold steps).</summary>
    public static IReadOnlyList<Option<StepType>> GroupTypes { get; } =
        Enum.GetValues<StepType>().Where(Step.AllowsChildren).Select(t => new Option<StepType>(t, Loc.T("Step." + t))).ToList();

    public static IReadOnlyList<Option<TextMatch>> Matches { get; } =
        Enum.GetValues<TextMatch>().Select(m => new Option<TextMatch>(m, Loc.T("Match." + m))).ToList();
}
