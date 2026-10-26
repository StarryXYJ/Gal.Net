namespace GalNet.Core.Execution;

/// <summary>Presents one already-filtered choice and returns its visible index.</summary>
public interface IChoicePresenter
{
    Task<int> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellationToken);
}
