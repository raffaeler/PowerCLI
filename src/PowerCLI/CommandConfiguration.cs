using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerCLI;

public sealed record CommandConfiguration
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<CommandDefinition> Commands { get; init; } = [];

    public static CommandConfiguration FromJson(string json)
    {
        try
        {
            var configuration = JsonSerializer.Deserialize<CommandConfiguration>(json, JsonOptions)
                ?? throw new CommandConfigurationException("Configuration must be an object.");
            using var document = JsonDocument.Parse(json);
            CheckProperties(document.RootElement, "$");
            if (!document.RootElement.TryGetProperty("schemaVersion", out _) || configuration.SchemaVersion != 1)
                throw new CommandConfigurationException("$.schemaVersion: only version 1 is supported.");
            if (!document.RootElement.TryGetProperty("commands", out _))
                throw new CommandConfigurationException("$.commands: required property is missing.");
            return configuration;
        }
        catch (JsonException exception)
        {
            throw new CommandConfigurationException($"{exception.Path}: {exception.Message}");
        }
    }

    public static async ValueTask<CommandConfiguration> FromJsonAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        return FromJson(await reader.ReadToEndAsync(cancellationToken));
    }

    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static void CheckProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
            throw new CommandConfigurationException($"{path}: null is not a configuration value; omit optional properties instead.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new CommandConfigurationException($"{path}.{property.Name}: duplicate property.");
                CheckProperties(property.Value, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in element.EnumerateArray()) CheckProperties(child, $"{path}[{index++}]");
        }
    }
}
