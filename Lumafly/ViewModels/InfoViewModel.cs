using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using MsBox.Avalonia.Enums;
using PropertyChanged.SourceGenerator;
using Lumafly.Enums;
using Lumafly.Interfaces;
using Lumafly.Models;
using Lumafly.Services;
using Lumafly.Util;

namespace Lumafly.ViewModels;

public partial class InfoViewModel : ViewModelBase
{
    private readonly IInstaller _installer;
    private readonly IModSource _modSource;
    private readonly ISettings _settings;
    private readonly IUrlSchemeHandler _urlSchemeHandler;
    private readonly HttpClient _hc;
    
    [Notify]
    private bool _isLaunchingGame;
    [Notify]
    private string _additionalInfo = "";
    [Notify]
    private bool _additionalInfoVisible;
    
    public InfoViewModel(IInstaller installer, IModSource modSource ,ISettings settings, HttpClient hc, IUrlSchemeHandler urlSchemeHandler)
    {
        Trace.WriteLine("Initializing InfoViewModel");
        _installer = installer;
        _modSource = modSource;
        _settings = settings;
        _hc = hc;
        _urlSchemeHandler = urlSchemeHandler;
        Task.Run(FetchAdditionalInfo);
        Dispatcher.UIThread.Invoke(() => HandleLaunchUrlScheme(_urlSchemeHandler));
    }
    public void OpenLink(object link) => Process.Start(new ProcessStartInfo((string)link) { UseShellExecute = true });

    private const string hollow_knight = "hollow_knight";
    private const string HollowKnight = "Hollow Knight";

    public async Task LaunchGame(object _isVanilla) => await _LaunchGame(bool.Parse((string) _isVanilla));
    
    
    /// <summary>
    /// Launches the game
    /// </summary>
    /// <param name="isVanilla">Set to true for vanilla game, set to false for modded game and set to null for no change to current api state</param>
    private async Task _LaunchGame(bool? isVanilla)
    {
        Trace.WriteLine("Launching game");
        IsLaunchingGame = true;
        try
        {
            // remove any existing hk instance
            try
            {
                foreach (var proc in Process.GetProcesses()
                    .Where(static p => p.ProcessName.StartsWith(hollow_knight) || p.ProcessName.StartsWith(HollowKnight)))
                {
                    var killed = false;
                    try
                    {
                        // don't waste more than 5 secs on this
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        if (killed = !proc.CloseMainWindow())
                            proc.Kill(true);
                        else
                            Trace.WriteLine("found a window to close"); // TODO: remove
                        await proc.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (!killed)
                            proc.Kill(true); // might as well still try a kill
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                if (!await DisplayErrors.DisplayAreYouSureWarning(Resources.HKAlreadyRunning.Replace("\\n", "\n")))
                    return;
            } // this is not vital

            await _installer.CheckAPI();

            if (isVanilla != null)
            {
                if (!(_modSource.ApiInstall is NotInstalledState or InstalledState { Enabled: false } && isVanilla.Value
                      || _modSource.ApiInstall is InstalledState { Enabled: true } && !isVanilla.Value))
                {
                    await ModListViewModel.ToggleApiCommand(_modSource, _installer);
                }
            }

            var exeDetails = GetExecutableDetails();

            if (exeDetails.isSteam)
            {
                Process.Start(new ProcessStartInfo("steam://rungameid/367520")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exeDetails.name,
                    WorkingDirectory = exeDetails.path,
                    UseShellExecute = true,
                });
            }

        }
        catch (Exception e)
        {
            await DisplayErrors.DisplayGenericError($"Unable to launch the game", e);
        }
        finally
        {
            IsLaunchingGame = false;
        }
    }

    private (string path, string name, bool isSteam) GetExecutableDetails()
    {
        string exeName;
        
        // get exe path
        var managedFolder = new DirectoryInfo(_settings.ManagedFolder);
        var managedParent = managedFolder.Parent; // now in hollow_knight_data or (for mac) data folder
            
        var hkExeFolder = managedParent!.Parent; // now in the hk exe folder or (for mac) resources folder;
            
        // mac os path has 2 extra folders
        if (OperatingSystem.IsMacOS())
        {
            // an executable exists at hollow_knight.app/Contents/MacOS/Hollow Knight
            // I am unsure if directly running it works, but I have now way to test
            hkExeFolder = hkExeFolder!.Parent!; // now in contents folder
            hkExeFolder = new(Path.Combine(hkExeFolder.FullName, "MacOS"));
            exeName = HollowKnight;
        }
        else
        {
            exeName = managedParent.Name.Replace("_Data", string.Empty); //unity appends _Data to end of exe name
        }

        if (OperatingSystem.IsWindows())
            exeName += ".exe";
        else if (OperatingSystem.IsLinux())
            exeName += ".x86_64";

        if (hkExeFolder is null) throw new Exception("Hollow Knight executable not found");
        string exePath = hkExeFolder.FullName;
        
        // check if path contains steam_api file
        var pluginsFolder = Path.Combine(managedParent.FullName, "Plugins");
        if (OperatingSystem.IsMacOS())
            pluginsFolder = Path.Combine(hkExeFolder.Parent!.FullName, "PlugIns");
        
        var isSteam = false;
        // avoid notfound exceptions in the rare case folder structure changes
        if (Directory.Exists(pluginsFolder))
        {
            isSteam = Directory.EnumerateFiles(
                pluginsFolder,
                "*steam_api*",
                SearchOption.AllDirectories
            ).Any();
        }
        
        return (exePath, exeName, isSteam);
    }

    public async Task FetchAdditionalInfo()
    {
        const string additionalInfoLink = "https://raw.githubusercontent.com/TheMulhima/Lumafly/static-resources/AdditionalInfo.md";
        try
        {
            AdditionalInfo = await _hc.GetStringAsync2(
                _settings,
                new Uri(additionalInfoLink),
                new CancellationTokenSource(ModDatabase.TIMEOUT).Token);
            
            if (!string.IsNullOrEmpty(AdditionalInfo)) 
                AdditionalInfoVisible = true;
        }
        catch (Exception)
        {
            // ignored not important
        }
    }
    
    private async Task HandleLaunchUrlScheme(IUrlSchemeHandler urlSchemeHandler)
    {
        if (urlSchemeHandler is { Handled: false, UrlSchemeCommand: UrlSchemeCommands.launch })
        {
            if (urlSchemeHandler.Data is "")
                await _LaunchGame(null);
            else if (urlSchemeHandler.Data.ToLower() is "vanilla" or "false")
                await _LaunchGame(true);
            else if (urlSchemeHandler.Data.ToLower() is "modded" or "true")
                await _LaunchGame(false);
            else
                await _urlSchemeHandler.ShowConfirmation("Launch Game", 
                    "Launch game command is invalid. Please specify the launch as vanilla or modded or leave blank for regular launch", 
                    Icon.Warning);

            _urlSchemeHandler.FinishHandlingUrlScheme();
        }
    }
}