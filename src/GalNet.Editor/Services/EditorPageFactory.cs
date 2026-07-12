using System;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class EditorPageFactory : IEditorPageFactory
{
    private readonly IServiceProvider _serviceProvider;

    public EditorPageFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public EditorPageViewModel CreateEditorPage() =>
        _serviceProvider.GetRequiredService<EditorPageViewModel>();
}
