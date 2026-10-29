using GalNet.Core.Parameters;
using GalNet.SchemaGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GeneralTest.Schema;

public sealed class FieldSchemaGeneratorTests
{
    [Test]
    public void AConsumerCompiledAgainstTheOldDescriptorApiLoadsAgainstCurrentCore()
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) != "GalNet.Core.dll").Distinct(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        var version = typeof(FieldDescriptor).Assembly.GetName().Version;
        var oldSource = """
            using System;
            using System.Collections.Generic;
            using System.Text.Json;
            namespace GalNet.Core.Parameters {
                public enum FieldValueSemantic { None, LocalizedText, CharacterReference }
                public sealed class EditorHints {}
                public sealed class ValueDescriptor {
                    public ValueDescriptor(Type type, bool? allowNull = null, JsonElement? constraints = null,
                        JsonSerializerOptions jsonOptions = null, IReadOnlyList<string> choices = null) {}
                    public Type ValueType => null;
                }
                public sealed class FieldDescriptor {
                    public FieldDescriptor(string id, ValueDescriptor value, bool isRequired = false, JsonElement? defaultValue = null,
                        EditorHints editor = null, string labelKey = null, string descriptionKey = null, FieldValueSemantic semantic = FieldValueSemantic.None) {}
                    public ValueDescriptor Value => null;
                }
            }
            """;
        var oldCore = CSharpCompilation.Create("GalNet.Core",
            [CSharpSyntaxTree.ParseText("[assembly: System.Reflection.AssemblyVersion(\"" + version + "\")]"), CSharpSyntaxTree.ParseText(oldSource)], platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var oldBytes = new MemoryStream();
        var oldResult = oldCore.Emit(oldBytes);
        Assert.That(oldResult.Success, Is.True, string.Join("\n", oldResult.Diagnostics));
        var consumer = CSharpCompilation.Create("OldDescriptorConsumer",
            [CSharpSyntaxTree.ParseText("""
                public static class OldContract {
                    public static string Read() => new GalNet.Core.Parameters.FieldDescriptor("x",
                        new GalNet.Core.Parameters.ValueDescriptor(typeof(int))).Value.ValueType.FullName;
                }
                """)], platform.Append(MetadataReference.CreateFromImage(oldBytes.ToArray())),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var bytes = new MemoryStream();
        var result = consumer.Emit(bytes);
        Assert.That(result.Success, Is.True, string.Join("\n", result.Diagnostics));
        bytes.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("OldDescriptorApi", isCollectible: true);
        context.Resolving += (_, name) => name.Name == "GalNet.Core" ? typeof(FieldDescriptor).Assembly : null;
        try
        {
            var assembly = context.LoadFromStream(bytes);
            Assert.That(assembly.GetType("OldContract")!.GetMethod("Read")!.Invoke(null, null), Is.EqualTo("System.Int32"));
        }
        finally { context.Unload(); }
    }

    [Test]
    public void FixedJsonStructuresGenerateLazyTypeReferencesAndKeepTheirWireType()
    {
        const string source = """
            using GalNet.Core.Parameters;
            [GenerateFieldSchema] public partial class Child {
                [Field(0)] public Child Next {get;set;}
            }
            [GenerateFieldSchema] public partial class Options {
                [Field(0, StructureType = typeof(Child), StructureAllowsJsonString = true, StructureIgnoresCase = true)]
                public System.Text.Json.JsonElement Transform {get;set;}
            }
            """;
        var driver = CSharpGeneratorDriver.Create(new FieldSchemaGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(Compile(source), out var output, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var code = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.ToString()));
        Assert.That(code, Does.Contain("ValueStructure.ForType(typeof(global::Child)").And.Contain("allowsJsonString: true").And.Contain("ignoresCase: true"));
        Assert.That(code, Does.Contain("typeof(global::System.Text.Json.JsonElement)"));
        Assert.That(code, Does.Contain("WithSchemaProvider(static () => global::Child.Schema)"), "Recursive child schemas must be deferred through a static delegate.");
    }

    [Test]
    public void GameTextSemanticsAreInheritedAndIndependentOfEditorHints()
    {
        const string source = """
            using GalNet.Core.Parameters;
            public class BaseOptions { [Field(0), CharacterReference] public string? Speaker {get;set;} }
            [GenerateFieldSchema] public partial class Child {
                [Field(0), LocalizedText] public string? Caption {get;set;}
            }
            [GenerateFieldSchema] public partial class Options : BaseOptions {
                [Field(1)] public Child? Child {get;set;}
            }
            """;
        var driver = CSharpGeneratorDriver.Create(new FieldSchemaGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(Compile(source), out var output, out var diagnostics);
        Assert.That(diagnostics, Is.Empty);
        Assert.That(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var code = string.Join("\n", driver.GetRunResult().GeneratedTrees);
        Assert.That(code, Does.Contain("FieldValueSemantic.CharacterReference").And.Contains("FieldValueSemantic.LocalizedText"));
    }

    [TestCase("[Field(0), LocalizedText] public int Count {get;set;}")]
    [TestCase("[Field(0), CharacterReference, LocalizedText] public string Name {get;set;}")]
    [TestCase("[LocalizedText] public string Name {get;set;}")]
    public void InvalidGameTextSemanticsProduceLocatedDiagnostic(string member)
    {
        var driver = CSharpGeneratorDriver.Create(new FieldSchemaGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(Compile("using GalNet.Core.Parameters; [GenerateFieldSchema] public partial class Options {" + member + "}"), out _, out var diagnostics);
        Assert.That(diagnostics.Single().Id, Is.EqualTo("GNF001"));
        Assert.That(diagnostics[0].Location.IsInSource, Is.True);
        Assert.That(driver.GetRunResult().GeneratedTrees, Is.Empty);
    }

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
        Assert.That(code.IndexOf("(\"enabled\"", StringComparison.Ordinal), Is.LessThan(code.IndexOf("(\"count\"", StringComparison.Ordinal)));
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
        Assert.That(code, Does.Contain("ValueCodec.ParseDefault").And.Contains("[1,2]").And.Contains("choices: new string[]"));
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
        Assert.That(code, Does.Contain("(\"urlValue\"").And.Contains("(\"asset-id\""));
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
        Assert.That(generated.IndexOf("(\"number\"", StringComparison.Ordinal), Is.LessThan(generated.IndexOf("(\"text\"", StringComparison.Ordinal)));
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
