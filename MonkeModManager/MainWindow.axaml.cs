using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DiscordRPC;
using Newtonsoft.Json;
using Button = Avalonia.Controls.Button;

namespace MonkeModManager;

public partial class MainWindow : Window
{
    #region Properties
    public ObservableCollection<Mod> Mods { get; } = new();
    private static List<Border> ModControls = new();
    private string gamePath;
    private string pluginsPath;
    private readonly HttpClient httpClient = new();
    Theme CurrentTheme;
    private DiscordRpcClient client;
    #endregion
    
    #region Window And Init
    public MainWindow()
    {
        DataContext = this;
        InitializeComponent();
        
        this.Opened += async (s, e) =>
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorMessage($"Initialization failed: {ex.Message}");
            }
        };
    }

    private async Task InitializeAsync()
    {
        if (!File.Exists(GetConfigPath()))
        {
            var selectedPath = await ShowGamePathDialog();
            if (string.IsNullOrEmpty(selectedPath))
            {
                Close();
                return;
            }
            await SaveConfig(selectedPath, GetTheme());
        }
        else
        {
            gamePath = GetGamePath();
            if (string.IsNullOrEmpty(gamePath))
            {
                await ShowErrorMessage("select a game path");
                var selectedPath = await ShowGamePathDialog();
                if (string.IsNullOrEmpty(selectedPath))
                {
                    Close();
                    return;
                }

                await SaveConfig(selectedPath, GetTheme());
            }
        }
        pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");

        var (updateAvailable, newVersion) = await IsUpdateAvailable(new Version(1, 2));

        if (updateAvailable)
        {
            await NewVersionDialog(newVersion);
        }
        CurrentTheme = GetTheme();
        switch (CurrentTheme)
        {
            case MonkeModManager.Theme.Dark:
                MainGrid.Background = Brush.Parse("#121212");
                TitleText.Foreground = Brush.Parse("#E0E0E0");
                ModsThingy.Background = Brush.Parse("#121212");
                ModsThingy.BorderBrush = Brush.Parse("#2C2C2C");
                MsgBox0Border.Background = Brush.Parse("#2C2C2C");
                MessageBox0.Foreground = Brush.Parse("#E0E0E0");
                break;
            case MonkeModManager.Theme.Light:
                MainGrid.Background = Brush.Parse("#FFFFFF");
                TitleText.Foreground = Brush.Parse("#212121");
                ModsThingy.Background = Brush.Parse("#FFFFFF");
                ModsThingy.BorderBrush = Brush.Parse("#E0E0E0");
                MsgBox0Border.Background = Brush.Parse("#E0E0E0");
                MessageBox0.Foreground = Brush.Parse("#212121");
                break;
            case MonkeModManager.Theme.DarkHighContrast:
                MainGrid.Background = Brush.Parse("#000000");
                TitleText.Foreground = Brush.Parse("#FFFFFF");
                ModsThingy.Background = Brush.Parse("#000000");
                ModsThingy.BorderBrush = Brush.Parse("#FFFFFF");
                MsgBox0Border.Background = Brush.Parse("#000000");
                MessageBox0.Foreground = Brush.Parse("#FFFFFF");
                break;
            case MonkeModManager.Theme.Sunrise:
                MainGrid.Background = Brush.Parse("#F5E0C3");
                TitleText.Foreground = Brush.Parse("#4E342E");
                ModsThingy.Background = Brush.Parse("#F5E0C3");
                ModsThingy.BorderBrush = Brush.Parse("#D7CCC8");
                MsgBox0Border.Background = Brush.Parse("#FFE0B2");
                MessageBox0.Foreground = Brush.Parse("#6D4C41");
                break;
            case MonkeModManager.Theme.Frost:
                MainGrid.Background = Brush.Parse("#D0E1F9");
                TitleText.Foreground = Brush.Parse("#0D47A1");
                ModsThingy.Background = Brush.Parse("#D0E1F9");
                ModsThingy.BorderBrush = Brush.Parse("#90A4AE");
                MsgBox0Border.Background = Brush.Parse("#BBDEFB");
                MessageBox0.Foreground = Brush.Parse("#0D47A1");
                break;
        }

        InitForRPC();
        await CheckOrInstallBepInEx();
        await LoadModsFromTheNewGitHubRepoAsync();
    }
    void InitForRPC()
    {
        client = new DiscordRpcClient("1389965977705513010");

        client.OnReady += (sender, e) =>
        {
            Console.WriteLine($"[DiscordRPC] - Connected to Discord as {e.User.Username}");
            client.SetPresence(new RichPresence
            {
                Buttons = new[]
                {
                    new DiscordRPC.Button
                    {
                        Label = "Install Mods too",
                        Url = "https://github.com/arielthemonke/MonkeModManager/releases/latest"
                    },
                },
                Details = "Modding",
                State = "Installing Mods",
            });
            Console.WriteLine($"[DiscordRPC] - RPC Set");
        };

        client.OnError += (sender, e) =>
        {
            Console.WriteLine($"[ERROR][DiscordRPC] - {e.Message}");
        };
        
        client.OnConnectionFailed += (sender, e) =>
        {
            Console.WriteLine($"[ERROR][DiscordRPC] - Connection failed: {e}");
        };

        client.OnConnectionEstablished += (sender, e) =>
        {
            Console.WriteLine("[DiscordRPC] - RPC Connection established");
        };

        client.Initialize();
    }

    public static async Task<(bool UpdateAvailable, Version LatestVersion)> IsUpdateAvailable(Version currentVersion)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "ModManager");

            string url = "https://raw.githubusercontent.com/arielthemonke/MonkeModManager/main/version";
            string response = await client.GetStringAsync(url);
            var latestVersion = Version.Parse(response);

            return (currentVersion < latestVersion, latestVersion);
        }
        catch
        {
            return (false, currentVersion);
        }
    }
    #endregion
    
    #region Configs And Stuff
    private static string GetConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "MonkeModManager");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "config.json");
    }

    private static Theme GetTheme()
    {
        try
        {
            var json = File.ReadAllText(GetConfigPath());
            var config = JsonConvert.DeserializeObject<Config>(json);
            if (!string.IsNullOrWhiteSpace(config.Theme.ToString()))
            {
                return config.Theme;
            }
            else
            {
                return MonkeModManager.Theme.Light;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
        
    }
    #endregion

    #region Mod Loading
    private readonly HashSet<string> BlacklistedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "BepInEx"
        // made it like this because i might add more later but idk
    };

    public async Task LoadModsFromTheNewGitHubRepoAsync()
    {
        using var client = new HttpClient();
        const string url = "https://raw.githubusercontent.com/The-Graze/MonkeModInfo/master/modinfo.json";

        try
        {
            var json = await client.GetStringAsync(url);
            var mods = JsonConvert.DeserializeObject<List<Mod>>(json);
            Mods.Clear();
            ItemControl0.Items.Clear();
        
            if (mods != null)
            {
                var loadedCount = 0;
                var blacklistedCount = 0;
            
                foreach (var mod in mods)
                {
                    if (BlacklistedMods.Contains(mod.Name))
                    {
                        blacklistedCount++;
                        Console.WriteLine($"skipped: {mod.Name}");
                        continue;
                    }
                
                    Mods.Add(mod);
                    var modControl = MakeModControl(mod);
                    ItemControl0.Items.Add(modControl);
                    loadedCount++;
                }
            
                Console.WriteLine($"loaded {loadedCount} mods, skipped {blacklistedCount} blacklisted mods");
                // even counting!! not that anyone will read this but yeah
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Loading mods failed: {ex.Message}");
            await ShowErrorMessage("Couldn't load the mod list from GitHub.");
        }
    }
    
    public async Task fixBepInExConfig()
    {
        string url = "https://raw.githubusercontent.com/arielthemonke/ModInfo/main/BepInEx.cfg";
        string configPath = Path.Combine(gamePath, "BepInEx", "config", "BepInEx.cfg");

        try
        {
            using var client = new HttpClient();
            var configContent = await client.GetStringAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            await File.WriteAllTextAsync(configPath, configContent);

            Console.WriteLine("yay i did a thing!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"never mind, it didnt work :sob: {ex.Message}");
            await ShowErrorMessage("it didnt work. error code: 50000000");
        }
    }
    #endregion

    #region UI And Stuff
    private async Task SaveConfig(string path, Theme theme)
    {
        gamePath = path;
        CurrentTheme = theme;
        var config = new Config { GamePath = path, Theme = theme};
        var json = JsonConvert.SerializeObject(config, Formatting.Indented);
        await File.WriteAllTextAsync(GetConfigPath(), json);
        MessageBox0.Text = "Game path saved successfully!";
    }
    
    private void ShowDropdown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.Open(button);
        }
    }

    void SwitchTheme(object sender, RoutedEventArgs e)
    {
        var currentTheme = GetTheme();
        var themes = Enum.GetValues<Theme>();
        var currentIndex = Array.IndexOf(themes, currentTheme);
        var nextIndex = (currentIndex + 1) % themes.Length;
            
        CurrentTheme = themes[nextIndex];

        var config = new Config { GamePath = GetGamePath(), Theme = CurrentTheme };
        var json = JsonConvert.SerializeObject(config);
        File.WriteAllText(GetConfigPath(), json);
        RestartApp();
    }
    void RestartApp()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else
        {
            Environment.Exit(0);
        }
    }

    private Border MakeModControl(Mod mod)
    {
        var border = new Border
        {
            Tag = mod.Name,
            BorderThickness = new Thickness(2),
            BorderBrush = GetGroupColor(mod.Group),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(6),
            Padding = new Thickness(12),
            Background = Brushes.White
        };

        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var contentStack = new StackPanel
        {
            Spacing = 4
        };

        var nameTextBlock = new TextBlock
        {
            Text = mod.DisplayName,
            FontSize = 18,
            FontWeight = FontWeight.Bold
        };
        var infoPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };

        TextBlock authorTextBlock = null;
        if (!string.IsNullOrEmpty(mod.Author))
        {
            authorTextBlock = new TextBlock
            {
                Text = mod.AuthorInfo,
                FontSize = 12,
                FontStyle = FontStyle.Italic
            };
            infoPanel.Children.Add(authorTextBlock);
        }

        TextBlock groupText = null;
        if (!string.IsNullOrEmpty(mod.Group))
        {
            var groupBadge = new Border
            {
                Background = GetGroupColor(mod.Group),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2)
            };
            groupText = new TextBlock
            {
                Text = mod.Group,
                FontSize = 10,
                FontWeight = FontWeight.Bold
            };
            groupBadge.Child = groupText;
            infoPanel.Children.Add(groupBadge);
        }

        TextBlock dependenciesText = null;
        if (mod.Dependencies?.Any() == true)
        {
            dependenciesText = new TextBlock
            {
                Text = $"Dependencies: {string.Join(", ", mod.Dependencies)}",
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            };
        }

        var urlTextBlock = new TextBlock
        {
            Text = ShortenUrl(mod.DownloadUrl),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap
        };

        var isInstalled = IsModInstalled(mod);
        var statusText = new TextBlock
        {
            Name = "status",
            Text = isInstalled ? "\u2713 Installed" : "Not installed",
            FontSize = 12,
            Foreground = isInstalled ? Brushes.Green : Brushes.Orange
        };

        contentStack.Children.Add(nameTextBlock);
        contentStack.Children.Add(infoPanel);
        if (dependenciesText != null)
            contentStack.Children.Add(dependenciesText);
        contentStack.Children.Add(urlTextBlock);
        contentStack.Children.Add(statusText);

        var buttonStack = new StackPanel
        {
            Name = "ButtonStack",
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var installButton = new Button
        {
            Name = "InstallButton",
            Content = isInstalled ? "Reinstall" : "Install",
            Background = isInstalled ? Brushes.Orange : Brushes.Green,
            Foreground = Brushes.White,
            Padding = new Thickness(16, 8),
            CornerRadius = new CornerRadius(4)
        };

        installButton.Click += async (s, e) =>
        {
            await InstallMod(mod, installButton, statusText);
        };

        if (isInstalled)
        {
            var uninstallButton = new Button
            {
                Content = "Uninstall",
                Background = Brushes.Red,
                Foreground = Brushes.White,
                Padding = new Thickness(16, 8),
                CornerRadius = new CornerRadius(4)
            };

            uninstallButton.Click += async (s, e) =>
            {
                await UninstallMod(mod, installButton, uninstallButton, statusText);
            };

            buttonStack.Children.Add(uninstallButton);
        }

        buttonStack.Children.Add(installButton);

        Grid.SetColumn(contentStack, 0);
        Grid.SetColumn(buttonStack, 1);

        mainGrid.Children.Add(contentStack);
        mainGrid.Children.Add(buttonStack);
        contentStack.Name = "ContentStack";

        border.Child = mainGrid;
        
        switch (CurrentTheme)
        {
            case MonkeModManager.Theme.Light:
                border.Background = Brush.Parse("#F9F9F9");
                nameTextBlock.Foreground = Brush.Parse("#212121");
                if (authorTextBlock != null) authorTextBlock.Foreground = Brush.Parse("#212121");
                if (dependenciesText != null) dependenciesText.Foreground = Brush.Parse("#666666");
                if (groupText != null) groupText.Foreground = Brush.Parse("#FFFFFF");
                urlTextBlock.Foreground = Brush.Parse("#212121");
                break;
            case MonkeModManager.Theme.Dark:
                border.Background = Brush.Parse("#1F1F1F");
                nameTextBlock.Foreground = Brush.Parse("#E0E0E0");
                if (authorTextBlock != null) authorTextBlock.Foreground = Brush.Parse("#E0E0E0");
                if (dependenciesText != null) dependenciesText.Foreground = Brush.Parse("#B0B0B0");
                if (groupText != null) groupText.Foreground = Brush.Parse("#FFFFFF");
                urlTextBlock.Foreground = Brush.Parse("#E0E0E0");
                break;
            case MonkeModManager.Theme.DarkHighContrast:
                border.Background = Brush.Parse("#000000");
                nameTextBlock.Foreground = Brush.Parse("#FFFFFF");
                if (authorTextBlock != null) authorTextBlock.Foreground = Brush.Parse("#FFFFFF");
                if (dependenciesText != null) dependenciesText.Foreground = Brush.Parse("#FFFFFF");
                if (groupText != null) groupText.Foreground = Brush.Parse("#000000");
                urlTextBlock.Foreground = Brush.Parse("#FFFFFF");
                break;
            case MonkeModManager.Theme.Sunrise:
                border.Background = Brush.Parse("#FFE0B2");
                nameTextBlock.Foreground = Brush.Parse("#4E342E");
                if (authorTextBlock != null) authorTextBlock.Foreground = Brush.Parse("#4E342E");
                if (dependenciesText != null) dependenciesText.Foreground = Brush.Parse("#6D4C41");
                if (groupText != null) groupText.Foreground = Brush.Parse("#FFFFFF");
                urlTextBlock.Foreground = Brush.Parse("#4E342E");
                break;
            case MonkeModManager.Theme.Frost:
                border.Background = Brush.Parse("#BBDEFB");
                nameTextBlock.Foreground = Brush.Parse("#0D47A1");
                if (authorTextBlock != null) authorTextBlock.Foreground = Brush.Parse("#0D47A1");
                if (dependenciesText != null) dependenciesText.Foreground = Brush.Parse("#546E7A");
                if (groupText != null) groupText.Foreground = Brush.Parse("#FFFFFF");
                urlTextBlock.Foreground = Brush.Parse("#0D47A1");
                break;
        }
        ModControls.Add(border);
        return border;
    }

    private IBrush GetGroupColor(string group)
    {
        switch (CurrentTheme)
        {
            case MonkeModManager.Theme.DarkHighContrast:
                return group?.ToLower() switch
                {
                    _ => Brush.Parse("#FFFFFF")
                };
            default:
                return group?.ToLower() switch
                {
                    "core" => Brushes.DarkBlue,
                    "libraries" => Brushes.Purple,
                    "gameplay" => Brushes.Green,
                    "cosmetic" => Brushes.Pink,
                    "utility" => Brushes.Orange,
                    _ => Brushes.LightGray
                    // im colour blind so hope I didnt do stupid things
                };
        }
    }
    private string ShortenUrl(string url)
    {
        if (string.IsNullOrEmpty(url) || url.Length <= 50)
            return url;
        
        return url.Substring(0, 47) + "...";
    }

    private bool IsModInstalled(Mod mod)
    {
        if (string.IsNullOrEmpty(pluginsPath) || !Directory.Exists(pluginsPath))
            return false;
            
        var downloadUrl = mod.DownloadUrl;
        if (string.IsNullOrEmpty(downloadUrl))
            return false;
            
        try
        {
            var uri = new Uri(downloadUrl);
            var fileName = Path.GetFileName(uri.LocalPath);
            
            if (string.IsNullOrEmpty(fileName))
                return false;

            var installLocation = !string.IsNullOrEmpty(mod.InstallLocation) 
                ? mod.InstallLocation 
                : "BepInEx/plugins";
            var targetDirectory = Path.Combine(gamePath, installLocation);

            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var trackingFile = Path.Combine(targetDirectory, $".{mod.Name}_installed.txt");
                return File.Exists(trackingFile);
            }
            else
            {
                var targetPath = Path.Combine(targetDirectory, fileName);
                if (File.Exists(targetPath))
                    return true;
                var files = Directory.GetFiles(targetDirectory, fileName, SearchOption.AllDirectories);
                return files.Length > 0;
            }
        }
        catch (Exception ex)
        {
            return false;
        }
    }
