using System.Text.Json;

// Protocol 1: one JSON request on stdin; one JSON response on stdout. Diagnostics belong on stderr.
var line = await Console.In.ReadLineAsync() ?? throw new InvalidDataException("Missing request.");
using var request = JsonDocument.Parse(line); var root = request.RootElement;
if (root.GetProperty("apiVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported protocol.");
var command = root.GetProperty("command").GetString();
if (command == "describe")
{
    Console.WriteLine(JsonSerializer.Serialize(new { apiVersion = 1, message = $"{root.GetProperty("width").GetInt32()} × {root.GetProperty("height").GetInt32()} · {root.GetProperty("bitDepth").GetInt32()} bit PNG" }));
}
else if (command == "duplicate")
{
    var output = Path.Combine(root.GetProperty("outputDirectory").GetString()!, "duplicate.png");
    File.Copy(root.GetProperty("input").GetString()!, output);
    Console.WriteLine(JsonSerializer.Serialize(new { apiVersion = 1, image = "duplicate.png", name = "Plugin copy" }));
}
else { Console.Error.WriteLine("Unknown command."); Environment.ExitCode = 2; }
