namespace GalNet.Core.View;

/// <summary>Input boundary through which the runtime waits for player decisions.</summary>
public interface IInteractionView
{
    Task WaitForClickAsync(CancellationToken ct);
    /// <summary>Shows options for one widget instance and returns the zero-based selected option index.</summary>
    /// <param name="widgetInstanceId">Stable presentation identifier used to update or dismiss the choice widget.</param>
    /// <param name="options">Already-resolved visible option text, in selection order.</param>
    /// <param name="ct">Cancels the pending player decision.</param>
    Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct);
}
