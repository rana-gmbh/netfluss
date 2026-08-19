// Copyright (C) 2026 Rana GmbH
//
// This file is part of NetFluss.
//
// NetFluss is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// NetFluss is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with NetFluss. If not, see <https://www.gnu.org/licenses/>.

using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetFluss.Core;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> as JSON.
///
/// <para>Takes an explicit path rather than reaching for <c>%LOCALAPPDATA%</c> itself, so
/// Core stays platform-neutral and the tests can point it at a temp directory instead of
/// scribbling on the developer's real settings.</para>
///
/// <para><b>Corrupt files do not take the app down.</b> A settings file that fails to parse
/// — half-written during a power cut, hand-edited badly — falls back to defaults rather
/// than throwing. A menu-bar app with no window has nowhere to show a load error, and
/// starting with default preferences is always better than not starting.</para>
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private bool _suspended;

    public SettingsStore(string path)
    {
        _path = path;
        Settings = Load();

        // Persist on every change: Preferences has no OK button, matching how macOS
        // System Settings and the NetFluss preferences window both behave.
        Settings.PropertyChanged += OnSettingsChanged;
    }

    public AppSettings Settings { get; }

    /// <summary>Raised after a change has been written, so the app can re-apply live.</summary>
    public event EventHandler? Changed;

    /// <summary>The conventional per-user location on Windows.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetFluss",
        "settings.json");

    /// <summary>
    /// Applies several changes as one unit, writing and notifying once at the end. Without
    /// this, resetting a dozen properties would write the file a dozen times.
    /// </summary>
    public void Batch(Action<AppSettings> edit)
    {
        _suspended = true;
        try
        {
            edit(Settings);
        }
        finally
        {
            _suspended = false;
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write-then-replace: a crash midway through leaves the previous settings
            // intact instead of a truncated file that will not parse on next launch.
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Settings, SerializerOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked or read-only profile must not kill the meter. The user loses
            // persistence for this session and nothing else.
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), SerializerOptions)
                   ?? new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suspended)
        {
            return;
        }

        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
