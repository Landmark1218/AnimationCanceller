using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;

namespace AnimCancelPatcher
{
    public partial class MainWindow : Window
    {
        private List<CharacterConfig> _characters = new List<CharacterConfig>();
        private readonly PatcherLogic _patcher;
        private string _pakPath = string.Empty;
        private string _exePath = string.Empty;
        private string _currentAesKey = string.Empty;
        private const int AppId = 1607250;
       
        private bool _isTestMode = false; //配布するときだけfalseにすることを忘れるな
        private int _dummyRetryCount = 0; //そのうちリトライ処理入れるかも
        private string _lastSelectedId = "";

        public MainWindow()
        {
            InitializeComponent();
            _patcher = new PatcherLogic();
            LoadConfigurations();
            ShowFirstLaunchMessageIfNeeded();

            this.Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializePathsAndFetchAesAsync();
        }

        private void ShowFirstLaunchMessageIfNeeded()
        {
            string flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".first_launch_done");
            bool isFirst = false;

            if (!File.Exists(flagPath))
            {
                isFirst = true;
                MessageBox.Show(
                    "If you got this tool from anywhere other than TheBingChilled server, you're gay.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                File.WriteAllText(flagPath, "done");
            }
        }

        private void ValidateSelections_WIP()
        {
        }

