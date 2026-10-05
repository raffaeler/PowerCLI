using System.Text.Json;
using PowerCLI;
using Xunit;

namespace PowerCLI.Tests;

public sealed class CommandSchemaTests
{
    [Fact]
    public void ShippedSchemaDefinesVersionAndRejectsUnknownPropertiesAtEveryLevel()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "commands.schema.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["schemaVersion", "commands"], root.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString() ?? throw new InvalidDataException("Schema entry must be a string.")).ToArray());
        var definitions = root.GetProperty("$defs");
        foreach (var name in new[] { "command", "form", "choices" })
            Assert.False(definitions.GetProperty(name).GetProperty("additionalProperties").GetBoolean());
        foreach (var name in new[] { "value", "option" })
            Assert.False(definitions.GetProperty(name).GetProperty("unevaluatedProperties").GetBoolean());
        Assert.Equal(["string", "integer", "number", "boolean", "relativePath"],
            definitions.GetProperty("metadata").GetProperty("properties").GetProperty("type").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString() ?? throw new InvalidDataException("Schema entry must be a string.")).ToArray());
    }

    [Fact]
    public void JsonConfigurationStillCompilesWithoutDemoDeploymentFiles()
    {
        const string json = """
            {"schemaVersion":1,"commands":[{"name":"/echo","forms":[
                {"id":"echo","syntax":"<text>","handler":"echo","arguments":{"text":{}}}
            ]}]}
            """;
        var configuration = CommandConfiguration.FromJson(json);
        Assert.Equal(1, configuration.SchemaVersion);
        var registry = new CommandRegistry(configuration,
            new Dictionary<string, ICommandHandler> { ["echo"] = new RecordingCommandHandler() });
        Assert.Contains("/echo", registry.CommandNames);
    }
}
