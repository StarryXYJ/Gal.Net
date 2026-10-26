using GalNet.Editor.Abstraction.Schemas;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GalNet.Core.Entry;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Variable;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;

namespace GalNet.Editor.Services;

public sealed record VariableReference(string Location, bool Certain, AuthorNavigationTarget? Target = null);

/// <summary>Edits declarations and their known references as one project history intent.</summary>
public sealed class VariableAuthoringService(IEditorDocumentService documents, IEntryCatalog catalog, IVariableDefinitionService definitions, EditorHistories histories)
{
    public IReadOnlyList<ProjectVariableDefinition> Get(VariableScope scope) => List(scope);
    private List<ProjectVariableDefinition> List(VariableScope scope) => scope switch
    {
        VariableScope.Player => documents.CurrentDocument.PlayerVariables,
        VariableScope.Save => documents.CurrentDocument.SaveVariables,
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };
    private IEnumerable<ProjectVariableDefinition> All => Get(VariableScope.Player).Concat(Get(VariableScope.Save));
    private ProjectVariableDefinition Find(VariableScope scope, string uid) => List(scope).SingleOrDefault(item => item.DefaultValue.Uid == uid)
        ?? throw new EditorOperationException("variable.missing", "The variable no longer exists.");
    public ProjectVariableDefinition Add(VariableScope scope)
    {
        var number = 1;
        string name;
        do { name = $"var_{scope.ToString().ToLowerInvariant()}_{number++}"; } while (All.Any(item => item.Name == name));
        var value = new ProjectVariableDefinition { Name = name, DefaultValue = new() { Name = name } };
        histories.Content.Execute(new VariableEdit(this, scope, List(scope).Count, null, value, [], new("History.Variable.Add", name)));
        return value;
    }
    public IReadOnlyList<VariableReference> Inspect(VariableScope scope, string uid) => References(Find(scope, uid).Name).Select(item => item.Reference).ToArray();

    public IReadOnlyList<VariableReference> Update(VariableScope scope, string uid, string name, VariableType type, string defaultText, string description, string? expectedSnapshot = null)
    {
        var before = Find(scope, uid);
        if (expectedSnapshot is not null && JsonSerializer.Serialize(before) != expectedSnapshot) throw new EditorOperationException("variable.changed", "The variable or its references changed outside this edit.");
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) || GalleryUnlockVariable.IsReservedName(name)
            || new[] { "true", "false", "and", "or", "not", "in", "mod", "xor" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new EditorOperationException("variable.name", "Use an ASCII letter or underscore followed by letters, digits or underscores; Gallery names are reserved.");
        if (All.Any(item => item.DefaultValue.Uid != uid && item.Name == name)) throw new EditorOperationException("variable.duplicate", "The variable name is already used in this project.");
        var after = before.Clone(); after.Name = name; after.DefaultValue.Name = name; after.Description = description;
        try
        {
            after.DefaultValue.Value = type switch
            {
                VariableType.Bool => VariableValue.From(bool.Parse(defaultText)),
                VariableType.Int => VariableValue.From(int.Parse(defaultText, NumberStyles.Integer, CultureInfo.InvariantCulture)),
                VariableType.Float => ParseFloat(defaultText),
                VariableType.String => VariableValue.From(defaultText),
                _ => throw new FormatException()
            };
        }
        catch (Exception failure) when (failure is FormatException or OverflowException) { throw new EditorOperationException("variable.default", "The default value is not valid for the selected type."); }
        var references = before.Name == name ? [] : References(before.Name);
        var patches = references.Where(item => item.Reference.Certain).Select(item => new Patch(item.Slot, item.Value, item.Direct ? RenameSymbol(item.Value, before.Name, name) : Rewrite(item.Value, before.Name, name).Text)).ToArray();
        if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
            histories.Content.Execute(new VariableEdit(this, scope, List(scope).IndexOf(before), before.Clone(), after, patches, new("History.Variable.Update", name)));
        return references.Where(item => !item.Reference.Certain).Select(item => item.Reference).ToArray();
    }
    public void Delete(VariableScope scope, string uid)
    {
        var before = Find(scope, uid);
        if (References(before.Name).Any(item => item.Reference.Certain)) throw new EditorOperationException("variable.inUse", "The variable has references. Remove them before deleting it.");
        histories.Content.Execute(new VariableEdit(this, scope, List(scope).IndexOf(before), before.Clone(), null, [], new("History.Variable.Delete", before.Name)));
    }
    public static string DefaultText(ProjectVariableDefinition value) => value.Type switch
    {
        VariableType.Bool => value.DefaultValue.AsBool() ? "true" : "false",
        VariableType.Int => value.DefaultValue.AsInt().ToString(CultureInfo.InvariantCulture),
        VariableType.Float => value.DefaultValue.AsFloat().ToString("R", CultureInfo.InvariantCulture),
        _ => value.DefaultValue.AsString()
    };
    private static VariableValue ParseFloat(string text)
    {
        var value = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return float.IsFinite(value) ? VariableValue.From(value) : throw new FormatException();
    }