        private void LoadConfigurations()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "characters.json");
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                var parsed = JsonConvert.DeserializeObject<List<CharacterConfig>>(json);
                if (parsed != null)
                {
                    _characters.Clear();
                    foreach (var c in parsed)
                    {
                        _characters.Add(c);
                    }
                }
                CharacterListBox.ItemsSource = _characters;
                if (_characters.Count > 0)
                {
                    CharacterListBox.SelectedIndex = 0;
                }
            }
            else
            {
                MessageBox.Show("characters.json not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task InitializePathsAndFetchAesAsync()
        {
            PatchButton.IsEnabled = false;

            string gamePath = FindSteamGamePath(AppId);
            if (!string.IsNullOrEmpty(gamePath))
            {
                _pakPath = Path.Combine(gamePath, "HerovsGame", "Content", "Paks");

                string exeDir = Path.Combine(gamePath, "HerovsGame", "Binaries", "Win64");
                if (Directory.Exists(exeDir))
                {
                    string mhurExe = Path.Combine(exeDir, "MHUR.exe");
                    string shippingExe = Path.Combine(exeDir, "HerovsGame-Win64-Shipping.exe");

                    bool foundExe = false;

                    if (File.Exists(mhurExe))
                    {
                        _exePath = mhurExe;
                        foundExe = true;
                    }

                    if (foundExe == false && File.Exists(shippingExe))
                    {
                        _exePath = shippingExe;
                        foundExe = true;
                    }

                    if (foundExe == false)
                    {
                        var exes = Directory.GetFiles(exeDir, "*.exe");
                        if (exes.Length > 0)
                        {
                            _exePath = exes[0];
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(_pakPath) && Directory.Exists(_pakPath))
            {
                InstallPathDisplayText.Text = _pakPath;
                InstallPathDisplayText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 227, 161));
            }
            else
            {
                InstallPathDisplayText.Text = "Game installation path not found.";
                InstallPathDisplayText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 139, 168));
            }

            if (!string.IsNullOrEmpty(_exePath) && File.Exists(_exePath))
            {
                AesKeyDisplayText.Text = "Fetching AES key from game memory...";
                AesKeyDisplayText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255));

                _currentAesKey = await Task.Run(() => AesKeyFinder.FindKey(_exePath));

                if (!string.IsNullOrEmpty(_currentAesKey))
                {
                    AesKeyDisplayText.Text = _currentAesKey;
                    AesKeyDisplayText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 227, 161));
                    PatchButton.IsEnabled = true;
                }
                else
                {
                    AesKeyDisplayText.Text = "Failed to find AES Key from the executable.";
                    AesKeyDisplayText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 139, 168));
                }
            }
            else
            {
                AesKeyDisplayText.Text = "Game executable not found. Cannot fetch AES Key.";
                AesKeyDisplayText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 139, 168));
                MessageBox.Show("Could not find the game installation path automatically. Please check your Steam installation.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CharacterListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CharacterListBox.SelectedItem is CharacterConfig selectedConfig)
            {
                StageSlider.Maximum = selectedConfig.MaxComboStages;
                if (StageSlider.Value > selectedConfig.MaxComboStages)
                {
                    StageSlider.Value = selectedConfig.MaxComboStages;
                }
                _lastSelectedId = selectedConfig.CharacterId;
            }
        }

        private async void ExecutePatch_Click(object sender, RoutedEventArgs e)
        {
            if (CharacterListBox.SelectedItem is CharacterConfig selectedConfig)
            {
                int targetStage = (int)StageSlider.Value;
                string pakPath = _pakPath;
                string aesKey = _currentAesKey;
                bool forcePatch = false;

                if (string.IsNullOrEmpty(pakPath) || !Directory.Exists(pakPath))
                {
                    MessageBox.Show("Paks directory is not set or not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrEmpty(aesKey))
                {
                    MessageBox.Show("AES key was not successfully found. Cannot proceed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!selectedConfig.ComboAssets.TryGetValue(targetStage, out string? assetName) || string.IsNullOrEmpty(assetName))
                {
                    MessageBox.Show($"Asset name for stage {targetStage} is not defined.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                PatchButton.IsEnabled = false;

                try
                {
                    StatusText.Text = "Extracting from Pak...";
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(137, 180, 250));

                    var extractor = new PakExtractor();
                    await extractor.ExtractAssetAsync(pakPath, aesKey, selectedConfig.RelativeAssetDirectory, assetName);

                    StatusText.Text = "Applying Patch...";
                    string outputRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output");

                    await Task.Run(() =>
                    {
                        _patcher.ProcessComboCancel(selectedConfig, targetStage, outputRoot);
                    });

                    StatusText.Text = "Creating Pak file...";
                    string paksDirectory = _pakPath;
                    string pakOutputPath = await PackageModAsync(selectedConfig, targetStage, assetName, paksDirectory);

                    StatusText.Text = "Success!";
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 227, 161));

                    MessageBox.Show($"Patch completed for {selectedConfig.Name} stage {targetStage}!\n\nGenerated Pak:\n{pakOutputPath}",
                                    "Completed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    StatusText.Text = "Error!";
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 139, 168));
                    MessageBox.Show($"An error occurred during processing:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    PatchButton.IsEnabled = true;
                }
            }
        }

        private async Task<string> PackageModAsync(CharacterConfig config, int targetStage, string assetName, string paksDirectory)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string unrealPakDir = Path.Combine(baseDir, "Unrealpak");
            string pakBatPath = Path.Combine(unrealPakDir, "pak.bat");

            if (!File.Exists(pakBatPath))
            {
                throw new FileNotFoundException("Unrealpak/pak.bat が見つかりません。ツールと同じ階層に Unrealpak フォルダと pak.bat を配置してください。");
            }

            string pakNameBase = $"X-{config.Name}-{config.Element}{targetStage}_P";
            string stagingFolder = Path.Combine(unrealPakDir, pakNameBase);

            if (Directory.Exists(stagingFolder))
            {
                Directory.Delete(stagingFolder, true);
            }

            string relativeAssetPath = config.RelativeAssetDirectory.Replace('/', Path.DirectorySeparatorChar);
            string assetDestDir = Path.Combine(stagingFolder, relativeAssetPath);
            Directory.CreateDirectory(assetDestDir);

            string outputRoot = Path.Combine(baseDir, "Output");
            string assetSourceDir = Path.Combine(outputRoot, relativeAssetPath);
            string srcUasset = Path.Combine(assetSourceDir, assetName + ".uasset");
            string destUasset = Path.Combine(assetDestDir, assetName + ".uasset");
            if (File.Exists(srcUasset))
            {
                File.Copy(srcUasset, destUasset, true);
            }

            string srcUexp = Path.Combine(assetSourceDir, assetName + ".uexp");
            string destUexp = Path.Combine(assetDestDir, assetName + ".uexp");
            if (File.Exists(srcUexp))
            {
                File.Copy(srcUexp, destUexp, true);
            }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = pakBatPath,
                Arguments = $"\"{stagingFolder}\"",
                WorkingDirectory = unrealPakDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            await Task.Run(() =>
            {
                using (Process? process = Process.Start(psi))
                {
                    if (process == null) throw new Exception("pak.batの起動に失敗しました。");

                    process.StandardInput.WriteLine();
                    process.StandardInput.Flush();

                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        string error = process.StandardError.ReadToEnd();
                        throw new Exception($"UnrealPakの実行中にエラーが発生しました:\n{error}");
                    }
                }

                if (Directory.Exists(stagingFolder))
                {
                    Directory.Delete(stagingFolder, true);
                }
            });

            string generatedPakPath = Path.Combine(unrealPakDir, $"{pakNameBase}.pak");

            if (!string.IsNullOrWhiteSpace(paksDirectory))
            {
                string animationsDir = Path.Combine(paksDirectory, "~Animations");
                if (!Directory.Exists(animationsDir))
                {
                    Directory.CreateDirectory(animationsDir);
                }

                string finalPakPath = Path.Combine(animationsDir, $"{pakNameBase}.pak");
                File.Copy(generatedPakPath, finalPakPath, overwrite: true);
                File.Delete(generatedPakPath);

                return finalPakPath;
            }

            return generatedPakPath;
        }

        private string FindSteamGamePath(int appId)
        {
            string steamPath = GetSteamInstallPath();

            if (steamPath == null)
                return null;

            List<string> libraries = GetSteamLibraries(steamPath);

            foreach (string library in libraries)
            {
                string manifestPath = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");

                if (!File.Exists(manifestPath))
                    continue;

                string acfContent = File.ReadAllText(manifestPath);

                Match match = Regex.Match(
                    acfContent,
                    "\"installdir\"\\s+\"([^\"]+)\""
                );

                if (!match.Success)
                    continue;

                string installDir = match.Groups[1].Value;

                string gamePath = Path.Combine(
                    library,
                    "steamapps",
                    "common",
                    installDir
                );

                if (Directory.Exists(gamePath))
                    return gamePath;
            }

            return null;
        }

        private string GetSteamInstallPath()
        {
            string[] registryPaths =
            {
                @"SOFTWARE\\WOW6432Node\\Valve\\Steam",
                @"SOFTWARE\\Valve\\Steam"
            };

            foreach (string path in registryPaths)
            {
                using RegistryKey key = Registry.LocalMachine.OpenSubKey(path);

                string installPath = key?.GetValue("InstallPath")?.ToString();

                if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                {
                    return installPath;
                }
            }

            return null;
        }

        private List<string> GetSteamLibraries(string steamPath)
        {
            List<string> libraries = new()
            {
                steamPath
            };

            string vdfPath = Path.Combine(
                steamPath,
                "steamapps",
                "libraryfolders.vdf"
            );

            if (!File.Exists(vdfPath))
                return libraries;

            string content = File.ReadAllText(vdfPath);

            MatchCollection matches = Regex.Matches(
                content,
                "\"path\"\\s+\"([^\"]+)\""
            );

            foreach (Match match in matches)
            {
                string path = match.Groups[1].Value.Replace(@"\\", @"\");

                if (Directory.Exists(path))
                {
                    libraries.Add(path);
                }
            }

            return libraries;
        }
    }
}