using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Editor.Services;

namespace GalNet.Editor.ViewModels;

public sealed class FileDocumentPanelViewModel : ObservableObject, IDisposable
{
    public ProjectTextBuffer Buffer { get; }
    public string Title => Buffer.Title;
    public FileDocumentPanelViewModel(ProjectTextBuffer buffer) { Buffer = buffer; buffer.PropertyChanged += OnChanged; }
    private void OnChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(ProjectTextBuffer.Title)) OnPropertyChanged(nameof(Title)); }
    public void Dispose() => Buffer.PropertyChanged -= OnChanged;
}
