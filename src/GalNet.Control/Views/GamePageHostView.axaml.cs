using System;
using Avalonia;
using Avalonia.Controls;
using GalNet.Control.ViewModels;
using Serilog;

namespace GalNet.Control.Views;

public partial class GamePageHostView : UserControl
{
    private GamePageHostViewModel? _viewModel;
    private object? _currentPage;

    public GamePageHostView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.InternalNav.CurrentPageChanged -= OnCurrentPageChanged;

        _viewModel = DataContext as GamePageHostViewModel;
        _currentPage = null;
        InternalContent.Content = null;

        if (_viewModel is not null)
        {
            _viewModel.InternalNav.CurrentPageChanged += OnCurrentPageChanged;

            if (_viewModel.InternalNav.CurrentPage is { } currentPage)
                OnCurrentPageChanged(currentPage);
        }

        base.OnDataContextChanged(e);
    }

    private void OnCurrentPageChanged(object? page)
    {
        if (ReferenceEquals(page, _currentPage) && InternalContent.Content is not null)
            return;

        if (page == null)
        {
            _currentPage = null;
            InternalContent.Content = null;
            return;
        }

        if (_viewModel is null)
        {
            Log.Warning("GamePageHostView: DataContext is not GamePageHostViewModel");
            return;
        }

        var viewType = _viewModel.InternalNav.GetRegisteredViewType(page.GetType());
        if (viewType == null)
        {
            Log.Warning("No view registered for {Type}", page.GetType().Name);
            _currentPage = page;
            InternalContent.Content = new TextBlock
            {
                Text = $"No view for {page.GetType().Name}",
                Foreground = Avalonia.Media.Brushes.Red,
                FontSize = 18,
            };
            return;
        }

        try
        {
            var view = (Avalonia.Controls.Control)Activator.CreateInstance(viewType)!;
            view.DataContext = page;
            _currentPage = page;
            InternalContent.Content = view;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create view for {Type}", page.GetType().Name);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.InternalNav.CurrentPageChanged -= OnCurrentPageChanged;

        _viewModel = null;
        _currentPage = null;
        InternalContent.Content = null;

        base.OnDetachedFromVisualTree(e);
    }
}
