using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace StmHex
{
    public partial class MainWindow : Window
    {
        private bool isConnected = false;
        private Process currentProcess;
        private string cubeProgrammerPath = "";
        private DateTime flashStartTime;
        private bool isFlashing = false;
        private List<string> stLinkList = new List<string>();
        private string selectedStLink = "";
        private bool isBusy = false;
        private bool isScanning = false;

        private const string RegistryPath = @"HKEY_CURRENT_USER\Software\STM32Flasher";

        public MainWindow()
        {
            InitializeComponent();
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            _ = InitializeAsync();
        }
        private async Task InitializeAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                TxtLoadingStatus.Text = "Загрузка конфигурации...";
                LoadConfig();
                await Task.Delay(500);

                // Сканируем только если путь указан
                if (!string.IsNullOrEmpty(cubeProgrammerPath) && File.Exists(cubeProgrammerPath))
                {
                    TxtLoadingStatus.Text = "Поиск ST-Link...";
                    await ScanStLinksInBackground();
                    await Task.Delay(500);

                    TxtLoadingStatus.Text = "Проверка устройств...";
                    await ScanStLinksInBackground();
                }
                else
                {
                    TxtLoadingStatus.Text = "STM32CubeProgrammer не найден";
                    await Task.Delay(500);
                }

                TxtLoadingStatus.Text = "Готово!";
                await Task.Delay(300);
                LoadingOverlay.Visibility = Visibility.Collapsed;

                if (!string.IsNullOrEmpty(cubeProgrammerPath) && File.Exists(cubeProgrammerPath))
                {
                    LogMessage("✅ Программа готова к работе", Brushes.Green);
                }
                else
                {
                    LogMessage("⚠️ Укажите путь к STM32CubeProgrammer через кнопку ⚙", Brushes.Orange);
                }
            }
            catch (Exception ex)
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                LogMessage($"⚠️ Ошибка инициализации: {ex.Message}", Brushes.Orange);
            }
        }
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (BtnFlash.IsEnabled && !isFlashing)
                {
                    BtnFlash_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
        }


        private async Task ScanStLinksInBackground()
        {
            if (isScanning) return;
            isScanning = true;

            try
            {
                var result = await Task.Run(() => RunCubeProgrammerSync("-l ST-LINK"));

                await Dispatcher.InvokeAsync(() =>
                {
                    var newList = new List<string>();

                    string[] lines = result.Output.Split('\n');
                    foreach (string line in lines)
                    {
                        if (line.Contains("ST-LINK SN"))
                        {
                            int idx = line.IndexOf(":");
                            if (idx >= 0)
                            {
                                string sn = line.Substring(idx + 1).Trim();
                                if (!newList.Contains(sn))
                                    newList.Add(sn);
                            }
                        }
                    }

                    // Проверяем изменился ли список
                    bool changed = newList.Count != stLinkList.Count;
                    if (!changed)
                    {
                        foreach (string sn in newList)
                        {
                            if (!stLinkList.Contains(sn))
                            {
                                changed = true;
                                break;
                            }
                        }
                    }

                    if (changed)
                    {
                        stLinkList = newList;
                        UpdateStLinkComboBox();
                    }
                });
            }
            catch
            {
                // Игнорируем ошибки
            }
            finally
            {
                isScanning = false;
            }
        }

        private (int ExitCode, string Output) RunCubeProgrammerSync(string arguments)
        {
            // Проверка на пустой путь
            if (string.IsNullOrEmpty(cubeProgrammerPath) || !File.Exists(cubeProgrammerPath))
            {
                return (-1, "STM32CubeProgrammer не найден");
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cubeProgrammerPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit(5000);
                return (process.ExitCode, output);
            }
            catch
            {
                return (-1, "");
            }
        }

        // ==================== ВЫБОР ST-LINK ====================

        private void UpdateStLinkComboBox()
        {
            string currentSelection = CmbStLink.SelectedItem?.ToString();

            CmbStLink.Items.Clear();

            foreach (string sn in stLinkList)
            {
                string displaySn = sn.Length > 10 ? sn.Substring(sn.Length - 10) : sn;
                CmbStLink.Items.Add(displaySn);
            }

            if (CmbStLink.Items.Count > 0)
            {
                if (!string.IsNullOrEmpty(currentSelection))
                {
                    int idx = CmbStLink.Items.IndexOf(currentSelection);
                    if (idx >= 0)
                        CmbStLink.SelectedIndex = idx;
                    else
                        CmbStLink.SelectedIndex = 0;
                }
                else if (!string.IsNullOrEmpty(selectedStLink))
                {
                    string shortSelected = selectedStLink.Length > 10
                        ? selectedStLink.Substring(selectedStLink.Length - 10)
                        : selectedStLink;
                    int idx = CmbStLink.Items.IndexOf(shortSelected);
                    if (idx >= 0)
                        CmbStLink.SelectedIndex = idx;
                    else
                        CmbStLink.SelectedIndex = 0;
                }
                else
                {
                    CmbStLink.SelectedIndex = 0;
                    if (stLinkList.Count > 0)
                        selectedStLink = stLinkList[0];
                }
            }
        }

        private async void CmbStLink_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbStLink.SelectedItem == null) return;
            if (stLinkList.Count == 0) return;

            int selectedIndex = CmbStLink.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= stLinkList.Count) return;

            string newStLink = stLinkList[selectedIndex];

            if (newStLink == selectedStLink) return;

            selectedStLink = newStLink;
            LogMessage($"Выбран ST-Link: {selectedStLink}", Brushes.Green);

            if (isConnected)
            {
                await ReconnectToSTM32();
            }
        }

        private async Task ReconnectToSTM32()
        {
            try
            {
                isBusy = true;
                BtnConnect.IsEnabled = false;
                CmbStLink.IsEnabled = false;
                BtnFlash.IsEnabled = false;
                BtnErase.IsEnabled = false;

                LogMessage($"Переподключение к {selectedStLink}...", Brushes.Blue);

                isConnected = false;

                string args = $"-c port=SWD mode=UR sn={selectedStLink}";
                var result = await RunCubeProgrammerAsync(args);

                if (result.ExitCode == 0)
                {
                    isConnected = true;
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                    TxtStatus.Text = "Подключено";
                    BtnConnect.Content = "Отключить";
                    BtnFlash.IsEnabled = !string.IsNullOrEmpty(TxtHexPath.Text);
                    BtnErase.IsEnabled = true;

                    LogMessage($"✅ Переподключено к {selectedStLink}", Brushes.Green);
                }
                else
                {
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
                    TxtStatus.Text = "Не подключено";
                    BtnConnect.Content = "Подключить";

                    LogMessage("❌ Не удалось переподключиться", Brushes.Red);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Ошибка: {ex.Message}", Brushes.Red);
            }
            finally
            {
                isBusy = false;
                BtnConnect.IsEnabled = true;
                CmbStLink.IsEnabled = true;
            }
        }

        // ==================== КОНФИГУРАЦИЯ ====================

        private void LoadConfig()
        {
            try
            {
                cubeProgrammerPath = (string)Registry.GetValue(RegistryPath, "CubeProgrammerPath", "");

                if (!string.IsNullOrEmpty(cubeProgrammerPath) && File.Exists(cubeProgrammerPath))
                {
                    LogMessage($"STM32CubeProgrammer найден: {cubeProgrammerPath}", Brushes.Green);
                }
                else if (!string.IsNullOrEmpty(cubeProgrammerPath))
                {
                    LogMessage("⚠️ Сохраненный путь недействителен", Brushes.Orange);
                    cubeProgrammerPath = "";
                }
                else
                {
                    LogMessage("ℹ️ Укажите путь к STM32CubeProgrammer через кнопку ⚙", Brushes.Gray);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Ошибка чтения реестра: {ex.Message}", Brushes.Orange);
                cubeProgrammerPath = "";
            }

            BtnConnect.IsEnabled = true;
            BtnBrowse.IsEnabled = true;
            BtnChangePath.Visibility = Visibility.Visible;
            BtnFlash.IsEnabled = false;
            BtnErase.IsEnabled = false;
            CmbStLink.IsEnabled = true;
        }

        private void SavePathToRegistry(string path)
        {
            try
            {
                Registry.SetValue(RegistryPath, "CubeProgrammerPath", path);
                LogMessage("✅ Путь сохранен в реестре", Brushes.Green);
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Не удалось сохранить: {ex.Message}", Brushes.Orange);
            }
        }

        private void ShowPathSelectionDialog()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "STM32_Programmer_CLI.exe|STM32_Programmer_CLI.exe|Все файлы|*.*";
            dialog.Title = "Укажите путь к STM32_Programmer_CLI.exe";
            dialog.FileName = "STM32_Programmer_CLI.exe";

            if (dialog.ShowDialog() == true)
            {
                if (File.Exists(dialog.FileName))
                {
                    cubeProgrammerPath = dialog.FileName;
                    SavePathToRegistry(cubeProgrammerPath);
                    LogMessage("STM32CubeProgrammer готов к работе", Brushes.Green);
                }
                else
                {
                    MessageBox.Show("Выбранный файл не существует!", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnChangePath_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(cubeProgrammerPath))
            {
                var result = MessageBox.Show(
                    "Сменить путь к STM32CubeProgrammer CLI?\n\n" +
                    $"Текущий: {cubeProgrammerPath}",
                    "Смена пути",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            ShowPathSelectionDialog();
        }

        // ==================== ПОДКЛЮЧЕНИЕ ====================

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (isBusy) return;

            if (!isConnected)
            {
                if (string.IsNullOrEmpty(cubeProgrammerPath) || !File.Exists(cubeProgrammerPath))
                {
                    MessageBox.Show(
                        "STM32CubeProgrammer CLI не найден!\n\nНажмите кнопку ⚙ чтобы указать путь",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await ConnectToSTM32();
            }
            else
            {
                DisconnectFromSTM32();
            }
        }

        private async Task ConnectToSTM32(bool ignoreFlashButton = false)
        {
            try
            {
                isBusy = true;
                BtnConnect.IsEnabled = false;
                CmbStLink.IsEnabled = false;
                LogMessage("Поиск ST-Link...", Brushes.Black);

                string args = "-c port=SWD mode=UR";
                if (!string.IsNullOrEmpty(selectedStLink))
                    args += $" sn={selectedStLink}";

                var result = await RunCubeProgrammerAsync(args);

                if (result.ExitCode == 0)
                {
                    isConnected = true;
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                    TxtStatus.Text = "Подключено";
                    BtnConnect.Content = "Отключить";

                    if (!isFlashing && !ignoreFlashButton)
                    {
                        BtnFlash.IsEnabled = !string.IsNullOrEmpty(TxtHexPath.Text);
                        BtnErase.IsEnabled = true;
                    }

                    LogMessage("✅ Подключение выполнено", Brushes.Green);
                }
                else
                {
                    isConnected = false;
                    LogMessage("❌ Не удалось подключиться", Brushes.Red);
                    MessageBox.Show($"Ошибка подключения:\n{result.Output}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Ошибка: {ex.Message}", Brushes.Red);
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isBusy = false;
                BtnConnect.IsEnabled = !isFlashing;
                CmbStLink.IsEnabled = !isFlashing;
            }
        }

        private void DisconnectFromSTM32()
        {
            isConnected = false;
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
            TxtStatus.Text = "Не подключено";
            BtnConnect.Content = "Подключить";
            BtnFlash.IsEnabled = false;
            BtnErase.IsEnabled = false;
            LogMessage("Отключено от ST-Link", Brushes.Orange);
        }

        private async Task<bool> CheckConnection()
        {
            try
            {
                string args = "-c port=SWD mode=UR";
                if (!string.IsNullOrEmpty(selectedStLink))
                    args += $" sn={selectedStLink}";

                var result = await RunCubeProgrammerAsync(args);
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        // ==================== ВЫБОР ФАЙЛА ====================

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "HEX files (*.hex)|*.hex|All files (*.*)|*.*";
            dialog.Title = "Выберите HEX файл прошивки";

            if (dialog.ShowDialog() == true)
            {
                TxtHexPath.Text = dialog.FileName;
                LogMessage($"Выбран файл: {Path.GetFileName(dialog.FileName)}", Brushes.Black);
                if (isConnected) BtnFlash.IsEnabled = true;
            }
        }

        // ==================== ПРОШИВКА ====================

        private async void BtnFlash_Click(object sender, RoutedEventArgs e)
        {
            if (isFlashing || isBusy) return;

            if (string.IsNullOrEmpty(TxtHexPath.Text))
            {
                MessageBox.Show("Выберите HEX файл!", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "Начать прошивку?\n\nБудут выполнены все операции.",
                "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            await FlashCompleteAsync();
        }

        private async Task FlashCompleteAsync()
        {
            try
            {
                isFlashing = true;
                SetUIEnabled(false);

                LogMessage("Проверка подключения...", Brushes.Black);
                bool connected = await CheckConnection();
                if (!connected)
                    throw new Exception("Микроконтроллер не подключен!\nПроверьте соединение ST-Link.");

                ClearOldLogsIfNeeded();

                LogMessage("═══════════════════════════", Brushes.Blue);
                LogMessage("🔧 НАЧАЛО ПРОШИВКИ", Brushes.Blue);
                LogMessage("═══════════════════════════", Brushes.Blue);

                string hexPath = TxtHexPath.Text;
                flashStartTime = DateTime.Now;

                UpdateProgress(5, "Проверка защиты", "Проверка RDP1...");
                bool rdp1Set = await CheckRDP1();

                if (rdp1Set)
                {
                    UpdateProgress(10, "Сброс защиты", "Сброс RDP1...");
                    await ResetRDP1();
                    await Task.Delay(1000);
                    await ConnectToSTM32(true);
                }

                UpdateProgress(20, "Стирание", "Стирание...");
                await EraseFlash();

                UpdateProgress(30, "Прошивка", "Запись...");
                await FlashHex(hexPath);

                UpdateProgress(70, "Верификация", "Проверка...");
                await VerifyFlash(hexPath);

                UpdateProgress(90, "Защита", "Установка RDP1...");
                await SetRDP1();

                await Task.Delay(1000);
                UpdateProgress(95, "Проверка", "Проверка RDP1...");
                bool rdp1Verified = await CheckRDP1();

                if (!rdp1Verified)
                    throw new Exception("RDP1 не установлен!");

                TimeSpan duration = DateTime.Now - flashStartTime;
                UpdateProgress(100, "Готово", "Успешно!");
                LogMessage("✅ Прошивка завершена!", Brushes.Green);
                LogMessage($"⏱ Время: {duration.TotalSeconds:F1} сек", Brushes.Blue);

                MessageBox.Show($"Успешно!\nВремя: {duration.TotalSeconds:F1} сек", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Ошибка: {ex.Message}", Brushes.Red);
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isFlashing = false;
                SetUIEnabled(true);
            }
        }

        // ==================== ОЧИСТКА ====================

        private async void BtnErase_Click(object sender, RoutedEventArgs e)
        {
            if (isFlashing || isBusy) return;

            var result = MessageBox.Show(
                "Очистить микроконтроллер?\n\n" +
                "Будет снята защита RDP1 (если есть)\n" +
                "и стерта вся память.",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            await EraseCompleteAsync();
        }

        private async Task EraseCompleteAsync()
        {
            try
            {
                isFlashing = true;
                SetUIEnabled(false);

                LogMessage("Проверка подключения...", Brushes.Black);
                bool connected = await CheckConnection();
                if (!connected)
                    throw new Exception("Микроконтроллер не подключен!\nПроверьте соединение ST-Link.");

                UpdateProgress(10, "Проверка защиты", "Проверка RDP1...");
                bool rdp1Set = await CheckRDP1();

                if (rdp1Set)
                {
                    UpdateProgress(30, "Сброс защиты", "Сброс RDP1...");
                    await ResetRDP1();
                    await Task.Delay(1000);
                    await ConnectToSTM32(true);
                }

                UpdateProgress(60, "Очистка", "Стирание памяти...");
                await EraseFlash();

                UpdateProgress(100, "Готово", "Микроконтроллер очищен!");
                LogMessage("✅ Микроконтроллер очищен", Brushes.Green);

                MessageBox.Show("Микроконтроллер очищен!", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Ошибка: {ex.Message}", Brushes.Red);
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isFlashing = false;
                SetUIEnabled(true);
            }
        }

        // ==================== КОМАНДЫ ====================

        private async Task<bool> CheckRDP1()
        {
            var result = await RunCubeProgrammerAsync(GetConnectionArgs("-ob displ"));
            if (result.ExitCode != 0) throw new Exception(result.Output);

            bool rdp1Set = result.Output.Contains("0xBB") ||
                          result.Output.Contains("Level 1") ||
                          result.Output.Contains("read protection");

            LogMessage(rdp1Set ? "RDP1 установлен" : "RDP1 не установлен",
                rdp1Set ? Brushes.Orange : Brushes.Green);
            return rdp1Set;
        }

        private string GetConnectionArgs(string command)
        {
            string args = "-c port=SWD mode=UR";
            if (!string.IsNullOrEmpty(selectedStLink))
                args += $" sn={selectedStLink}";
            args += $" {command}";
            return args;
        }

        private async Task ResetRDP1() => await RunCommand("-ob RDP=0xAA", "RDP1 сброшен");
        private async Task EraseFlash() => await RunCommand("-e all", "Память стерта");
        private async Task FlashHex(string hex) => await RunCommand($"-w \"{hex}\" -v", "Прошивка записана");
        private async Task VerifyFlash(string hex) => await RunCommand($"-w \"{hex}\" -v", "Верификация успешна");
        private async Task SetRDP1() => await RunCommand("-ob RDP=0xBB", "RDP1 установлен");

        private async Task RunCommand(string args, string successMsg)
        {
            var result = await RunCubeProgrammerAsync(GetConnectionArgs(args));
            if (result.ExitCode != 0) throw new Exception(result.Output);
            LogMessage(successMsg, Brushes.Green);
        }

        private async Task<(int ExitCode, string Output)> RunCubeProgrammerAsync(string arguments)
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cubeProgrammerPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                Dispatcher.Invoke(() => LogMessage(output, Brushes.Gray));
                return (process.ExitCode, output);
            });
        }

        private void UpdateProgress(int percent, string stage, string message)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = percent;
                TxtStage.Text = stage;
                TxtPercent.Text = $"{percent}%";
                LogMessage(message, Brushes.Black);
            });
        }

        private void SetUIEnabled(bool enabled)
        {
            if (isFlashing)
            {
                BtnFlash.IsEnabled = false;
                BtnErase.IsEnabled = false;
                BtnBrowse.IsEnabled = false;
                TxtHexPath.IsEnabled = false;
                BtnConnect.IsEnabled = false;
                CmbStLink.IsEnabled = false;
                return;
            }

            BtnFlash.IsEnabled = enabled && isConnected && !string.IsNullOrEmpty(TxtHexPath.Text);
            BtnErase.IsEnabled = enabled && isConnected;
            BtnBrowse.IsEnabled = enabled;
            TxtHexPath.IsEnabled = enabled;
            BtnConnect.IsEnabled = enabled && !isBusy;
            CmbStLink.IsEnabled = enabled && !isBusy;
        }

        private void TxtHexPath_PreviewDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void TxtHexPath_Drop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0 && files[0].EndsWith(".hex", StringComparison.OrdinalIgnoreCase))
            {
                TxtHexPath.Text = files[0];
                if (isConnected) BtnFlash.IsEnabled = true;
            }
        }

        private void LogMessage(string message, Brush color)
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLog.ScrollToEnd();
        }

        private void ClearOldLogsIfNeeded()
        {
            string[] lines = TxtLog.Text.Split('\n');
            int flashCount = 0;
            foreach (string line in lines)
                if (line.Contains("НАЧАЛО ПРОШИВКИ")) flashCount++;

            if (flashCount >= 10)
            {
                int found = 0;
                int lastStart = 0;
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (lines[i].Contains("НАЧАЛО ПРОШИВКИ"))
                    {
                        found++;
                        if (found == 10) { lastStart = i; break; }
                    }
                }
                if (lastStart > 0)
                {
                    TxtLog.Text = string.Join("\n", lines.Skip(lastStart));
                    TxtLog.AppendText("\n[--- Очищено ---]\n\n");
                }
            }
        }
    }
}