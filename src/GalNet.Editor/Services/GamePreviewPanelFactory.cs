using System;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class GamePreviewPanelFactory : IGamePreviewPanelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public GamePreviewPanelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public GamePreviewPanelViewModel Create() =>
        _serviceProvider.GetRequiredService<GamePreviewPanelViewModel>();
}
