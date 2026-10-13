using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Shared.Commands;

namespace GeneralTest.Editor.Shared;

public sealed class CliValueContractTests
{
    [Test]
    public void PaginationDefaultsAndHelpRulesMatchTheValueUsedByExecution()
    {
        var catalog = new ToolOperationCatalog();
        var invocation = CliInvocation.Parse(catalog, ["files", "project"]);
        Assert.That(invocation.GetValue("--offset")!.Value.GetInt32(), Is.Zero);
        Assert.That(invocation.GetValue("--limit"), Is.Null);
        Assert.That(invocation.Has("--offset"), Is.False);
        var field = invocation.Definition.Parameters.Single(parameter => parameter.Name == "--offset");
        Assert.That(field.ValueContract, Is.TypeOf<ValueDescriptor>());
        var schema = invocation.Definition.InputSchema.GetProperty("properties").GetProperty("--offset");
        Assert.That(schema.GetProperty("minimum").GetInt32(), Is.Zero);
        Assert.That(schema.GetProperty("default").GetInt32(), Is.Zero);
        var explicitZero = CliInvocation.Parse(catalog, ["files", "project", "--limit", "0"]);
        Assert.That(explicitZero.GetValue("--limit")!.Value.GetInt32(), Is.Zero);
    }

    [TestCase("-1")]
    [TestCase("1.5")]
    [TestCase("null")]
    [TestCase("2147483648")]
    public void InvalidPaginationFailsBeforeAnyProjectAccess(string value) =>
        Assert.That(() => CliInvocation.Parse(new ToolOperationCatalog(), ["files", "missing-project", "--limit", value]), Throws.ArgumentException);

    [Test]
    public void HelpDefaultUsesTheSameCustomCodecAsExecution()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter<DayOfWeek>());
        var value = new ValueDescriptor(typeof(DayOfWeek), jsonOptions: options);
        var parameter = CliParameter.FromValue("--day", value, "Day", defaultValue: DayOfWeek.Monday);
        var operation = ToolOperationCatalog.Route("day", "day", "Test", [parameter]);
        Assert.That(parameter.Type, Is.EqualTo("string"));
        Assert.That(operation.InputSchema.GetProperty("properties").GetProperty("--day").GetProperty("default").GetString(), Is.EqualTo("Monday"));
    }

    [Test]
    public async Task RealHelpAndFilesUseTheSharedContract()
    {
        var (helpExit, help, _) = await CliHelpTests.Invoke(["help", "files", "--json"]);
        using var parsed = JsonDocument.Parse(help);
        Assert.That(helpExit, Is.Zero);
        Assert.That(parsed.RootElement.GetProperty("data").GetProperty("operations")[0].GetProperty("inputSchema")
            .GetProperty("properties").GetProperty("--limit").GetProperty("minimum").GetInt32(), Is.Zero);
        var (invalidExit, invalid, _) = await CliHelpTests.Invoke(["files", "missing-project", "--limit", "-1"]);
        Assert.That(invalidExit, Is.EqualTo(2));
        Assert.That(invalid, Does.Contain("cli.arguments"));
        var root = Path.Combine(Path.GetTempPath(), "GalNet-CliValues-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "file.txt"), "content");
            var (exit, output, _) = await CliHelpTests.Invoke(["files", root, "--limit", "0"]);
            using var result = JsonDocument.Parse(output);
            Assert.That(exit, Is.Zero, output);
            Assert.That(result.RootElement.GetProperty("success").GetBoolean(), Is.True);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
