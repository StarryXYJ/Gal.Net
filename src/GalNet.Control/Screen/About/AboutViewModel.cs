using System.Text;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Assets;
using GalNet.Core.UI;

namespace GalNet.Control.Screen.About;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IGameScreenNavigator _navigator;
    public AboutUiConfiguration Configuration { get; }
    public Thickness ContentMargin => new(Configuration.ContentPadding);

    [ObservableProperty]
    private string _markdown = "No about document is configured.";

    public AboutViewModel(IGameScreenNavigator navigator, AboutUiConfiguration configuration, IAssetManager? assets)
    {
        _navigator = navigator;
        Configuration = configuration;
        if (!string.IsNullOrWhiteSpace(configuration.ContentAsset) && assets is not null)
            _ = LoadAsync(assets, configuration.ContentAsset);
    }

    [RelayCommand]
    private Task BackAsync() => _navigator.GoBackAsync();

    private async Task LoadAsync(IAssetManager assets, string assetId)
    {
        try
        {
            var file = await assets.GetFileAsync(assetId);
            if (file is null)
            {
                SetMarkdown("The configured about document could not be found.");
                return;
            }
            if (!AssetPickerTextFileExtensions.IsSupported(file.Path))
            {
                SetMarkdown("The configured asset is not a supported text document.");
                return;
            }

            SetMarkdown(DecodeText(await file.ReadAllBytesAsync()));
        }
        catch (Exception)
        {
            SetMarkdown("The configured about document could not be read.");
        }
    }

    private void SetMarkdown(string value)
    {
        if (Dispatcher.UIThread.CheckAccess()) Markdown = value;
        else Dispatcher.UIThread.Post(() => Markdown = value);
    }

    internal static string DecodeText(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true, throwOnInvalidCharacters: true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true, throwOnInvalidCharacters: true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: true, throwOnInvalidBytes: true).GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true).GetString(bytes, 2, bytes.Length - 2);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
    }
}
