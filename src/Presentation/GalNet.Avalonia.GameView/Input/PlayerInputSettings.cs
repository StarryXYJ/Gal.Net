using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Avalonia.GameView.Input;

public enum PlayerInputDevice { KeyboardMouse, Gamepad, Touch }
public sealed record GameInputAction(string Id, string NameKey, bool IsHold = false);
public sealed record InputBinding(PlayerInputDevice Device, string Gesture);
public sealed record PlayerInputConfiguration(int Version, bool EastConfirms, Dictionary<string, InputBinding[]> Bindings);

/// <summary>Scope-owned player preferences. Files and execution are owned by the host.</summary>
public sealed class PlayerInputSettings : ObservableObject, IDisposable
{
    public static IReadOnlyList<GameInputAction> Actions { get; } = Array.AsReadOnly(new GameInputAction[]
    {
        new("advance", "Input.Advance"), new("hold.quick", "Input.HoldQuick", true),
        new("toggle.quick", "Input.Quick"), new("toggle.auto", "Input.Auto"),
        new("skip.unread", "Input.Skip"), new("toggle.ui", "Input.HideUi"),
        new("save.first-empty", "Input.Save"), new("open.save", "Input.OpenSave"),
        new("open.load", "Input.Load"), new("open.settings", "Input.Settings"),
        new("screenshot", "Input.Screenshot"), new("toggle.fullscreen", "Input.Fullscreen"),
        new("open.history", "Input.History")
    });
    public static IReadOnlyList<string> TouchGestures { get; } = Array.AsReadOnly(new[]
    { "Tap", "DoubleTap", "LongPress", "TwoFingerTap", "TwoFingerLongPress", "SwipeLeft", "SwipeRight", "SwipeUp", "SwipeDown" });
    private PlayerInputConfiguration _configuration = Defaults();
    private readonly SemaphoreSlim _edits = new(1, 1);
    public PlayerInputConfiguration Configuration => Copy(_configuration);
    public PlayerInputDevice LastDevice { get; set; }
    public PlayerInputDevice? SelectedDevice { get; set; }
    public bool EastConfirms => _configuration.EastConfirms;
    public bool GamepadSupported { get; set; }
    public bool TouchSupported { get; set; } = true;
    public bool IsCapturing { get; set; }
    public bool IsPreviewingChoice { get; set; }
    public event Action<IReadOnlySet<string>>? GamepadChanged;
    public event Action? GamepadCancelled;
    public void CancelGamepad()
    {
        GamepadButtons = new HashSet<string>();
        GamepadCancelled?.Invoke();
    }
    public IReadOnlySet<string> GamepadButtons { get; private set; } = new HashSet<string>();
    public void ObserveGamepad(IReadOnlySet<string> buttons)
    {
        GamepadButtons = buttons;
        if (buttons.Count > 0) LastDevice = PlayerInputDevice.Gamepad;
        GamepadChanged?.Invoke(buttons);
    }
    public Func<PlayerInputConfiguration, Task<bool>> CommitAsync { get; set; } = _ => Task.FromResult(false);
    public string? ErrorKey { get; private set; }
    public string? ConflictActionKey { get; private set; }
    public void ReportError(string? key) { ErrorKey = key; Changed?.Invoke(); }
    public event Action? Changed;
    /// <summary>Optional future page extension; no handler means the history action is unavailable.</summary>
    public Func<Task>? OpenHistoryAsync { get; set; }
    public Func<Task>? ToggleFullscreenAsync { get; set; }
    public bool IsActionAvailable(string id) => id != "open.history" || OpenHistoryAsync is not null;

    public InputBinding[] GetBindings(string action, PlayerInputDevice device) =>
        _configuration.Bindings.GetValueOrDefault(action, []).Where(binding => binding.Device == device).ToArray();
    public string? Resolve(PlayerInputDevice device, string gesture) => _configuration.Bindings
        .FirstOrDefault(pair => pair.Value.Any(binding => binding.Device == device && binding.Gesture == gesture)).Key;

    public async Task<bool> SetBindingAsync(string action, PlayerInputDevice device, int index, string? gesture)
    {
        await _edits.WaitAsync();
        try
        {
            if (!Actions.Any(item => item.Id == action) || index is < 0 or > 1) return false;
            var next = Configuration;
            var bindings = next.Bindings.GetValueOrDefault(action, []);
            var own = bindings.Where(binding => binding.Device == device).ToList();
            while (own.Count <= index) own.Add(new(device, ""));
            own[index] = new(device, gesture ?? "");
            next.Bindings[action] = bindings.Where(binding => binding.Device != device).Concat(own).ToArray();
            if (Validate(next) is { } error)
            {
                ErrorKey = error;
                var conflict = Resolve(device, gesture ?? "");
                ConflictActionKey = error == "Input.Conflict" ? Actions.FirstOrDefault(item => item.Id == conflict)?.NameKey : null;
                Changed?.Invoke();
                return false;
            }
            ErrorKey = null;
            ConflictActionKey = null;
            return await CommitAsync(next);
        }
        finally { _edits.Release(); }
    }

