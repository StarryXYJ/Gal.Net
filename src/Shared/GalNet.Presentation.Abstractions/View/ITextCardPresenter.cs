using GalNet.Contracts.Builtins.Scene;

namespace GalNet.Presentation.Abstractions.View;

public interface ITextCardPresenter
{
    Task ShowTextCardAsync(TextCardRequest request, CancellationToken cancellationToken);
    void ClearTextCards();
}
