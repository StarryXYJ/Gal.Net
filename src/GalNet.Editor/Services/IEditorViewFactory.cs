using System;

namespace GalNet.Editor.Services;

public interface IEditorViewFactory
{
    Avalonia.Controls.Control CreateView(Type viewType, object dataContext);
    Avalonia.Controls.Control? CreateViewForViewModel(object viewModel);
    bool CanCreateViewForViewModel(object viewModel);
}
