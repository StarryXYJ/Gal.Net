using System;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class GamePreviewPanelFactory : IGamePreviewPanelFactory
{
    public GamePreviewPanelViewModel Create(IServiceProvider services) =>
        services.GetRequiredService<GamePreviewPanelViewModel>();
}
