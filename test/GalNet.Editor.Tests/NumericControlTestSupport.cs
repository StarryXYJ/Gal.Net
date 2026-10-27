using Avalonia.Controls;
using Avalonia.VisualTree;
using GalNet.Game.Controls.Forms;

namespace GeneralTest.Editor;

internal static class NumericControlTestSupport
{
    public static TextBox Input(Control editor) => editor as TextBox ?? editor.GetVisualDescendants().OfType<TextBox>().Single();
    public static IEnumerable<TextBox> NamedInputs(Control root, string name) => root.GetVisualDescendants().OfType<Control>()
        .Where(control => control.Name == name && control is TextBox or NumericUpDown).Select(Input);
    public static TextBox FieldInput(Control root, string field) => root.GetVisualDescendants().OfType<Control>()
        .Where(control => control is TextBox or NumericUpDown && control.DataContext is FieldEditorContext context && context.Field.Id == field).Select(Input).Distinct().Single();
}
