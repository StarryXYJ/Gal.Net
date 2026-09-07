namespace GalNet.Core.View;

public interface IPageView
{
    Task<string> ShowPageAsync(string screenInstanceId, CancellationToken ct);
}
