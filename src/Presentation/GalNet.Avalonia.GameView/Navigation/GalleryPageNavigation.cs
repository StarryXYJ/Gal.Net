using Avalonia.Controls;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Gallery;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Avalonia.GameView.Navigation;

/// <summary>Activation contract for a page that renders one exact Gallery type.</summary>
public interface IGalleryPageViewModel : IActivatablePageViewModel<GalleryTypeData>;

/// <summary>Read-only mapping from Gallery type IDs to their explicitly registered pages.</summary>
public interface IGalleryPageRegistry
{
    Type GetViewModelType(string galleryTypeId);
    bool TryGetViewModelType(string galleryTypeId, out Type viewModelType);
}

/// <summary>Composition-time builder for exact Gallery type page mappings.</summary>
public interface IGalleryPageRegistryBuilder
{
    void Add<TViewModel, TView>(string galleryTypeId)
        where TViewModel : PageViewModelBase, IGalleryPageViewModel
        where TView : Control;

    void Replace<TViewModel, TView>(string galleryTypeId)
        where TViewModel : PageViewModelBase, IGalleryPageViewModel
        where TView : Control;

    IGalleryPageRegistry Build();
}

public sealed class GalleryPageRegistryBuilder(IPageViewRegistryBuilder pageViews, IServiceCollection services) : IGalleryPageRegistryBuilder
{
    private readonly Dictionary<string, Type> _pages = new(StringComparer.Ordinal);
    private bool _built;

    public void Add<TViewModel, TView>(string galleryTypeId)
        where TViewModel : PageViewModelBase, IGalleryPageViewModel
        where TView : Control => Register<TViewModel, TView>(galleryTypeId, replace: false);

    public void Replace<TViewModel, TView>(string galleryTypeId)
        where TViewModel : PageViewModelBase, IGalleryPageViewModel
        where TView : Control => Register<TViewModel, TView>(galleryTypeId, replace: true);

    public IGalleryPageRegistry Build()
    {
        _built = true;
        return new GalleryPageRegistry(_pages);
    }

    private void Register<TViewModel, TView>(string galleryTypeId, bool replace)
        where TViewModel : PageViewModelBase, IGalleryPageViewModel
        where TView : Control
    {
        if (_built) throw new InvalidOperationException("Gallery page mappings are immutable after composition.");
        var typeId = NormalizeTypeId(galleryTypeId);
        if (_pages.ContainsKey(typeId) && !replace)
            throw new InvalidOperationException($"Gallery page '{typeId}' is already registered. Use Replace explicitly.");
        pageViews.Register<TViewModel, TView>();
        services.AddScoped<TViewModel>();
        services.AddScoped<TView>();
        _pages[typeId] = typeof(TViewModel);
    }

    private static string NormalizeTypeId(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_' and not '-' and not '.'))
            throw new InvalidDataException("Gallery type IDs must contain only letters, digits, '_', '-' or '.'.");
        return normalized;
    }
}

public sealed class GalleryPageRegistry(IReadOnlyDictionary<string, Type> pages) : IGalleryPageRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _pages = new Dictionary<string, Type>(pages, StringComparer.Ordinal);

    public Type GetViewModelType(string galleryTypeId) =>
        TryGetViewModelType(galleryTypeId, out var viewModelType)
            ? viewModelType
            : throw new KeyNotFoundException($"No Gallery page is registered for '{galleryTypeId}'.");

    public bool TryGetViewModelType(string galleryTypeId, out Type viewModelType) =>
        _pages.TryGetValue(NormalizeTypeId(galleryTypeId), out viewModelType!);

    private static string NormalizeTypeId(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0 || normalized.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_' and not '-' and not '.'))
            throw new InvalidDataException("Gallery type IDs must contain only letters, digits, '_', '-' or '.'.");
        return normalized;
    }
}

/// <summary>Routes Gallery content by exact Gallery type ID without resource-type fallbacks.</summary>
public sealed class GalleryNavigationService(
    IServiceProvider services,
    IGameNavigationService navigation,
    IGalleryPageRegistry pages)
{
    public async Task OpenTypeAsync(GalleryTypeData gallery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gallery);
        if (!pages.TryGetViewModelType(gallery.Type.TypeId, out var viewModelType))
        {
            var diagnostic = services.GetRequiredService<MissingGalleryPageViewModel>();
            await diagnostic.ActivateAsync(gallery, cancellationToken);
            await navigation.NavigateAsync(diagnostic, cancellationToken: cancellationToken);
            return;
        }

        var viewModel = services.GetRequiredService(viewModelType);
        if (viewModel is not PageViewModelBase page || viewModel is not IGalleryPageViewModel galleryPage)
            throw new InvalidOperationException($"Gallery page '{viewModelType.FullName}' has an invalid registration.");
        await galleryPage.ActivateAsync(gallery, cancellationToken);
        await navigation.NavigateAsync(page, cancellationToken: cancellationToken);
    }
}
