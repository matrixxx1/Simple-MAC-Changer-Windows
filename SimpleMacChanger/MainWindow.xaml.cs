using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SimpleMacChanger;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, string> _originalMacByAdapter = new();
    private readonly string[] _features =
    {
        "Adapter review surface",
        "MAC generation and validation",
        "Reversible change planning",
        "Actionable admin handoff script output"
    };

    private AdapterInfo? _selectedAdapter;

    private static readonly Regex MacAddressRegex =
        new(@"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$|^[0-9A-Fa-f]{12}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public MainWindow()
    {
        InitializeComponent();

        AdminHint.Text = IsRunningAsAdministrator()
            ? "Administrator mode: this window can execute command guidance with current privileges. Actual adapter writes still require PowerShell execution."
            : "Not running as administrator. You should run this app as admin to apply changes immediately. You can still generate and export a script for later.";

        AddActivity("Simple MAC Changer loaded. " + string.Join("; ", _features));
        RefreshAdapters();
    }

    private void OnRefreshAdapters(object sender, RoutedEventArgs e)
    {
        RefreshAdapters();
    }

    private void OnGenerateMac(object sender, RoutedEventArgs e)
    {
        var generated = GenerateRandomMac();
        TargetMacText.Text = generated;
        AddActivity($"Generated locally-administered MAC: {generated}");
    }

    private void OnValidateMac(object sender, RoutedEventArgs e)
    {
        var value = TargetMacText.Text.Trim();
        if (IsValidMac(value))
        {
            var normalized = NormalizeMac(value);
            AddActivity($"Validated MAC format: {NormalizeToDisplay(normalized)}");
            MessageBox.Show($"MAC address is valid.\n\n{NormalizeToDisplay(normalized)}", "Valid MAC", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AddActivity("MAC validation failed.");
        MessageBox.Show("MAC address format invalid.\nUse 12 hex chars (001122334455) or colon/hyphen separated pairs.", "Invalid MAC", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnBuildPlan(object sender, RoutedEventArgs e)
    {
        if (_selectedAdapter == null)
        {
            AddActivity("No adapter selected.");
            MessageBox.Show("Select an adapter first.", "Missing adapter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var targetRaw = TargetMacText.Text.Trim();
        if (!IsValidMac(targetRaw))
        {
            AddActivity("Cannot build plan because the target MAC is invalid.");
            MessageBox.Show("Enter a valid target MAC first.", "Invalid MAC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var target = NormalizeMac(targetRaw);
        if (!_originalMacByAdapter.TryGetValue(_selectedAdapter.InterfaceId, out var original))
        {
            AddActivity("Unable to determine baseline MAC for selected adapter.");
            return;
        }

        var plan = BuildChangePlan(_selectedAdapter.Name, original, target);
        Clipboard.SetText(plan);
        AddActivity($"Plan ready for {_selectedAdapter.Name}. Copied to clipboard.");
        MessageBox.Show(plan, "PowerShell Change Plan (copied)", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnCopyRestoreMac(object sender, RoutedEventArgs e)
    {
        if (_selectedAdapter == null)
        {
            AddActivity("No selected adapter to copy restore command for.");
            return;
        }

        if (!_originalMacByAdapter.TryGetValue(_selectedAdapter.InterfaceId, out var original))
        {
            AddActivity("No original MAC recorded for selected adapter.");
            return;
        }

        var restore = BuildRestorePlan(_selectedAdapter.Name, original);
        Clipboard.SetText(restore);
        AddActivity($"Restore command for {_selectedAdapter.Name} copied.");
        MessageBox.Show("Restore command copied to clipboard.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExportPlan(object sender, RoutedEventArgs e)
    {
        if (_selectedAdapter == null)
        {
            AddActivity("Export cancelled: no adapter selected.");
            MessageBox.Show("Select an adapter first.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_originalMacByAdapter.TryGetValue(_selectedAdapter.InterfaceId, out var original))
        {
            AddActivity("Cannot export plan: no baseline MAC captured for selected adapter.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export MAC change plan",
            Filter = "PowerShell Script|*.ps1|Text File|*.txt|All Files|*.*",
            FileName = $"{SanitizeFileName(_selectedAdapter.Name)}-mac-plan.ps1"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var targetRaw = TargetMacText.Text.Trim();
        if (!IsValidMac(targetRaw))
        {
            targetRaw = GenerateRandomMac();
        }

        var target = NormalizeMac(targetRaw);
        var script = BuildChangePlan(_selectedAdapter.Name, original, target);
        File.WriteAllText(dialog.FileName, script, Encoding.UTF8);
        AddActivity($"Plan exported to {dialog.FileName}");
        MessageBox.Show($"Plan exported.\n\n{dialog.FileName}", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnClearLog(object sender, RoutedEventArgs e)
    {
        ActivityList.Items.Clear();
    }

    private void OnAdapterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedAdapter = AdapterList.SelectedItem as AdapterInfo;
        if (_selectedAdapter is null)
        {
            CurrentAdapterText.Text = "No adapter selected";
            SelectionSummary.Text = string.Empty;
            return;
        }

        TargetMacText.Text = _selectedAdapter.MacAddress;
        CurrentAdapterText.Text = $"{_selectedAdapter.Name} ({_selectedAdapter.Type})";
        SelectionSummary.Text = $"Interface ID: {_selectedAdapter.InterfaceId} | Current MAC: {NormalizeToDisplay(_selectedAdapter.MacAddress)}";

        if (!_originalMacByAdapter.ContainsKey(_selectedAdapter.InterfaceId))
        {
            _originalMacByAdapter[_selectedAdapter.InterfaceId] = _selectedAdapter.MacAddress;
        }

        AddActivity($"Selected adapter: {_selectedAdapter.Name}");
    }

    private void RefreshAdapters()
    {
        AdapterList.Items.Clear();
        _selectedAdapter = null;
        CurrentAdapterText.Text = "No adapter selected";
        SelectionSummary.Text = string.Empty;
        _originalMacByAdapter.Clear();

        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus != OperationalStatus.Unknown)
            .OrderBy(nic => nic.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var nic in adapters)
        {
            var info = new AdapterInfo(
                name: nic.Name,
                type: nic.NetworkInterfaceType.ToString(),
                status: nic.OperationalStatus.ToString(),
                macAddress: NormalizeMac(nic.GetPhysicalAddress().ToString()),
                interfaceId: nic.Id);

            AdapterList.Items.Add(info);
            _originalMacByAdapter[info.InterfaceId] = info.MacAddress;
        }

        AddActivity($"Loaded {adapters.Count} network adapter entries.");
        if (adapters.Count == 0)
        {
            AddActivity("No adapters found. Check NIC permissions or run in normal PowerShell context.");
        }
    }

    private static string BuildChangePlan(string adapterName, string originalMac, string targetMac)
    {
        var safeName = adapterName.Replace("'", "''", StringComparison.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("# PowerShell plan generated by Simple MAC Changer");
        sb.AppendLine($"# Adapter: {adapterName}");
        sb.AppendLine("# Run this in an elevated PowerShell prompt.");
        sb.AppendLine();
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        sb.AppendLine($"$adapter = '{safeName}'");
        sb.AppendLine($"$newMac = '{NormalizeToDisplay(targetMac)}'");
        sb.AppendLine($"$oldMac = '{NormalizeToDisplay(originalMac)}'");
        sb.AppendLine("Write-Host \"Original MAC: $oldMac\"");
        sb.AppendLine("Write-Host \"Applying new MAC: $newMac\"");
        sb.AppendLine("Set-NetAdapter -Name $adapter -MacAddress ($newMac -replace '[: -]') -ErrorAction Stop");
        sb.AppendLine("Disable-NetAdapter -Name $adapter -Confirm:$false");
        sb.AppendLine("Enable-NetAdapter -Name $adapter -Confirm:$false");
        sb.AppendLine("Write-Host 'Done. If needed, disable/enable adapter or reboot.'");
        return sb.ToString();
    }

    private static string BuildRestorePlan(string adapterName, string restoreMac)
    {
        var safeName = adapterName.Replace("'", "''", StringComparison.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("# Restore plan generated by Simple MAC Changer");
        sb.AppendLine($"# Adapter: {adapterName}");
        sb.AppendLine("# Run this in an elevated PowerShell prompt.");
        sb.AppendLine();
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        sb.AppendLine($"$adapter = '{safeName}'");
        sb.AppendLine($"$restoreMac = '{NormalizeToDisplay(restoreMac)}'");
        sb.AppendLine("Set-NetAdapter -Name $adapter -MacAddress ($restoreMac -replace '[: -]') -ErrorAction Stop");
        sb.AppendLine("Disable-NetAdapter -Name $adapter -Confirm:$false");
        sb.AppendLine("Enable-NetAdapter -Name $adapter -Confirm:$false");
        return sb.ToString();
    }

    private static string NormalizeMac(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = Regex.Replace(value, "[-:\\s]", string.Empty, RegexOptions.CultureInvariant);
        return cleaned.Length >= 12 ? cleaned[..12] : cleaned;
    }

    private static string NormalizeToDisplay(string hex12)
    {
        if (string.IsNullOrWhiteSpace(hex12))
        {
            return string.Empty;
        }

        var pairs = Enumerable.Range(0, 6).Select(i => hex12[(i * 2)..(i * 2 + 2)]);
        return string.Join(":", pairs).ToUpperInvariant();
    }

    private static bool IsValidMac(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeMac(value);
        if (normalized.Length != 12)
        {
            return false;
        }

        var asPairs = string.Join(":", Enumerable.Range(0, 6)
            .Select(i => normalized.AsSpan(i * 2, 2).ToString()));
        return MacAddressRegex.IsMatch(asPairs);
    }

    private static string GenerateRandomMac()
    {
        Span<byte> bytes = stackalloc byte[6];
        new Random().NextBytes(bytes);

        // Make it unicast + locally administered.
        bytes[0] = (byte)((bytes[0] & 0xFC) | 0x02);
        return string.Concat(bytes.ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(input.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "adapter" : cleaned;
    }

    private void AddActivity(string message)
    {
        if (ActivityList.Items.Count > 200)
        {
            ActivityList.Items.RemoveAt(ActivityList.Items.Count - 1);
        }

        ActivityList.Items.Insert(0, $"{DateTime.Now:t} - {message}");
        StatusText.Text = message;
    }

    private sealed class AdapterInfo
    {
        public AdapterInfo(string name, string type, string status, string macAddress, string interfaceId)
        {
            Name = name;
            Type = type;
            Status = status;
            MacAddress = macAddress;
            InterfaceId = interfaceId;
        }

        public string Name { get; }
        public string Type { get; }
        public string Status { get; }
        public string MacAddress { get; }
        public string InterfaceId { get; }

        public override string ToString() => Name;
    }
}
