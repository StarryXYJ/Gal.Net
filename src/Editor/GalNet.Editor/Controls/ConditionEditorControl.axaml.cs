using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Core.Variable;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Controls;

public partial class ConditionEditorControl : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<ConditionEditorControl, string>(
            nameof(Text),
            defaultValue: string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable<ConditionVariableSuggestion>> SuggestionsProperty =
        AvaloniaProperty.Register<ConditionEditorControl, IEnumerable<ConditionVariableSuggestion>>(
            nameof(Suggestions),
            []);

    public static readonly StyledProperty<IEnumerable<ProjectVariableDefinition>> ValidationVariablesProperty =
        AvaloniaProperty.Register<ConditionEditorControl, IEnumerable<ProjectVariableDefinition>>(
            nameof(ValidationVariables),
            []);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IEnumerable<ConditionVariableSuggestion> Suggestions
    {
        get => GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public IEnumerable<ProjectVariableDefinition> ValidationVariables
    {
        get => GetValue(ValidationVariablesProperty);
        set => SetValue(ValidationVariablesProperty, value);
    }

    public ObservableCollection<ConditionVariableSuggestion> FilteredSuggestions { get; } = [];

    public ConditionEditorControl()
    {
        InitializeComponent();
        EditorBox.AddHandler(InputElement.KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        EditorBox.LostFocus += (_, _) => Validate();
        SuggestionsList.SelectionChanged += OnSuggestionSelected;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty || change.Property == SuggestionsProperty)
            UpdateSuggestions();
    }

    private void UpdateSuggestions()
    {
        FilteredSuggestions.Clear();
        var filter = ExtractVariableFilter(Text);
        if (filter is null)
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }

        foreach (var suggestion in Suggestions
                     .Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .Take(8))
        {
            FilteredSuggestions.Add(suggestion);
        }

        SuggestionsPopup.IsOpen = FilteredSuggestions.Count > 0;
    }

    private void OnSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (SuggestionsList.SelectedItem is not ConditionVariableSuggestion suggestion)
            return;

        Text = ReplaceCurrentVariableToken(Text, suggestion.Name);
        SuggestionsPopup.IsOpen = false;
        SuggestionsList.SelectedItem = null;
        EditorBox.CaretIndex = Text.Length;
        Validate();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            SuggestionsPopup.IsOpen = false;
    }

    private void Validate()
    {
        if (ConditionExpressionValidator.TryValidate(Text, ValidationVariables, out var error))
            SetValidationMessage(string.Empty);
        else
            SetValidationMessage(error ?? "Invalid condition");
    }

    private static string? ExtractVariableFilter(string text)
    {
        var lastOpen = text.LastIndexOf('[');
        if (lastOpen < 0)
            return null;

        var lastClose = text.LastIndexOf(']');
        if (lastClose > lastOpen)
            return null;

        return text[(lastOpen + 1)..];
    }

    private static string ReplaceCurrentVariableToken(string text, string variableName)
    {
        var lastOpen = text.LastIndexOf('[');
        if (lastOpen < 0)
            return $"{text}[{variableName}]";

        var lastClose = text.LastIndexOf(']');
        if (lastClose > lastOpen)
            return $"{text}[{variableName}]";

        return $"{text[..lastOpen]}[{variableName}]";
    }

    private void SetValidationMessage(string message)
    {
        ValidationText.Text = message;
        ValidationBorder.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
}
