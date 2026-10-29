using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using GalNet.Core.Assets;
using GalNet.Editor.Models;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Controls;

internal static class AssetFieldEditor
{
    public static FieldEditorLease Create(FieldEditorContext context, IAssetManager? assets, AssetPickerFilter filter)
    {
        if (assets is null) return DefaultFieldEditors.CreateText(context);
        var picker = new AssetPicker { AssetManager = assets, Filter = filter };
        var lifetime = new CancellationTokenSource(); var refreshing = false; var disposed = false;
        void Refresh()
        {
            if (disposed) return;
            refreshing = true;
            try { picker.IsEnabled = context.IsEnabled; picker.SelectedAssetId = context.EffectiveValue is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : ""; }
            finally { refreshing = false; }
        }
        void Changed(object? sender, PropertyChangedEventArgs args) => Refresh();
        async void Selected(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property != AssetPicker.SelectedAssetIdProperty || refreshing || disposed) return;
            try { await context.CommitAsync(new(JsonSerializer.SerializeToElement(picker.SelectedAssetId)), lifetime.Token); }
            catch (OperationCanceledException) { }
            finally { Refresh(); }
        }
        Refresh(); context.PropertyChanged += Changed; picker.PropertyChanged += Selected;
        return new(picker, () => { disposed = true; lifetime.Cancel(); context.PropertyChanged -= Changed; picker.PropertyChanged -= Selected; picker.Dispose(); picker.AssetManager = null; lifetime.Dispose(); });
    }
}
