using System.ComponentModel;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Services;

namespace GalNet.Editor.Tests;

public sealed class TypedEditorContributionTests
{
    private static readonly IServiceProvider Services = new EmptyServiceProvider();

    [Test]
    public void Registry_rejects_duplicate_panel_ids()
    {
        var registry = new EditorExtensionRegistry();
        registry.RegisterDockPanel(new UnparameterizedContribution());

        Assert.That(
            () => registry.RegisterDockPanel(new UnparameterizedContribution()),
            Throws.InvalidOperationException.With.Message.Contains("typed-panel"));
        Assert.That(registry.FindDockPanel("typed-panel"), Is.Not.Null);
    }

    [Test]
    public void Unparameterized_bridge_forwards_typed_values_and_rejects_parameters()
    {
        IDockPanelContribution contribution = new UnparameterizedContribution();

        var viewModel = contribution.CreateViewModel(Services);
        var view = contribution.CreateView(Services, viewModel);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel, Is.TypeOf<PanelViewModel>());
            Assert.That(view, Is.SameAs(viewModel));
            Assert.That(
                () => contribution.CreateViewModel(Services, new PanelParameter("unexpected")),
                Throws.ArgumentException.With.Message.Contains("does not accept a parameter"));
            Assert.That(
                () => contribution.CreateView(Services, new object()),
                Throws.ArgumentException.With.Message.Contains(typeof(PanelViewModel).FullName));
        });
    }

    [Test]
    public void Parameterized_bridge_requires_the_declared_parameter_type()
    {
        IDockPanelContribution contribution = new ParameterizedContribution();
        var parameter = new PanelParameter("chapter-1");

        var viewModel = (PanelViewModel)contribution.CreateViewModel(Services, parameter);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Value, Is.EqualTo("chapter-1"));
            Assert.That(
                () => contribution.CreateViewModel(Services),
                Throws.ArgumentException.With.Message.Contains("null"));
            Assert.That(
                () => contribution.CreateViewModel(Services, new object()),
                Throws.ArgumentException.With.Message.Contains(typeof(PanelParameter).FullName));
        });
    }

    [Test]
    public void Inspector_bridge_checks_dock_and_inspector_view_model_types()
    {
        IInspectorControlContribution contribution = new InspectorContribution();
        var dock = new PanelViewModel("dock");

        var inspector = contribution.CreateViewModel(Services, dock);

        Assert.Multiple(() =>
        {
            Assert.That(inspector, Is.TypeOf<InspectorViewModel>());
            Assert.That(contribution.CreateView(Services, inspector), Is.SameAs(inspector));
            Assert.That(
                () => contribution.CreateViewModel(Services, new object()),
                Throws.ArgumentException.With.Message.Contains(typeof(PanelViewModel).FullName));
            Assert.That(
                () => contribution.CreateView(Services, new OtherInspectorViewModel()),
                Throws.ArgumentException.With.Message.Contains(typeof(InspectorViewModel).FullName));
        });
    }

    private sealed record PanelParameter(string Value);
    private sealed record PanelViewModel(string Value = "panel");

    private sealed class UnparameterizedContribution : DockPanelContributionBase<PanelViewModel>
    {
        public override string PanelId => "typed-panel";
        public override string TitleKey => "Typed";
        public override DockPanelPlacement Placement => DockPanelPlacement.MainDocument;
        public override bool IsGlobal => true;
        protected override PanelViewModel CreateViewModel(IServiceProvider services) => new();
        protected override object CreateView(IServiceProvider services, PanelViewModel viewModel) => viewModel;
    }

    private sealed class ParameterizedContribution : DockPanelContributionBase<PanelViewModel, PanelParameter>
    {
        public override string PanelId => "parameterized-panel";
        public override string TitleKey => "Parameterized";
        public override DockPanelPlacement Placement => DockPanelPlacement.MainDocument;
        public override bool IsGlobal => false;
        protected override PanelViewModel CreateViewModel(IServiceProvider services, PanelParameter parameter) =>
            new(parameter.Value);
        protected override object CreateView(IServiceProvider services, PanelViewModel viewModel) => viewModel;
    }

    private sealed class InspectorContribution :
        InspectorControlContributionBase<PanelViewModel, InspectorViewModel>
    {
        protected override InspectorViewModel CreateViewModel(IServiceProvider services, PanelViewModel dockViewModel) =>
            new(dockViewModel.Value);
        protected override object CreateView(IServiceProvider services, InspectorViewModel viewModel) => viewModel;
    }

    private sealed class InspectorViewModel(string value) : InspectorViewModelBase(value);
    private sealed class OtherInspectorViewModel() : InspectorViewModelBase("other");

    private abstract class InspectorViewModelBase(string value) : IInspectorControlViewModel
    {
        public string Value { get; } = value;
        public bool IsAvailable => true;
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public void Dispose() { }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