#endregion

    #region Installation
    private void ExtractZipToFolder(string zipPath, string targetDirectory)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var entryPath = entry.FullName.Replace('\\', '/');
            if (entryPath.Contains("../") || Path.IsPathRooted(entryPath))
                continue;

            var destinationPath = Path.Combine(targetDirectory, entry.FullName);
            
            var fullDestinationPath = Path.GetFullPath(destinationPath);
            var fullTargetPath = Path.GetFullPath(targetDirectory);
            
            if (!fullDestinationPath.StartsWith(fullTargetPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var directory = Path.GetDirectoryName(destinationPath);
            
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private async Task InstallMod(Mod mod, Button installButton, TextBlock statusText)
    {
        if (mod == null)
            throw new ArgumentNullException(nameof(mod));

        try
        {
            if (installButton != null)
            {
                installButton.IsEnabled = false;
                installButton.Content = "Installing...";
            }
            MessageBox0.Text = $"Installing {mod.Name}...";

            var installLocation = !string.IsNullOrEmpty(mod.InstallLocation) 
                ? mod.InstallLocation
                : "BepInEx/plugins";
            
            var targetDirectory = Path.Combine(gamePath, installLocation);
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            var downloadUrl = mod.DownloadUrl;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new InvalidOperationException("Mod download URL is empty");
            }

            var uri = new Uri(downloadUrl);
            var fileName = Path.GetFileName(uri.LocalPath);
            
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"{mod.Name}.dll";
            }

            var downloadPath = await DownloadFile(downloadUrl, fileName);

            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var installedFiles = new List<string>();
                
                using (var archive = ZipFile.OpenRead(downloadPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;
                        var entryPath = entry.FullName.Replace('\\', '/');
                        if (entryPath.Contains("../") || Path.IsPathRooted(entryPath))
                            continue;

                        var destinationPath = Path.Combine(targetDirectory, entry.FullName);
                        var fullDestinationPath = Path.GetFullPath(destinationPath);
                        var fullTargetPath = Path.GetFullPath(targetDirectory);
                        
                        if (!fullDestinationPath.StartsWith(fullTargetPath, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var directory = Path.GetDirectoryName(destinationPath);
                        
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        entry.ExtractToFile(destinationPath, overwrite: true);
                        var relativePath = Path.GetRelativePath(targetDirectory, destinationPath);
                        installedFiles.Add(relativePath);
                    }
                }
                
                var trackingFile = Path.Combine(targetDirectory, $".{mod.Name}_installed.txt");
                await File.WriteAllLinesAsync(trackingFile, installedFiles);
                
                if (File.Exists(downloadPath))
                {
                    File.Delete(downloadPath);
                }
            }
            else
            {
                var targetPath = Path.Combine(targetDirectory, fileName);
                
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                
                File.Move(downloadPath, targetPath);
            }

            if (statusText != null && installButton != null)
            {
                statusText.Text = "✓ Installed";
                statusText.Foreground = Brushes.Green;
                installButton.Content = "Reinstall";
                installButton.Background = Brushes.Orange;
            }

            MessageBox0.Text = $"Successfully installed {mod.Name} v{mod.Version}!";
        }
        catch (Exception ex)
        {
            await ShowErrorMessage($"Failed to install {mod.Name}: {ex.Message}");
            
            statusText.Text = "Installation failed";
            statusText.Foreground = Brushes.Red;
            installButton.Content = "Install";
            installButton.Background = Brushes.Green;
        }
        finally
        {
            installButton.IsEnabled = true;
        }
    }

    private async Task UninstallMod(Mod mod, Button installButton, Button uninstallButton, TextBlock statusText)
    {
        if (mod == null)
            throw new ArgumentNullException(nameof(mod));

        try
        {
            var installLocation = !string.IsNullOrEmpty(mod.InstallLocation) 
                ? mod.InstallLocation 
                : "BepInEx/plugins";
            
            var targetDirectory = Path.Combine(gamePath, installLocation);
            var fileDeleted = false;
            
            var downloadUrl = mod.DownloadUrl;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                MessageBox0.Text = $"Cannot uninstall {mod.Name}: No download URL available.";
                return;
            }

            var uri = new Uri(downloadUrl);
            var fileName = Path.GetFileName(uri.LocalPath);
            
            if (string.IsNullOrEmpty(fileName))
            {
                MessageBox0.Text = $"Cannot uninstall {mod.Name}: Cannot determine filename from URL.";
                return;
            }

            if (Directory.Exists(targetDirectory))
            {
                if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var trackingFile = Path.Combine(targetDirectory, $".{mod.Name}_installed.txt");
                    
                    if (File.Exists(trackingFile))
                    {
                        var installedFiles = await File.ReadAllLinesAsync(trackingFile);
                        
                        foreach (var relativePath in installedFiles)
                        {
                            var fullPath = Path.Combine(targetDirectory, relativePath);
                            if (File.Exists(fullPath))
                            {
                                try
                                {
                                    File.Delete(fullPath);
                                    fileDeleted = true;
                                }
                                catch
                                {
                                    // idk
                                }
                            }
                        }
                        
                        try
                        {
                            File.Delete(trackingFile);
                        }
                        catch
                        {
                            // another part where idk
                        }
                        
                        var directoriesToCheck = installedFiles
                            .Select(f => Path.GetDirectoryName(Path.Combine(targetDirectory, f)))
                            .Where(d => !string.IsNullOrEmpty(d))
                            .Distinct()
                            .OrderByDescending(d => d.Length);
                        
                        foreach (var dir in directoriesToCheck)
                        {
                            try
                            {
                                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                                {
                                    Directory.Delete(dir);
                                }
                            }
                            catch
                            {
                                // writing to make rider shut up
                            }
                        }
                    }
                    else
                    {
                        MessageBox0.Text = $"Cannot uninstall {mod.Name}: No installation tracking found. This mod may have been installed with an older version of the manager.";
                        return;
                    }
                }
                else
                {
                    var targetPath = Path.Combine(targetDirectory, fileName);
                    
                    if (File.Exists(targetPath))
                    {
                        try
                        {
                            File.Delete(targetPath);
                            fileDeleted = true;
                        }
                        catch
                        {
                            // rider
                        }
                    }
                    else
                    {
                        var files = Directory.GetFiles(targetDirectory, fileName, SearchOption.AllDirectories);
                        
                        foreach (var file in files)
                        {
                            try
                            {
                                File.Delete(file);
                                fileDeleted = true;
                                break;
                            }
                            catch
                            {
                                // gibb banan
                            }
                        }
                    }
                }
            }

            if (fileDeleted)
            {
                statusText.Text = "Not installed";
                statusText.Foreground = Brushes.Orange;
                installButton.Content = "Install";
                installButton.Background = Brushes.Green;

                if (uninstallButton.Parent is StackPanel parent)
                {
                    parent.Children.Remove(uninstallButton);
                }

                MessageBox0.Text = $"Successfully uninstalled {mod.Name}!";
            }
            else
            {
                MessageBox0.Text = $"Could not find {mod.Name} files to uninstall.";
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessage($"Could not uninstall {mod.Name}: {ex.Message}");
        }
    }

    private async Task<string> DownloadFile(string url, string fileName)
    {
        try
        {
            var fileBytes = await httpClient.GetByteArrayAsync(url);
            var downloadDir = Path.GetTempPath();
            Directory.CreateDirectory(downloadDir);
            
            var filePath = Path.Combine(downloadDir, fileName);
            await File.WriteAllBytesAsync(filePath, fileBytes);
            
            return filePath;
        }
        catch (Exception ex)
        {
            throw new Exception($"Download failed: {ex.Message}");
        }
    }

    

    private async Task<bool> CheckOrInstallBepInEx()
    {
        try
        {
            var bepInExPath = Path.Combine(gamePath, "BepInEx");
            
            if (!Directory.Exists(bepInExPath))
            {
                MessageBox0.Text = "BepInEx not found. Downloading...";
                
                var bytes = await httpClient.GetByteArrayAsync(
                    "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip");
                
                var tempPath = Path.Combine(Path.GetTempPath(), $"BepInEx_{Guid.NewGuid()}.zip");
                await File.WriteAllBytesAsync(tempPath, bytes);
                
                MessageBox0.Text = "Extracting BepInEx please wait!!!!!";
                await ExtractBepInEx(tempPath);
                
                File.Delete(tempPath);
                await fixBepInExConfig();
                MessageBox0.Text = "BepInEx installed successfully!";
                
                return true;
            }
            
            MessageBox0.Text = "BepInEx already installed.";
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorMessage($"couldnt install bepinex: {ex.Message}");
            return false;
        }
    }

    private async Task ExtractBepInEx(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var destinationPath = Path.Combine(gamePath, entry.FullName);
                var directory = Path.GetDirectoryName(destinationPath);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"couldnr extract {ex.Message}");
        }
    }
    private async Task InstallFromDisk(string modPath = null)
    {
        try
        {
            var targetDirectory = Path.Combine(gamePath, "BepInEx/plugins");
            if (modPath == null)
            {
                MessageBox0.Text = "Installing...";
                var filePickerOptions = new FilePickerOpenOptions()
                {
                    Title = "Select File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("DLL Files") { Patterns = new[] { "*.dll" } }
                    }
                };

                var files = await MainWin.StorageProvider.OpenFilePickerAsync(filePickerOptions);
        
                if (files == null || files.Count == 0)
                    return;

                var selectedFile = files[0];
                var fileName = selectedFile.Name;
                var sourcePath = selectedFile.Path.LocalPath;

                var targetPath = Path.Combine(targetDirectory, fileName);
                File.Copy(sourcePath, targetPath, overwrite: true);

                MessageBox0.Text = $"Successfully installed {fileName}!";
            }
            else
            {
                string output = Path.Combine(targetDirectory, $"Mod{DateTime.Now.ToString("HH:mm:ss")}");
                File.Copy(modPath, output, overwrite: true);

                MessageBox0.Text = $"Successfully installed to {output}!";
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessage($"Failed to install from disk: {ex.Message}");
        
            MessageBox0.Text = "Installation failed";
        }
    }

    private async Task SilentInstall(string url)
    {
        var file = await DownloadFile(url, "SilentInstall.dll");
        await InstallFromDisk(file);
    }
    private bool TryFindModByName(string modName, out Mod foundMod)
    {
        foundMod = Mods.FirstOrDefault(m => m.Name.Equals(modName, StringComparison.OrdinalIgnoreCase));
        return foundMod != null;
    }

    private bool TryFindBorderByMod(Mod mod, out Border foundBorder)
    {
        foundBorder = ModControls.FirstOrDefault(m => m.Tag.ToString().Equals(mod.Name, StringComparison.OrdinalIgnoreCase));
        return foundBorder != null;
    }
    #endregion
    
    #region Game Path Stuff
    private string GetGamePath()
    {
        try
        {
            if (!File.Exists(GetConfigPath()))
                return null;

            var json = File.ReadAllText(GetConfigPath());
            var config = JsonConvert.DeserializeObject<Config>(json);
            
            if (!string.IsNullOrWhiteSpace(config?.GamePath) && Directory.Exists(config.GamePath))
            {
                return config.GamePath;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading the config{ex.Message}");
        }
        
        return null;
    }

    private async Task<string> ShowGamePathDialog()
    {
        var dialog = new Window
        {
            Title = "Select Game Path",
            Width = 500,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var stackPanel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15
        };

        switch (CurrentTheme)
        {
            case MonkeModManager.Theme.Light:
                stackPanel.Background = Brushes.White;
                break;
            case MonkeModManager.Theme.Dark:
                stackPanel.Background = Brushes.DarkBlue;
                break;
        }

        stackPanel.Children.Add(new TextBlock
        {
            Text = "Select your game path:",
            FontSize = 14,
            FontWeight = FontWeight.Medium
        });

        var pathTextBox = new TextBox
        {
            IsReadOnly = true,
            Height = 32,
            Background = Brushes.LightGray
        };

        var browseButton = new Button
        {
            Content = "Browse",
            Width = 100,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Height = 32,
            IsEnabled = false
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 80,
            Height = 32
        };

        string selectedPath = null;

        browseButton.Click += async (sender, e) =>
        {
            var folderDialog = new OpenFolderDialog
            {
                Title = "Select Game Folder"
            };

            var result = await folderDialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(result))
            {
                pathTextBox.Text = result;
                selectedPath = result;
                okButton.IsEnabled = true;
            }
        };

        okButton.Click += (sender, e) =>
        {
            dialog.Close();
        };

        cancelButton.Click += (sender, e) =>
        {
            selectedPath = null;
            dialog.Close();
        };

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);

        stackPanel.Children.Add(pathTextBox);
        stackPanel.Children.Add(browseButton);
        stackPanel.Children.Add(buttonPanel);

        dialog.Content = stackPanel;

        await dialog.ShowDialog(this);
        return selectedPath;
    }
    #endregion
    
    #region Error Handling
    private async Task ShowErrorMessage(string message)
    {
        MessageBox0.Text = $"Error: {message}";
        Console.WriteLine($"Error: {message}");
        
        var errorDialog = new Window
        {
            Title = "an ERROR has errored your app",
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15
        };

        content.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        });

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        okButton.Click += (s, e) => errorDialog.Close();
        content.Children.Add(okButton);

        errorDialog.Content = content;
        await errorDialog.ShowDialog(this);
    }
    #endregion
    
    #region Version Checking Stuff

    private async Task NewVersionDialog(Version version)
    {
        var dialog = new Window
        {
            Title = "New Version Available!!!",
            Width = 500,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 15
        };

        dialog.Content = content;

        var text = new TextBlock
        {
            Text = "New Version Available",
            TextWrapping = TextWrapping.Wrap,
        };

        var installBtn = new Button
        {
            Content = "Install",
        };
        installBtn.Click += async (sender, e) =>
        {
            var url = "https://github.com/arielthemonke/MonkeModManager/releases/latest";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };

        var closeBtn = new Button
        {
            Content = "Close",
        };
        closeBtn.Click += async (sender, e) =>
        {
            dialog.Close();
        };
        content.Children.Add(installBtn);
        content.Children.Add(closeBtn);
        content.Children.Add(text);
        await dialog.ShowDialog(dialog);
    }
    #endregion
    
    #region cant think of name
    protected override void OnClosed(EventArgs e)
    {
        Console.WriteLine("[DiscordRPC] - Disposing.........");
        httpClient?.Dispose();
        client?.ClearPresence();
        client?.Deinitialize();
        client?.Dispose();
        Console.WriteLine("[DiscordRPC] - Disposed");
        base.OnClosed(e);
    }

    private void LaunchBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "steam://rungameid/1533390",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _ = ShowErrorMessage("Failed to launch game: " + ex.Message);
        }
    }

    private void InstallFromDiskBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = InstallFromDisk();
    }

    void OpenModsFolder_OnClick(object? sender, RoutedEventArgs e)
    {
        var ModsPath = Path.Combine(gamePath, "BepInEx", "plugins");
        OpenFolder(ModsPath);
    }

    void OpenGamePath_OnClick(object? sender, RoutedEventArgs e)
    {
        OpenFolder(gamePath);
    }

    void InstallAll(object? sender, RoutedEventArgs e)
    {
        foreach (var mod in Mods)
        {
            _ = SilentInstall(mod.URL);
            Console.WriteLine($"Installed {mod.Name} from {mod.URL}");
        }
    }

    void OpenFolder(string Path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo() { FileName = Path, UseShellExecute = true });
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", Path);
        }
    }
    #endregion
}

#region other classes/enums
public enum Theme
{
    Light,
    Dark,
    DarkHighContrast,
    Sunrise,
    Frost
}

public class Mod
{
    public string Name { get; set; }
    public string Author { get; set; }
    public string Version { get; set; }
    public List<string> Dependencies { get; set; } = new List<string>();
    public List<Mod> DependenciesAsMod { get; set; } = new List<Mod>();
    [JsonProperty("install_location")]
    public string InstallLocation { get; set; }
    [JsonProperty("git_path")]
    public string GitPath { get; set; }
    public string Group { get; set; }
    [JsonProperty("download_url")]
    public string DownloadUrl { get; set; }

    public string ModName => Name;
    public string URL => DownloadUrl;
    public string DisplayName => $"{Name} v{Version}";
    public string AuthorInfo => !string.IsNullOrEmpty(Author) ? $"by {Author}" : "";
}

public class Config
{
    public string GamePath { get; set; }
    public Theme Theme { get; set; }
}
#endregion