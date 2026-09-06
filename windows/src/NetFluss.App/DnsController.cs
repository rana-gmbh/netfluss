// Copyright (C) 2026 Rana GmbH — GPLv3. See LICENSE at the repository root.

using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using NetFluss.Core;

namespace NetFluss.App;

/// <summary>What an adapter's resolvers currently are.</summary>
internal sealed record DnsState(string AdapterId, string AdapterName, IReadOnlyList<string> Servers);

/// <summary>Outcome of an apply, in a form the UI can show verbatim.</summary>
internal sealed record DnsApplyResult(bool Succeeded, string Message)
{
    internal static DnsApplyResult Ok(string message) => new(true, message);

    internal static DnsApplyResult Fail(string message) => new(false, message);
}

/// <summary>
/// Applies DNS settings. Implemented today by elevating on demand; the Phase 2 service will
/// implement the same interface and the UI will not know the difference.
/// </summary>
internal interface IDnsApplier
{
    Task<DnsApplyResult> ApplyAsync(string adapterName, IReadOnlyList<string> servers);
}

/// <summary>
/// Reads and writes per-adapter DNS.
///
/// <para><b>Reading needs no privileges</b> — <see cref="NetworkInterface"/> reports the
/// active resolvers — so the whole UI, including the active-preset checkmark, works in a
/// perfectly ordinary unelevated session. Only applying is gated.</para>
///
/// <para><b>Writing needs administrator.</b> <c>SetInterfaceDnsSettings</c> requires it, and
/// the port plan puts DNS switching in the elevated service for exactly that reason. Until
/// that service exists, an apply elevates a single short-lived <c>netsh</c> run, so the user
/// sees one UAC prompt per change rather than the app demanding admin at launch. Running the
/// whole app elevated would be the wrong trade: a meter that sits in the tray all day should
/// not hold administrator rights so that a rarely-used setting can be changed.</para>
/// </summary>
internal sealed class DnsController : IDnsApplier
{
    /// <summary>Current resolvers for every adapter that has any, keyed by interface GUID.</summary>
    internal static IReadOnlyList<DnsState> Read()
    {
        var states = new List<DnsState>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                IReadOnlyList<string> servers;
                try
                {
                    servers = [.. nic.GetIPProperties().DnsAddresses.Select(address => address.ToString())];
                }
                catch (NetworkInformationException)
                {
                    // An adapter can disappear between enumeration and query.
                    continue;
                }

                states.Add(new DnsState(nic.Id, nic.Name, servers));
            }
        }
        catch (NetworkInformationException)
        {
            return [];
        }

        return states;
    }

    public async Task<DnsApplyResult> ApplyAsync(string adapterName, IReadOnlyList<string> servers)
    {
        // Validated again here, not only in the UI. This method builds a command line for a
        // process that will run as administrator, so it does not get to assume its caller
        // checked anything.
        var validation = DnsValidator.Validate(servers);
        if (!validation.IsValid)
        {
            return DnsApplyResult.Fail(validation.Error ?? "Invalid servers.");
        }

        // The adapter name reaches the command line too, so it must be one Windows itself
        // reported rather than anything typed. A name containing a quote could otherwise
        // close the argument and append commands to an elevated shell.
        if (!NetworkInterface.GetAllNetworkInterfaces().Any(nic => nic.Name == adapterName))
        {
            return DnsApplyResult.Fail($"No adapter named '{adapterName}'.");
        }

        if (adapterName.Contains('"') || adapterName.Contains('%'))
        {
            return DnsApplyResult.Fail("That adapter's name cannot be used from a script.");
        }

        var script = BuildScript(adapterName, servers);
        var path = Path.Combine(Path.GetTempPath(), $"netfluss-dns-{Guid.NewGuid():N}.cmd");

        try
        {
            await File.WriteAllTextAsync(path, script);

            // "runas" is what raises the UAC prompt. Without UseShellExecute it is ignored
            // and the process simply starts unelevated, where every netsh call fails.
            var info = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{path}\"\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(info);
            if (process is null)
            {
                return DnsApplyResult.Fail("Could not start the elevated helper.");
            }

            await process.WaitForExitAsync();

            return process.ExitCode == 0
                ? DnsApplyResult.Ok(servers.Count == 0
                    ? $"{adapterName} is back on automatic DNS."
                    : $"{adapterName} now uses {string.Join(", ", servers)}.")
                : DnsApplyResult.Fail($"netsh exited with code {process.ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — the user dismissed the UAC prompt. Not a failure worth
            // dressing up as one.
            return DnsApplyResult.Fail("Cancelled. DNS was not changed.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DnsApplyResult.Fail(e.Message);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A temp file left behind is not worth failing the operation over.
            }
        }
    }

    /// <summary>
    /// The netsh script. IPv4 and IPv6 are separate stores in Windows, so a preset carrying
    /// both has to write both — and "System Default" has to clear both, or an IPv6 resolver
    /// left behind would keep answering and the change would look like it did nothing.
    /// </summary>
    private static string BuildScript(string adapterName, IReadOnlyList<string> servers)
    {
        var lines = new List<string> { "@echo off" };

        var v4 = servers.Where(s => !s.Contains(':')).ToList();
        var v6 = servers.Where(s => s.Contains(':')).ToList();

        foreach (var (family, list) in new[] { ("ipv4", v4), ("ipv6", v6) })
        {
            if (list.Count == 0)
            {
                lines.Add($"netsh interface {family} set dnsservers name=\"{adapterName}\" source=dhcp");
                continue;
            }

            lines.Add($"netsh interface {family} set dnsservers name=\"{adapterName}\" static {list[0]} primary validate=no");

            for (var i = 1; i < list.Count; i++)
            {
                lines.Add($"netsh interface {family} add dnsservers name=\"{adapterName}\" address={list[i]} index={i + 1} validate=no");
            }
        }

        // Stale entries would otherwise keep resolving from cache after the switch.
        lines.Add("ipconfig /flushdns");
        lines.Add("exit /b 0");

        return string.Join("\r\n", lines) + "\r\n";
    }
}
