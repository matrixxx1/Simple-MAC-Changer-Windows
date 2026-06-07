using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Services.Store;

namespace SimpleMacChanger;

public partial class MainWindow : Window
{
    private const int TrialWindowDays = 15;
    private const int TrialDailyMinutesLimit = 15;
    private readonly string _logRoot;
    private readonly string _logFilePath;
    private readonly string _usagePath;
    private readonly string _statePath;

    private readonly Dictionary<string, string> _originalMacByAdapter = new();
    private readonly DispatcherTimer _usageTimer;
    private AdapterInfo? _selectedAdapter;
    private StoreLicenseState _licenseState = new();
    private UsageState _usageState = new();

    private static readonly Regex MacAddressRegex =
        new(@"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$|^[0-9A-Fa-f]{12}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public MainWindow()
    {
        InitializeComponent();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _logRoot = Path.Combine(appData, "SimpleMacChanger");
        Directory.CreateDirectory(_logRoot);
        _logFilePath = Path.Combine(_logRoot, "activity.log");
        _usagePath = Path.Combine(_logRoot, "runtime-usage.json");
        _statePath = Path.Combine(_logRoot, "trial-state.json");

        _usageTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _usageTimer.Tick += OnUsageTimerTick;

        Loaded += async (_, _) =>
        {
            AddActivity("Simple MAC Changer initialized.");
            AdminHint.Text = IsRunningAsAdministrator()
                ? "Administrator mode: plan scripts are ready for immediate privileged execution in an elevated PowerShell prompt."
                : "Not running as administrator. You should run this app as admin to apply changes immediately. You can still generate/export scripts.";

            _usageState = LoadUsage();
            await RefreshLicenseAsync();
            RefreshAdapters();
            ApplyUsageStateTimer();
            UpdateLicenseDisplay();
        };
    }

    private void OnUsageTimerTick(object? sender, EventArgs e)
    {
        if (!RequiresLimitedUsage)
        {
            _usageTimer.Stop();
            return;
        }

        if (!_usageState.ActiveDate.HasValue || _usageState.ActiveDate.Value.Date != DateTime.UtcNow.Date)
        {
            _usageState = new UsageState
            {
                ActiveDate = DateTime.UtcNow,
                MinutesUsedToday = 0
            };
        }

        if (_usageState.MinutesUsedToday < TrialDailyMinutesLimit)
        {
            _usageState.MinutesUsedToday += 1;
            PersistUsage();
            UpdateLicenseDisplay();
        }

        if (_usageState.MinutesUsedToday == TrialDailyMinutesLimit)
        {
            AddActivity("Daily trial usage limit reached for today.");
        }
    }

    private async System.Threading.Tasks.Task RefreshLicenseAsync()
    {
        try
        {
            var context = StoreContext.GetDefault();
            var appLicense = await context.GetAppLicenseAsync();

            var isTrial = appLicense.IsTrial;
            var trialRemaining = isTrial
                ? CalculateRemainingTrialDays(appLicense.ExpirationDate.UtcDateTime)
                : 0;

            _licenseState = new StoreLicenseState
            {
                HasStoreLicenseInfo = true,
                LicenseSource = "Microsoft Store API",
                IsTrial = isTrial,
                IsPaid = !isTrial && appLicense.IsActive,
                IsStoreInstalled = true,
                IsActive = appLicense.IsActive,
                TrialExpiry = appLicense.ExpirationDate,
                TrialDaysRemaining = Math.Max(0, trialRemaining)
            };

            return;
        }
        catch
        {
            // StoreContext may not be available for unpacked/dev builds.
        }

        var localState = LoadTrialState();
        var now = DateTime.UtcNow;
        if (!localState.FirstRunDate.HasValue)
        {
            localState.FirstRunDate = now;
            SaveTrialState(localState);
        }

        var trialExpires = localState.FirstRunDate.Value.AddDays(TrialWindowDays);
        _licenseState = new StoreLicenseState
        {
            HasStoreLicenseInfo = false,
            LicenseSource = "Local fallback",
            IsTrial = true,
            IsPaid = false,
            IsActive = true,
            IsLocalFallback = true,
            TrialExpiry = new DateTimeOffset(trialExpires),
            TrialDaysRemaining = Math.Max(0, CalculateRemainingTrialDays(trialExpires))
        };

        if (_licenseState.TrialDaysRemaining <= 0)
        {
            _licenseState.IsActive = true;
        }
    }

    private bool RequiresLimitedUsage => _licenseState.RequiresLimitedUsageAfterTrial;

    private void ApplyUsageStateTimer()
    {
        if (_licenseState.RequiresLimitedUsageAfterTrial)
        {
            if (!_usageTimer.IsEnabled)
            {
                _usageTimer.Start();
            }
        }
        else
        {
            if (_usageTimer.IsEnabled)
            {
                _usageTimer.Stop();
            }
        }
    }

    private void OnRefreshAdapters(object sender, RoutedEventArgs e)
    {
        if (!CanUseRestrictedFeature("refresh adapters"))
        {
            return;
        }

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
        if (!CanUseRestrictedFeature("prepare change plan"))
        {
            return;
        }

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
        if (!CanUseRestrictedFeature("copy restore command"))
        {
            return;
        }

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
        if (!CanUseRestrictedFeature("export plan"))
        {
            return;
        }

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
        AddActivity("In-memory activity log cleared.");
    }

    private void OnViewLogs(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_logFilePath))
        {
            MessageBox.Show("Log file does not exist yet.", "Logs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{_logFilePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open log file.\n\n{ex.Message}", "Logs", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnShowAbout(object sender, RoutedEventArgs e)
    {
        var licenseMode = _licenseState.RequiresLimitedUsageAfterTrial
            ? "Post-trial limited mode"
            : "Paid or active trial mode";

        MessageBox.Show(
            "Simple MAC Changer\n\n" +
            $"Licensing model: {licenseMode}\n" +
            "• $1.99 USD one-time store purchase\n" +
            "• 15-day fully functional trial\n" +
            "• After trial: 15 minutes/day usage cap until purchase\n" +
            $"• Store API verification: {_licenseState.GetDisplayHeadline()}\n" +
            $"• Verification source: {_licenseState.LicenseSource}\n" +
            $"• Price: {_licenseState.GetPriceDisplay()}\n" +
            "• Log files are stored at: " + _logFilePath,
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
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

        TargetMacText.Text = _selectedAdapter.DisplayMac;
        CurrentAdapterText.Text = $"{_selectedAdapter.Name} ({_selectedAdapter.Type})";
        SelectionSummary.Text = $"Interface ID: {_selectedAdapter.InterfaceId} | Current MAC: {_selectedAdapter.DisplayMac}";

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

    private bool CanUseRestrictedFeature(string action)
    {
        if (_licenseState.HasUnlimitedAccess())
        {
            return true;
        }

        if (!_usageState.ActiveDate.HasValue || _usageState.ActiveDate.Value.Date != DateTime.UtcNow.Date)
        {
            _usageState = new UsageState
            {
                ActiveDate = DateTime.UtcNow,
                MinutesUsedToday = 0
            };
            PersistUsage();
            UpdateLicenseDisplay();
        }

        if (_usageState.MinutesUsedToday < TrialDailyMinutesLimit)
        {
            return true;
        }

        AddActivity($"Blocked while feature usage limit reached: {action}");
        MessageBox.Show("You are in post-trial limited mode with no remaining minutes today. Purchase from Store to continue.", "Trial limit reached", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void UpdateLicenseDisplay()
    {
        LicenseText.Text = _licenseState.GetDisplayHeadline();

        if (_licenseState.RequiresLimitedUsageAfterTrial)
        {
            if (_licenseState.IsTrial)
            {
                LicenseDetailsText.Text = $"Trial window remaining: {_licenseState.TrialDaysRemaining} days";
                UsageText.Text = "Daily usage limit applies after trial window ends.";
            }
            else
            {
                LicenseDetailsText.Text = "Store license was checked, trial window not active.";
                UsageText.Text = $"Daily limit: {_usageState.MinutesUsedToday}/{TrialDailyMinutesLimit} minutes used today.";
            }
        }
        else
        {
            LicenseDetailsText.Text = _licenseState.IsPaid
                ? "License source: Store purchase/license"
                : "License source: Active trial period";
            UsageText.Text = "Usage limit: Unlimited";
        }

        if (_licenseState.IsTrial && _licenseState.TrialExpiry.HasValue)
        {
            var expiry = _licenseState.TrialExpiry.Value.ToLocalTime();
            LicenseDetailsText.Text += $"\nTrial ends: {expiry:G}";
        }

        if (!string.IsNullOrWhiteSpace(_licenseState.LicenseSource))
        {
            LicenseDetailsText.Text += $"\nLicense lookup: {_licenseState.LicenseSource}";
        }

        if (_licenseState.IsActive is false)
        {
            LicenseDetailsText.Text += "\nLicense is not active in current context.";
        }
    }

    private static string BuildChangePlan(string adapterName, string originalMac, string targetMac)
    {
        var safeName = adapterName.Replace("'", "''", StringComparison.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("# PowerShell plan generated by Simple MAC Changer");
        sb.AppendLine("# Requires administrator context for immediate apply.");
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
        sb.AppendLine("# Run this in an elevated PowerShell prompt.");
        sb.AppendLine($"# Adapter: {adapterName}");
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
        var bytes = new byte[6];
        Random.Shared.NextBytes(bytes);

        // Make it unicast + locally administered.
        bytes[0] = (byte)((bytes[0] & 0xFC) | 0x02);
        return string.Concat(bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
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
        if (ActivityList.Items.Count > 250)
        {
            ActivityList.Items.RemoveAt(ActivityList.Items.Count - 1);
        }

        var line = $"{DateTime.UtcNow:O} - {message}";
        ActivityList.Items.Insert(0, line);
        StatusText.Text = message;

        try
        {
            File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Non-fatal; log in UI only.
        }
    }

    private TrialState LoadTrialState()
    {
        if (!File.Exists(_statePath))
        {
            return new TrialState();
        }

        try
        {
            var raw = File.ReadAllText(_statePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<TrialState>(raw) ?? new TrialState();
        }
        catch
        {
            return new TrialState();
        }
    }

    private void SaveTrialState(TrialState state)
    {
        try
        {
            Directory.CreateDirectory(_logRoot);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private void PersistUsage()
    {
        try
        {
            Directory.CreateDirectory(_logRoot);
            var snapshot = JsonSerializer.Serialize(_usageState);
            File.WriteAllText(_usagePath, snapshot, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private UsageState LoadUsage()
    {
        if (!File.Exists(_usagePath))
        {
            return new UsageState { ActiveDate = DateTime.UtcNow, MinutesUsedToday = 0 };
        }

        try
        {
            var raw = File.ReadAllText(_usagePath, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<UsageState>(raw);
            return state ?? new UsageState { ActiveDate = DateTime.UtcNow, MinutesUsedToday = 0 };
        }
        catch
        {
            return new UsageState { ActiveDate = DateTime.UtcNow, MinutesUsedToday = 0 };
        }
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
            DisplayMac = NormalizeToDisplay(macAddress);
        }

        public string Name { get; }
        public string Type { get; }
        public string Status { get; }
        public string MacAddress { get; }
        public string DisplayMac { get; }
        public string InterfaceId { get; }

        public override string ToString() => Name;
    }

    private sealed class StoreLicenseState
    {
        public bool HasStoreLicenseInfo { get; set; }
        public string? LicenseSource { get; set; }
        public bool IsTrial { get; set; }
        public bool IsPaid { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsStoreInstalled { get; set; }
        public bool IsLocalFallback { get; set; }
        public int TrialDaysRemaining { get; set; }
        public DateTimeOffset? TrialExpiry { get; set; }

        public bool RequiresTrialWindow => !HasUnlimitedAccess();
        public bool RequiresLimitedUsageAfterTrial => !HasUnlimitedAccess();

        public bool HasUnlimitedAccess()
        {
            if (!IsActive)
            {
                return false;
            }

            if (IsPaid)
            {
                return true;
            }

            return IsTrial && TrialDaysRemaining > 0;
        }

        public string GetDisplayHeadline() =>
            HasUnlimitedAccess() ? "License status: Paid or active trial" : "License status: Limited trial-after mode";

        public string GetPriceDisplay() => "$1.99 USD";
    }

    private sealed class UsageState
    {
        public DateTime? ActiveDate { get; set; }
        public int MinutesUsedToday { get; set; }
    }

    private sealed class TrialState
    {
        public DateTime? FirstRunDate { get; set; }
    }

    private static int CalculateRemainingTrialDays(DateTimeOffset targetUtc)
    {
        return (int)Math.Ceiling((targetUtc.UtcDateTime - DateTime.UtcNow).TotalDays);
    }

    private static int CalculateRemainingTrialDays(DateTime targetUtc)
    {
        return (int)Math.Ceiling((targetUtc - DateTime.UtcNow).TotalDays);
    }
}