    public async Task<bool> SetLayoutAsync(bool east)
    {
        await _edits.WaitAsync();
        try
        {
            var next = Configuration;
            if (next.EastConfirms == east) return true;
            foreach (var id in next.Bindings.Keys.ToArray())
                next.Bindings[id] = next.Bindings[id].Select(binding => binding.Device != PlayerInputDevice.Gamepad ? binding :
                    binding with { Gesture = string.Join('+', binding.Gesture.Split('+').Select(code => code == "South" ? "East" : code == "East" ? "South" : code).Order(StringComparer.Ordinal)) }).ToArray();
            return await CommitAsync(next with { EastConfirms = east });
        }
        finally { _edits.Release(); }
    }
    public async Task<bool> ResetAsync(PlayerInputDevice device)
    {
        await _edits.WaitAsync();
        try
        {
            var next = Configuration;
            var defaults = Defaults();
            foreach (var action in Actions)
                next.Bindings[action.Id] = next.Bindings.GetValueOrDefault(action.Id, []).Where(binding => binding.Device != device)
                    .Concat(defaults.Bindings.GetValueOrDefault(action.Id, []).Where(binding => binding.Device == device)).ToArray();
            return await CommitAsync(device == PlayerInputDevice.Gamepad ? next with { EastConfirms = false } : next);
        }
        finally { _edits.Release(); }
    }

    public void Apply(PlayerInputConfiguration configuration)
    {
        if (Validate(configuration) is { } error) throw new ArgumentException(error, nameof(configuration));
        _configuration = Copy(configuration);
        ErrorKey = null;
        ConflictActionKey = null;
        Changed?.Invoke();
        OnPropertyChanged(nameof(EastConfirms));
    }
    public string Serialize() => JsonSerializer.Serialize(_configuration);
    public void Restore(string? json)
    {
        try
        {
            var parsed = json is null ? null : JsonSerializer.Deserialize<PlayerInputConfiguration>(json);
            Apply(parsed is not null && Validate(parsed) is null ? parsed : Defaults());
        }
        catch (JsonException) { Apply(Defaults()); }
    }
    public static string? Validate(PlayerInputConfiguration configuration)
    {
        if (configuration.Version != 1 || configuration.Bindings is null) return "Input.InvalidConfiguration";
        var seen = new HashSet<InputBinding>();
        foreach (var pair in configuration.Bindings)
        {
            if (!Actions.Any(action => action.Id == pair.Key) || pair.Value is null) return "Input.InvalidConfiguration";
            foreach (var group in pair.Value.GroupBy(binding => binding?.Device))
                if (group.Count() > 2) return "Input.InvalidConfiguration";
            foreach (var binding in pair.Value)
            {
                if (binding is null || binding.Gesture is null || !Enum.IsDefined(binding.Device)) return "Input.InvalidConfiguration";
                if (binding.Gesture.Length == 0) continue;
                if (binding.Gesture.Length > 160) return "Input.InvalidConfiguration";
                if (!InputGestureCodec.IsValid(binding)) return "Input.InvalidConfiguration";
                if (binding.Device == PlayerInputDevice.Touch && !TouchGestures.Contains(binding.Gesture)) return "Input.InvalidConfiguration";
                if (!seen.Add(binding)) return "Input.Conflict";
                if (binding.Device == PlayerInputDevice.KeyboardMouse && binding.Gesture.Split('+').Contains("Escape")) return "Input.Reserved";
                if (binding.Device == PlayerInputDevice.Gamepad && binding.Gesture.Split('+').Contains(configuration.EastConfirms ? "South" : "East")) return "Input.Reserved";
                if (Actions.First(action => action.Id == pair.Key).IsHold && binding.Device == PlayerInputDevice.Touch) return "Input.HoldUnavailable";
                if (Actions.First(action => action.Id == pair.Key).IsHold && binding.Gesture.Split('+').Any(code => code is "WheelUp" or "WheelDown")) return "Input.HoldUnavailable";
            }
        }
        return null;
    }
    private static PlayerInputConfiguration Copy(PlayerInputConfiguration value) => value with
    { Bindings = value.Bindings.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal) };

    public static PlayerInputConfiguration Defaults() => new(1, false, new(StringComparer.Ordinal)
    {
        ["advance"] = [new(PlayerInputDevice.KeyboardMouse, "Space"), new(PlayerInputDevice.KeyboardMouse, "Enter"), new(PlayerInputDevice.Gamepad, "South"), new(PlayerInputDevice.Touch, "Tap")],
        ["hold.quick"] = [new(PlayerInputDevice.KeyboardMouse, "Control"), new(PlayerInputDevice.Gamepad, "West")],
        ["toggle.quick"] = [new(PlayerInputDevice.KeyboardMouse, "Tab")],
        ["toggle.auto"] = [new(PlayerInputDevice.KeyboardMouse, "A")],
        ["toggle.ui"] = [new(PlayerInputDevice.KeyboardMouse, "H"), new(PlayerInputDevice.KeyboardMouse, "MouseMiddle"), new(PlayerInputDevice.Gamepad, "North"), new(PlayerInputDevice.Touch, "TwoFingerTap")],
        ["save.first-empty"] = [new(PlayerInputDevice.KeyboardMouse, "Control+S")],
        ["open.save"] = [new(PlayerInputDevice.KeyboardMouse, "F5")],
        ["open.load"] = [new(PlayerInputDevice.KeyboardMouse, "Control+L")],
        ["open.settings"] = [new(PlayerInputDevice.KeyboardMouse, "F1"), new(PlayerInputDevice.Gamepad, "Start")],
        ["screenshot"] = [new(PlayerInputDevice.KeyboardMouse, "F12")],
        ["toggle.fullscreen"] = [new(PlayerInputDevice.KeyboardMouse, "Alt+Enter")]
    });
    public void Dispose() { CancelGamepad(); _edits.Dispose(); }
}
