using System;

namespace GalNet.Editor.Services;

public interface IEditorViewFactory
{
    Avalonia.Controls.Control CreateView(Type viewType, object dataContext);
}
