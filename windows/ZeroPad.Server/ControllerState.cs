using System.Text.Json;

namespace ZeroPad.Server;

internal sealed record ControllerState(
    long Sequence,
    IReadOnlySet<string> Buttons,
    float LeftX,
    float LeftY,
    float RightX,
    float RightY,
    float LeftTrigger,
    float RightTrigger)
{
    private static readonly HashSet<string> AllowedButtons = new(StringComparer.Ordinal)
    {
        "A", "B", "X", "Y", "LB", "RB", "LT", "RT",
        "START", "SELECT", "UP", "DOWN", "LEFT", "RIGHT",
        "L3", "R3"
    };

    public static ControllerState Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        Require(root.ValueKind == JsonValueKind.Object, "Root must be an object.");
        Require(Number(root, "version") == 1, "Unsupported protocol version.");
        Require(String(root, "type") == "state", "Unsupported message type.");

        var sequence = Integer(root, "sequence");
        Require(sequence >= 0, "Sequence must be non-negative.");

        var buttonsElement = Property(root, "buttons");
        Require(buttonsElement.ValueKind == JsonValueKind.Array, "buttons must be an array.");
        var buttons = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in buttonsElement.EnumerateArray())
        {
            Require(item.ValueKind == JsonValueKind.String, "Button names must be strings.");
            var button = item.GetString()!;
            Require(AllowedButtons.Contains(button), $"Unknown button: {button}");
            Require(buttons.Add(button), $"Duplicate button: {button}");
        }

        var axes = Property(root, "axes");
        Require(axes.ValueKind == JsonValueKind.Object, "axes must be an object.");
        var leftX = Range(Number(axes, "leftX"), -1f, 1f, "leftX");
        var leftY = Range(Number(axes, "leftY"), -1f, 1f, "leftY");
        var rightX = Range(Number(axes, "rightX"), -1f, 1f, "rightX");
        var rightY = Range(Number(axes, "rightY"), -1f, 1f, "rightY");

        var triggers = Property(root, "triggers");
        Require(triggers.ValueKind == JsonValueKind.Object, "triggers must be an object.");
        var leftTrigger = Range(Number(triggers, "left"), 0f, 1f, "left trigger");
        var rightTrigger = Range(Number(triggers, "right"), 0f, 1f, "right trigger");

        return new ControllerState(sequence, buttons, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger);
    }

    private static JsonElement Property(JsonElement parent, string name)
    {
        Require(parent.TryGetProperty(name, out var value), $"Missing field: {name}");
        return value;
    }

    private static string String(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        Require(value.ValueKind == JsonValueKind.String, $"{name} must be a string.");
        return value.GetString()!;
    }

    private static long Integer(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number), $"{name} must be an integer.");
        return value.GetInt64();
    }

    private static float Number(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number) && float.IsFinite(number), $"{name} must be a finite number.");
        return value.GetSingle();
    }

    private static float Range(float value, float minimum, float maximum, string name)
    {
        Require(value >= minimum && value <= maximum, $"{name} is outside its allowed range.");
        return value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new FormatException(message);
    }
}
