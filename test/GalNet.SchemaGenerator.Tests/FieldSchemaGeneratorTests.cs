using GalNet.Core.Parameters;
using GalNet.SchemaGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GeneralTest.Schema;

public sealed class FieldSchemaGeneratorTests
{
    [Test]
    public void InheritedFieldsKeepTheirDefaultCodecAndOrderWithoutRepeatingDeclarations()
    {
        var source = """
            using GalNet.Core.Parameters;
            public abstract class BaseOptions {
                [Field(0, DefaultJson = "{}")] public System.Text.Json.JsonElement Data {get;set;}
                [Field(1, AllowNull = true), System.Text.Json.Serialization.JsonConverter(typeof(StringBooleanValueConverter))]
                public bool? Enabled {get;set;} = true;
            }
            [GenerateFieldSchema] public partial class Options : BaseOptions {
                [Field(2)] public int Count {get;set;} = 3;
            }
            """;
        var driver = CSharpGeneratorDriver.Create(new FieldSchemaGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(Compile(source), out var output, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var code = driver.GetRunResult().GeneratedTrees.Single().ToString();
        Assert.That(code, Does.Contain("this.@Data").And.Contains("this.@Enabled").And.Contains("StringBooleanValueConverter").And.Contains("ValueCodec.ParseDefault"));
        Assert.That(code.IndexOf("new(\"enabled\"", StringComparison.Ordinal), Is.LessThan(code.IndexOf("new(\"count\"", StringComparison.Ordinal)));
    }

    [TestCase("public new int Value {get;set;}")]
    [TestCase("[Field(0)] public int Other {get;set;}")]
    public void HiddenInheritedFieldsAndCollidingOrdersHaveLocatedDiagnostics(string derived)
    {
        var source = "using GalNet.Core.Parameters; public class BaseOptions { [Field(0)] public int Value {get;set;} } " +
            "[GenerateFieldSchema] public partial class Options : BaseOptions { " + derived + " }";
        var driver = CSharpGeneratorDriver.Create(new FieldSchemaGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(Compile(source), out _, out var diagnostics);
        Assert.That(diagnostics.Single().Id, Is.EqualTo("GNF001"));
        Assert.That(diagnostics[0].Location.IsInSource, Is.True);
        Assert.That(driver.GetRunResult().GeneratedTrees, Is.Empty);
    }

    [Test]
    public void JsonDefaultAndChoicesGenerateCompilableSingleSourceMetadata()
    {
        var source = """
            using GalNet.Core.Parameters;
            [GenerateFieldSchema] public partial class Options {
                [Field(0, DefaultJson = "[1,2]")] public System.Collections.Generic.List<int> Numbers {get;set;}
                [Field(1, Choices = new[] {"Left", "Right"})] public string Side {get;set;} = "Left";
            }
            """;
        var driver = CSharpGeneratorDriver.Create(new FieldSchemaGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(Compile(source), out var output, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var code = driver.GetRunResult().GeneratedTrees.Single().ToString();
        Assert.That(code, Does.Contain("ValueCodec.ParseDefault").And.Contains("[1,2]").And.Contains("[\"options\"] = new string[]"));
    }

    [Test]
    public void JsonNamesAcronymsAndEscapedIdentifiersRemainCompilable()
    {
        var source = """
            using GalNet.Core.Parameters;
            [GenerateFieldSchema] public partial class @class {
                [Field(0)] public int URLValue {get;set;}
                [Field(1), System.Text.Json.Serialization.JsonPropertyName("asset-id")] public int ID {get;set;}
            }
            """;
        var driver = CSharpGeneratorDriver.Create(new FieldSchemaGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(Compile(source), out var output, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var code = driver.GetRunResult().GeneratedTrees.Single().ToString();
        Assert.That(code, Does.Contain("new(\"urlValue\"").And.Contains("new(\"asset-id\""));
    }
    [Test]
    public void ConstantDefaultsAndMemberChangesGenerateCompilableOrderedAccessors()
    {
        var source = """
            using GalNet.Core.Parameters;
            [GenerateFieldSchema] public partial class Options {
                [Field(1, AllowNull = false, MaxLength = 20)] public string Text {get;set;} = "hello";
                [Field(0, Minimum = 0, Maximum = 10, EditorId = "slider", Step = 0.5)] public double Number {get;set;} = 2.5;
            }
            """;
        var first = Compile(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new FieldSchemaGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(first, out var output, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var generated = driver.GetRunResult().GeneratedTrees.Single().ToString();
        Assert.That(generated.IndexOf("new(\"number\"", StringComparison.Ordinal), Is.LessThan(generated.IndexOf("new(\"text\"", StringComparison.Ordinal)));
        driver = driver.RunGeneratorsAndUpdateCompilation(Compile(source.Replace("Number", "Amount").Replace("2.5", "3.5")), out output, out diagnostics);
        Assert.That(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var changed = driver.GetRunResult().GeneratedTrees.Single().ToString();
        Assert.That(changed, Does.Contain("this.@Amount").And.Not.Contains("this.@Number"));
        Assert.That(changed, Does.Contain("3.5"));
    }

    [TestCase("[Field(0)] private int _number;")]
    [TestCase("[Field(0)] public int Number {get;init;}")]
    [TestCase("[Field(0)] public int Number {get;set;} = System.Environment.TickCount;")]
    [TestCase("[Field(0)] public int A {get;set;} [Field(0)] public int B {get;set;}")]
    [TestCase("[Field(0), System.Text.Json.Serialization.JsonIgnore] public int Number {get;set;}")]
    [TestCase("[Field(0, DefaultJson = \"1\")] public int Number {get;set;} = 1;")]
    [TestCase("[Field(0, DefaultJson = null)] public int Number {get;set;}")]
    [TestCase("[Field(0, Choices = null)] public string Side {get;set;}")]
    public void UnsupportedDeclarationsHaveLocatedDiagnostics(string members)
    {
        var input = Compile("using GalNet.Core.Parameters; [GenerateFieldSchema] public partial class Options {" + members + "}");
        var driver = CSharpGeneratorDriver.Create(new FieldSchemaGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(input, out _, out var diagnostics);
        Assert.That(diagnostics.Single().Id, Is.EqualTo("GNF001"));
        Assert.That(diagnostics[0].Location.IsInSource, Is.True);
        Assert.That(driver.GetRunResult().GeneratedTrees, Is.Empty);
    }

    private static CSharpCompilation Compile(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(FieldAttribute).Assembly.Location).Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path));
        return CSharpCompilation.Create("GeneratorConsumer", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