    private sealed record Slot(string Node, string? Entry, string? Item, string Field);
    private sealed record Found(Slot Slot, string Value, bool Direct, VariableReference Reference);
    private sealed record Patch(Slot Slot, string Before, string After);
    private List<Found> References(string name)
    {
        var found = new List<Found>();
        void Add(Slot slot, string text, bool expression, bool direct, string location)
        {
            var known = direct ? MatchesSymbol(text, name) : expression && text.Contains(name, StringComparison.Ordinal) && ValidExpression(text) && Rewrite(text, name, name).Found;
            if (known || text.Contains(name, StringComparison.Ordinal)) found.Add(new(slot, text, direct, new(location, known, new(slot.Node, slot.Entry,
                slot.Field.StartsWith("parameter:", StringComparison.Ordinal) ? slot.Field[10..] : slot.Field))));
        }
        foreach (var node in documents.CurrentDocument.Nodes)
        {
            foreach (var option in node.Options ?? []) Add(new(node.Id, null, option.Id, "option"), option.Condition, true, false, $"Graph/{node.Id}/options/{option.Id}/condition");
            foreach (var option in node.Options ?? []) Add(new(node.Id, null, option.Id, "optionText"), option.Text, false, false, $"Graph/{node.Id}/options/{option.Id}/text");
            foreach (var condition in node.Conditions ?? []) Add(new(node.Id, null, condition.Id, "branch"), condition.Expression, true, false, $"Graph/{node.Id}/conditions/{condition.Id}");
            if (!documents.LoadedProject.GroupEntries.TryGetValue(node.Id, out var entries)) continue;
            foreach (var entry in entries)
            {
                var path = $"Graph/{node.File}/entries/{entry.StableId}";
                Add(new(node.Id, entry.StableId, null, "condition"), entry.Condition, true, false, path + "/condition");
                catalog.TryGet(entry.Type, out var schema);
                foreach (var pair in entry.Parameters)
                {
                    var kind = schema?.DynamicParameters.TryGetValue(pair.Key, out var descriptor) == true
                        ? EntrySchema.GetEditorType(descriptor) : (EntryParameterType?)null;
                    var text = pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString()! : pair.Value.GetRawText();
                    Add(new(node.Id, entry.StableId, null, "parameter:" + pair.Key), text,
                        pair.Value.ValueKind == JsonValueKind.String && kind == EntryParameterType.Expression,
                        pair.Value.ValueKind == JsonValueKind.String && kind == EntryParameterType.VariableName, path + "/parameters/" + pair.Key);
                }
            }
        }
        return found;
    }
    // Rewrite symbols only outside quoted strings. Bracket parameters and bare
    // identifiers are supported; function names and opaque parameter text are not rewritten.
    private static (string Text, bool Found) Rewrite(string text, string name, string replacement)
    {
        var result = new StringBuilder(); var found = false;
        for (var index = 0; index < text.Length;)
        {
            var start = index; var ch = text[index++];
            if (ch is '\'' or '"' or '#')
            {
                var closed = false;
                while (index < text.Length)
                {
                    if (text[index] == '\\') { index += Math.Min(2, text.Length - index); continue; }
                    if (text[index++] == ch) { closed = true; break; }
                }
                if (!closed) return (text, false);
                result.Append(text.AsSpan(start, index - start)); continue;
            }
            if (ch == '[')
            {
                var end = text.IndexOf(']', index); if (end < 0) return (text, false);
                var symbol = text[index..end]; var match = MatchesSymbol(symbol, name);
                result.Append('[').Append(match ? RenameSymbol(symbol, name, replacement) : symbol).Append(']'); found |= match; index = end + 1; continue;
            }
            if (char.IsAsciiLetter(ch) || ch == '_')
            {
                while (index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] == '_')) index++;
                var next = index; while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                var match = text[start..index] == name && (start == 0 || !(char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '.' or '_'))
                    && (next == text.Length || text[next] is not ('(' or '.'));
                result.Append(match ? replacement : text[start..index]); found |= match; continue;
            }
            result.Append(ch);
        }
        return (result.ToString(), found);
    }
    private static bool MatchesSymbol(string symbol, string name) => symbol == name || symbol == "player." + name || symbol == "save." + name;
    private static string RenameSymbol(string symbol, string name, string replacement) => symbol[..^name.Length] + replacement;
    private static bool ValidExpression(string text)
    {
        try { return !new NCalc.Expression(text.Replace('"', '\'')).HasErrors(); }
        catch (Exception) { return false; }
    }
    private string Read(Slot slot)
    {
        var node = documents.CurrentDocument.Nodes.Single(item => item.Id == slot.Node);
        if (slot.Field == "option") return node.Options!.Single(item => item.Id == slot.Item).Condition;
        if (slot.Field == "branch") return node.Conditions!.Single(item => item.Id == slot.Item).Expression;
        var entry = documents.LoadedProject.GroupEntries[slot.Node].Single(item => item.StableId == slot.Entry);
        return slot.Field == "condition" ? entry.Condition : entry.Parameters[slot.Field[10..]].GetString()!;
    }
    private void Write(Slot slot, string value)
    {
        var node = documents.CurrentDocument.Nodes.Single(item => item.Id == slot.Node);
        if (slot.Field == "option") { node.Options!.Single(item => item.Id == slot.Item).Condition = value; return; }
        if (slot.Field == "branch") { node.Conditions!.Single(item => item.Id == slot.Item).Expression = value; return; }
        var entry = documents.LoadedProject.GroupEntries[slot.Node].Single(item => item.StableId == slot.Entry);
        if (slot.Field == "condition") entry.Condition = value;
        else entry.Parameters[slot.Field[10..]] = JsonSerializer.SerializeToElement(value);
    }
    private sealed record VariableEdit(VariableAuthoringService Owner, VariableScope Scope, int Index, ProjectVariableDefinition? Before, ProjectVariableDefinition? After, Patch[] Patches, EditorText DisplayText) : IUndoableEdit
    {
        public string Description => "Edit variable";
        public long RetainedBytes => (JsonSerializer.Serialize(Before).Length + JsonSerializer.Serialize(After).Length) * 2L + Patches.Sum(patch => (patch.Before.Length + patch.After.Length) * 2L);
        public void Redo() => Apply(Before, After, true);
        public void Undo() => Apply(After, Before, false);
        private void Apply(ProjectVariableDefinition? expected, ProjectVariableDefinition? value, bool forward)
        {
            var list = Owner.List(Scope); var uid = (Before ?? After)!.DefaultValue.Uid;
            var current = list.SingleOrDefault(item => item.DefaultValue.Uid == uid);
            if (JsonSerializer.Serialize(current) != JsonSerializer.Serialize(expected) || Patches.Any(patch => Owner.Read(patch.Slot) != (forward ? patch.Before : patch.After)))
                throw new EditorOperationException("variable.changed", "The variable or its references changed outside this edit.");
            var oldIndex = current is null ? Index : list.IndexOf(current);
            try
            {
                if (current is not null) list.Remove(current);
                if (value is not null) list.Insert(Math.Min(oldIndex, list.Count), value.Clone());
                foreach (var patch in Patches) Owner.Write(patch.Slot, forward ? patch.After : patch.Before);
                Owner.Changed();
            }
            catch (Exception failure)
            {
                try
                {
                    list.RemoveAll(item => item.DefaultValue.Uid == uid);
                    if (current is not null) list.Insert(Math.Min(oldIndex, list.Count), current);
                    foreach (var patch in Patches) Owner.Write(patch.Slot, forward ? patch.Before : patch.After);
                    Owner.Changed();
                }
                catch (Exception compensation) { throw new HistoryRepairRequiredException("Variable edit compensation failed.", new AggregateException(failure, compensation)); }
                throw;
            }
        }
    }
    private void Changed() { documents.SynchronizeGroups(); definitions.ResetFromDocument(); }
}
