using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Interop;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Reflection.Metadata;
using Birdy_Browser;
using System.Diagnostics;
using System.Reflection;
using System.Xml;
using IWshRuntimeLibrary;
using System.Windows.Threading;
//using System.Windows.Forms;
//using System.Drawing; // For Icon

namespace Birdy_Fences
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application


    {

        public static bool IsSnapEnabled { get; set; } = true;
        public static int TintValue { get; set; } = 60;
        public static string SelectedColor { get; set; } = "Gray";
        public static bool IsLogEnabled { get; set; } = false;


        private void LoadSettings()
        {
            string optionsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "options.json");
            if (System.IO.File.Exists(optionsFilePath))
            {
                string jsonContent = System.IO.File.ReadAllText(optionsFilePath);
                dynamic optionsData = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonContent);

                IsSnapEnabled = optionsData.IsSnapEnabled;
                TintValue = optionsData.TintValue;
                SelectedColor = optionsData.SelectedColor;
                IsLogEnabled = optionsData.IsLogEnabled;
            }
            else
            {
                SaveSettings(); // Create the file with default values if it doesn't exist
            }
        }

        private void SaveSettings()
        {
            string optionsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "options.json");
            var optionsData = new
            {
                IsSnapEnabled,
                TintValue,
                SelectedColor,
                IsLogEnabled
            };
            string formattedJson = Newtonsoft.Json.JsonConvert.SerializeObject(optionsData, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(optionsFilePath, formattedJson);
        }



        private DispatcherTimer _snapTimer;
        private NonActivatingWindow _currentDraggingFence;

        // Add a timer refference
        private TargetChecker _targetChecker;


        // Global variable to control snapping
      //  public static bool IsSnapEnabled { get; set; } = true;

        // Add a variable to control fence snapping
        public static int snapFences { get; set; } = 1;

        // Constants for snapping
      
        private const double SnapDistance = 20; // Distance to snap to (in pixels)
        private const double SnapThreshold = 30;       // 30px proximity for external snaps
        private const double InternalSnapThreshold = 15; // 15px for internal adjustments
        private const double MinGap = 8;              // 8px minimum between fences

        private System.Windows.Forms.NotifyIcon _trayIcon;
                private System.Windows.Forms.NotifyIcon trayIcon;

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpWindowClass, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        const int GWL_HWNDPARENT = -8;

        IntPtr hprog = FindWindowEx(
            FindWindowEx(
                FindWindow("Progman", "Program Manager"),
                IntPtr.Zero, "SHELLDLL_DefView", ""
            ),
            IntPtr.Zero, "SysListView32", "FolderView"
        );


        private void SaveFencesData(dynamic fencedata, string jsonFilePath)
        {
            string formattedJson = Newtonsoft.Json.JsonConvert.SerializeObject(fencedata, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(jsonFilePath, formattedJson);
        }

        private System.Drawing.Image LoadImageFromResources(string resourcePath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourcePath))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException($"Resource '{resourcePath}' not found.");
                }
                return System.Drawing.Image.FromStream(stream);
            }
        }

        private bool IsExecutableFile(string filePath)
        {
            // List of executable file extensions
            string[] executableExtensions = { ".exe", ".bat", ".cmd", ".vbs", ".ps1", ".hta" };

            // Resolve the target path if the file is a shortcut
            if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk")
            {
                try
                {
                    WshShell shell = new WshShell();
                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                    filePath = shortcut.TargetPath; // Get the target path of the shortcut
                }
                catch
                {
                    return false; // Error reading the shortcut
                }
            }

            // Get the file extension
            string extension = System.IO.Path.GetExtension(filePath).ToLower();

            // Check if the extension is in the list
            return executableExtensions.Contains(extension);
        }
        private string GetShortcutTarget(string filePath)
        {
            if (System.IO.Path.GetExtension(filePath).ToLower() == ".lnk")
            {
                // Handle shortcuts
                try
                {
                    WshShell shell = new WshShell();
                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(filePath);
                    return shortcut.TargetPath; // Return the target path of the shortcut
                }
                catch
                {
                    return null; // Error reading the shortcut
                }
            }
            else
            {
                // Handle regular files
                return filePath; // Return the file's path directly
            }
        }

        private void LogSnapDetails(string message)
        {
            if (!IsLogEnabled) return; // Check if logging is enabled

            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "snap_log.txt");
            using (StreamWriter writer = new StreamWriter(logFilePath, true))
            {
                writer.WriteLine($"{DateTime.Now}: {message}");
            }
        }


        private (double, double) SnapToClosestFence(NonActivatingWindow currentFence, List<NonActivatingWindow> allFences)
        {
            if (!IsSnapEnabled || snapFences == 0) return (currentFence.Left, currentFence.Top); // Snapping is disabled

            double initialX = currentFence.Left;
            double initialY = currentFence.Top;
            double snapX = initialX;
            double snapY = initialY;

            List<string> nearbyFences = new List<string>();
            List<string> causingSnapFences = new List<string>();

            foreach (var fence in allFences)
            {
                if (fence == currentFence) continue;

                // Calculate the edges of the current fence
                double currentLeft = currentFence.Left;
                double currentRight = currentFence.Left + currentFence.Width;
                double currentTop = currentFence.Top;
                double currentBottom = currentFence.Top + currentFence.Height;

                // Calculate the edges of the other fence
                double otherLeft = fence.Left;
                double otherRight = fence.Left + fence.Width;
                double otherTop = fence.Top;
                double otherBottom = fence.Top + fence.Height;

                // Determine the appropriate snap threshold
                double horizontalThreshold = (currentLeft >= otherLeft && currentRight <= otherRight) ? InternalSnapThreshold : SnapThreshold;
                double verticalThreshold = (currentTop >= otherTop && currentBottom <= otherBottom) ? InternalSnapThreshold : SnapThreshold;

                // Check for snapping on the left/right edges
                if (Math.Abs(currentRight - otherLeft) <= horizontalThreshold)
                {
                    snapX = otherLeft - currentFence.Width - MinGap;
                    causingSnapFences.Add(fence.Title);
                }
                else if (Math.Abs(currentLeft - otherRight) <= horizontalThreshold)
                {
                    snapX = otherRight + MinGap;
                    causingSnapFences.Add(fence.Title);
                }

                // Check for snapping on the top/bottom edges
                if (Math.Abs(currentBottom - otherTop) <= verticalThreshold)
                {
                    snapY = otherTop - currentFence.Height - MinGap;
                    causingSnapFences.Add(fence.Title);
                }
                else if (Math.Abs(currentTop - otherBottom) <= verticalThreshold)
                {
                    snapY = otherBottom + MinGap;
                    causingSnapFences.Add(fence.Title);
                }

                nearbyFences.Add(fence.Title);
            }

            // Ensure the new position is within valid screen coordinates
            snapX = Math.Max(0, snapX);
            snapY = Math.Max(0, snapY);

            // Apply the snapping
            currentFence.Left = snapX;
            currentFence.Top = snapY;

            // Log the details
            string logMessage = $"FenceDragged: {currentFence.Title}, NearbyFences: {string.Join(", ", nearbyFences)}, " +
                                $"CausingSnapFences: {string.Join(", ", causingSnapFences)}, " +
                                $"FenceDraggedPosition: ({initialX}, {initialY}), " +
                                $"FenceSnappedPosition: ({snapX}, {snapY}), " +
                                $"ScreenSize: ({SystemParameters.PrimaryScreenWidth}, {SystemParameters.PrimaryScreenHeight})";
            LogSnapDetails(logMessage);

            return (snapX, snapY);
        }

        private void ApplyTintAndColorToFence(NonActivatingWindow fence)
        {
            if (fence.Content is Border border)
            {
                Color baseColor = GetColorFromName(SelectedColor);
                byte alpha = (byte)(TintValue * 2.55);
                border.Background = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            }
        }

        private Color GetColorFromName(string colorName)
        {
            return colorName switch
            {
                "Red" => (Color)ColorConverter.ConvertFromString("#c10338"), // Monza
                "Green" => (Color)ColorConverter.ConvertFromString("#005618"), // Camarone
                "Blue" => (Color)ColorConverter.ConvertFromString("#012162"), // Midnight Blue
                "White" => (Color)ColorConverter.ConvertFromString("#fdfdff"), // Titan White
                "Gray" => (Color)ColorConverter.ConvertFromString("#3d3d3f"), // Ship Gray
                "Black" => (Color)ColorConverter.ConvertFromString("#0b0b0c"), // Woodsmoke
                "Purple" => (Color)ColorConverter.ConvertFromString("#3a0b50"), // Jagger
                "Yellow" => (Color)ColorConverter.ConvertFromString("#d8da1f"), // Sunflower
                _ => Colors.Transparent,
            };
        }

        private void ApplyTintToFence(NonActivatingWindow fence)
        {
            if (fence.Content is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromArgb((byte)(TintValue * 2.55), 0, 0, 0));
            }
        }

        private void ShowOptionsForm()
        {
            try
            {
                using (var frmOptions = new System.Windows.Forms.Form())
                {
                    // Set up the form
                    frmOptions.Text = "Options";
                    frmOptions.Size = new System.Drawing.Size(260, 340); // Adjust size
                    frmOptions.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
                    frmOptions.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
                    frmOptions.MaximizeBox = false;
                    frmOptions.MinimizeBox = false;

                    // Set the form icon to match the tray icon
                    string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                    frmOptions.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);

                    // Create a TableLayoutPanel for central alignment
                    var layoutPanel = new System.Windows.Forms.TableLayoutPanel
                    {
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        ColumnCount = 1,
                        AutoSize = true,
                        AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                        Padding = new System.Windows.Forms.Padding(10) // Add padding
                    };
                    layoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));

                    // GroupBox for Selections
                    var groupBoxSelections = new System.Windows.Forms.GroupBox
                    {
                        Text = "Selections",
                        Dock = System.Windows.Forms.DockStyle.Top,
                        AutoSize = true,
                        AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
                    };
                    var selectionsLayout = new System.Windows.Forms.TableLayoutPanel
                    {
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        ColumnCount = 2,
                        AutoSize = true,
                        AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
                    };
                    selectionsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
                    selectionsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));

                    // Enable snap function checkbox
                    var chkEnableSnap = new System.Windows.Forms.CheckBox
                    {
                        Text = "Enable snap function",
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        AutoSize = true,
                        Checked = IsSnapEnabled // Load setting
                    };
                    selectionsLayout.Controls.Add(chkEnableSnap, 0, 0);
                    selectionsLayout.SetColumnSpan(chkEnableSnap, 2);

                    // Tint label and numeric up down
                    var lblTint = new System.Windows.Forms.Label
                    {
                        Text = "Tint",
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        AutoSize = true
                    };
                    var numTint = new System.Windows.Forms.NumericUpDown
                    {
                        Maximum = 100,
                        Minimum = 1,
                        Value = TintValue, // Load setting
                        Dock = System.Windows.Forms.DockStyle.Fill
                    };
                    selectionsLayout.Controls.Add(lblTint, 0, 1);
                    selectionsLayout.Controls.Add(numTint, 1, 1);

                    // Color label and dropdown menu
                    var lblColor = new System.Windows.Forms.Label
                    {
                        Text = "Color",
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        AutoSize = true
                    };
                    var cmbColor = new System.Windows.Forms.ComboBox
                    {
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
                    };
                    cmbColor.Items.AddRange(new string[] { "Gray", "Black", "White", "Green", "Purple", "Yellow", "Red", "Blue" });
                    cmbColor.SelectedItem = SelectedColor; // Load setting
                    selectionsLayout.Controls.Add(lblColor, 0, 2);
                    selectionsLayout.Controls.Add(cmbColor, 1, 2);

                    groupBoxSelections.Controls.Add(selectionsLayout);
                    layoutPanel.Controls.Add(groupBoxSelections);

                    // GroupBox for Tools
                    var groupBoxTools = new System.Windows.Forms.GroupBox
                    {
                        Text = "Tools",
                        Dock = System.Windows.Forms.DockStyle.Top,
                        AutoSize = true,
                        AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
                    };
                    var toolsLayout = new System.Windows.Forms.TableLayoutPanel
                    {
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        ColumnCount = 1,
                        AutoSize = true,
                        AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
                    };
                    toolsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));

                    // Enable log checkbox
                    var chkEnableLog = new System.Windows.Forms.CheckBox
                    {
                        Text = "Enable log",
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        AutoSize = true,
                        Checked = IsLogEnabled // Load setting
                    };
                    toolsLayout.Controls.Add(chkEnableLog, 0, 0);

                    // Backup button
                    var btnBackup = new System.Windows.Forms.Button
                    {
                        Text = "Backup",
                        AutoSize = true,
                        Width = 80,
                        Height = 30,
                        Anchor = System.Windows.Forms.AnchorStyles.None
                    };
                    btnBackup.Click += (s, ev) => BackupData(); // Wire up the Backup button
                    toolsLayout.Controls.Add(btnBackup, 0, 1);
                    toolsLayout.SetCellPosition(btnBackup, new System.Windows.Forms.TableLayoutPanelCellPosition(0, 1));
                    toolsLayout.SetColumnSpan(btnBackup, 1);

                    groupBoxTools.Controls.Add(toolsLayout);
                    layoutPanel.Controls.Add(groupBoxTools);

                    // Cancel and Save buttons
                    var buttonsLayout = new System.Windows.Forms.TableLayoutPanel
                    {
                        Dock = System.Windows.Forms.DockStyle.Bottom,
                        ColumnCount = 2,
                        AutoSize = true,
                        AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
                    };
                    buttonsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
                    buttonsLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));

                    var btnCancel = new System.Windows.Forms.Button
                    {
                        Text = "Cancel",
                        AutoSize = true,
                        Width = 80,
                        Height = 30,
                        Anchor = System.Windows.Forms.AnchorStyles.Right
                    };
                    btnCancel.Click += (s, ev) => frmOptions.Close();
                    var btnSave = new System.Windows.Forms.Button
                    {
                        Text = "Save",
                        AutoSize = true,
                        Width = 80,
                        Height = 30,
                        Anchor = System.Windows.Forms.AnchorStyles.Right
                    };
                    btnSave.Click += (s, ev) =>
                    {
                        // Save settings
                        IsSnapEnabled = chkEnableSnap.Checked;
                        TintValue = (int)numTint.Value;
                        SelectedColor = cmbColor.SelectedItem.ToString();
                        IsLogEnabled = chkEnableLog.Checked;

                        SaveSettings(); // Save to JSON
                                        // Apply TintValue to all fences
                        //foreach (var fence in Application.Current.Windows.OfType<NonActivatingWindow>())
                        //{
                        //    ApplyTintToFence(fence);
                        //}

                        // Apply TintValue and SelectedColor to all fences
                        foreach (var fence in Application.Current.Windows.OfType<NonActivatingWindow>())
                        {
                            ApplyTintAndColorToFence(fence);
                        }

                        frmOptions.Close();
                    };
                    buttonsLayout.Controls.Add(btnCancel, 0, 0);
                    buttonsLayout.Controls.Add(btnSave, 1, 0);

                    layoutPanel.Controls.Add(buttonsLayout);

                    // Add the layout panel to the form
                    frmOptions.Controls.Add(layoutPanel);

                    // Show the form as a modal dialog
                    frmOptions.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions
                System.Windows.Forms.MessageBox.Show($"An error occurred: {ex.Message}", "Error",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
            }
        }



        private void BackupData()
        {
            try
            {
                string exePath = Assembly.GetEntryAssembly().Location;
                string exedir = Path.GetDirectoryName(exePath);
                string jsonFilePath = Path.Combine(exedir, "fences.json");
                string shortcutsFolderPath = Path.Combine(exedir, "Shortcuts");
                string backupsFolderPath = Path.Combine(exedir, "Backups");

                // Create the "Backups" folder if it doesn't exist
                if (!Directory.Exists(backupsFolderPath))
                {
                    Directory.CreateDirectory(backupsFolderPath);
                }

                // Create a new backup folder with the current datetime
                string backupFolderName = DateTime.Now.ToString("yyMMddHHmm") + "_backup";
                string backupFolderPath = Path.Combine(backupsFolderPath, backupFolderName);
                Directory.CreateDirectory(backupFolderPath);

                // Copy the "fences.json" file
                string backupJsonFilePath = Path.Combine(backupFolderPath, "fences.json");
                System.IO.File.Copy(jsonFilePath, backupJsonFilePath, true);

                // Copy the "Shortcuts" folder
                string backupShortcutsFolderPath = Path.Combine(backupFolderPath, "Shortcuts");
                if (Directory.Exists(shortcutsFolderPath))
                {
                    Directory.CreateDirectory(backupShortcutsFolderPath);
                    foreach (string dirPath in Directory.GetDirectories(shortcutsFolderPath, "*", SearchOption.AllDirectories))
                    {
                        Directory.CreateDirectory(dirPath.Replace(shortcutsFolderPath, backupShortcutsFolderPath));
                    }
                    foreach (string newPath in Directory.GetFiles(shortcutsFolderPath, "*.*", SearchOption.AllDirectories))
                    {
                        System.IO.File.Copy(newPath, newPath.Replace(shortcutsFolderPath, backupShortcutsFolderPath), true);
                    }
                }

                MessageBox.Show("Backup completed successfully.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during backup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void Application_Startup(object sender, StartupEventArgs e)
        {
            //_snapTimer = new DispatcherTimer
            //{
            //    Interval = TimeSpan.FromMilliseconds(100)
            //};
            //_snapTimer.Tick += SnapTimer_Tick;
            LoadSettings();

            //   ListEmbeddedResources(); // Call the method to list resources
            // Initialize NotifyIcon
            //   _trayIcon = new System.Windows.Forms.NotifyIcon
            string mexePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            _trayIcon = new System.Windows.Forms.NotifyIcon

            {
                // Icon = new System.Drawing.Icon("logo2.ico"), // Path to your logo2.ico
                //Visible = true
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(mexePath), // Use the executable's icon
                Visible = true
            };


            // Create a new context menu for the NotifyIcon
            var trayMenu = new System.Windows.Forms.ContextMenuStrip();


            string exePath = Assembly.GetEntryAssembly().Location;
            string exedir = Path.GetDirectoryName(exePath);
            string jsonFilePath = Path.Combine(exedir, "fences.json");

            // Load or create default JSON
            dynamic fencedata;
            if (System.IO.File.Exists(jsonFilePath))
            {
                string jsonContent = System.IO.File.ReadAllText(jsonFilePath);
                fencedata = Newtonsoft.Json.JsonConvert.DeserializeObject(jsonContent);

                // Migrate legacy JSON (add "IsFolder" property if missing)
                foreach (var fence in fencedata)
                {
                    if (fence["ItemsType"]?.ToString() == "Portal")
                    {
                        // Portal fences store a folder path in "Items"
                        string portalPath = fence["Items"]?.ToString();
                        if (!string.IsNullOrEmpty(portalPath))
                        {
                            // Ensure the portal path exists
                            if (!Directory.Exists(portalPath))
                            {
                                // If the portal folder is missing, mark it as a missing folder
                                fence["IsFolder"] = true;
                            }
                        }
                    }
                    else
                    {
                        // Regular fences: check each item
                        foreach (var item in fence["Items"])
                        {
                            if (item["IsFolder"] == null)
                            {
                                // Determine if the item is a folder based on its path
                                string path = item["Filename"]?.ToString();
                                bool isFolder = Directory.Exists(path);
                                item["IsFolder"] = isFolder;
                            }
                        }
                    }
                }

                // Save the migrated JSON
                System.IO.File.WriteAllText(jsonFilePath, Newtonsoft.Json.JsonConvert.SerializeObject(fencedata));
            }
            else
            {
                // Create default JSON (existing logic)
                string defaultJson = "[{\"Title\":\"New Fence\",\"X\":20,\"Y\":20,\"Width\":200,\"Height\":200,\"ItemsType\":\"Data\",\"Items\":[]}]";
                System.IO.File.WriteAllText(jsonFilePath, defaultJson);
                fencedata = Newtonsoft.Json.JsonConvert.DeserializeObject(defaultJson);
            }


            // Add "Options" menu item
            var optionsMenuItem = new System.Windows.Forms.ToolStripMenuItem("Options");
            optionsMenuItem.Click += (s, ev) => ShowOptionsForm();
            trayMenu.Items.Add(optionsMenuItem);



            var aboutMenuItem = new System.Windows.Forms.ToolStripMenuItem("About");
            aboutMenuItem.Click += (s, ev) =>
            {
                try
                {
                    using (var frmAbout = new System.Windows.Forms.Form())
                    {
                        // Set up the form
                        frmAbout.Text = "About Birdy Fences";
                        frmAbout.Size = new System.Drawing.Size(400, 450); // Adjust size
                        frmAbout.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
                        frmAbout.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
                        frmAbout.MaximizeBox = false;
                        frmAbout.MinimizeBox = false;

                        // Set the form icon to match the executable icon
                        string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                        frmAbout.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);

                        // Create a TableLayoutPanel for central alignment
                        var layoutPanel = new System.Windows.Forms.TableLayoutPanel
                        {
                            Dock = System.Windows.Forms.DockStyle.Fill,
                            ColumnCount = 1,
                            RowCount = 7, // Number of items
                            AutoSize = true,
                            AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
                            Padding = new System.Windows.Forms.Padding(20) // Add padding
                        };
                        layoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));

                        // Add a PictureBox for the image
                        var pictureBox = new System.Windows.Forms.PictureBox
                        {
                            Image = LoadImageFromResources("Birdy_Fences.Resources.logo1.png"), // Correct resource path
                            SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom,
                            Dock = System.Windows.Forms.DockStyle.Fill,
                            Height = 100 // Adjust size if needed
                        };
                        layoutPanel.Controls.Add(pictureBox);

                        // Add label for "Birdy Fences" title
                        var labelTitle = new System.Windows.Forms.Label
                        {
                            Text = "Birdy Fences",
                            Font = new System.Drawing.Font("Arial", 14, System.Drawing.FontStyle.Bold),
                            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                            Dock = System.Windows.Forms.DockStyle.Fill,
                            AutoSize = true
                        };
                        layoutPanel.Controls.Add(labelTitle);

                        // Add label for version
                        var version = Assembly.GetExecutingAssembly().GetName().Version;
                        var labelVersion = new System.Windows.Forms.Label
                        {
                            Text = $"ver {version}",
                            Font = new System.Drawing.Font("Arial", 10, System.Drawing.FontStyle.Bold),
                            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                            Dock = System.Windows.Forms.DockStyle.Fill,
                            AutoSize = true
                        };
                        layoutPanel.Controls.Add(labelVersion);

                        // Add label for the main text
                        var labelMainText = new System.Windows.Forms.Label
                        {
                            Text = "BirdyFences is an open-source alternative to StarDock's Fences, originally created by HakanKokcu.\n\nThis fork, maintained by Nikos Georgousis (limbo666), has been significantly enhanced and optimized for better performance and stability.",
                            Font = new System.Drawing.Font("Arial", 10),
                            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                            Dock = System.Windows.Forms.DockStyle.Fill,
                            AutoSize = true
                        };
                        layoutPanel.Controls.Add(labelMainText);

                        // Add a horizontal line
                        var horizontalLine = new System.Windows.Forms.Label
                        {
                            BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D,
                            Height = 2,
                            Dock = System.Windows.Forms.DockStyle.Fill,
                            Margin = new System.Windows.Forms.Padding(10, 10, 10, 10) // Add padding
                        };
                        layoutPanel.Controls.Add(horizontalLine);

                        // Add label for the GitHub text
                        var labelGitHubText = new System.Windows.Forms.Label
                        {
                            Text = "Please visit GitHub for news, updates, and bug reports.",
                            Font = new System.Drawing.Font("Arial", 9),
                            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                            Dock = System.Windows.Forms.DockStyle.Fill,
                            AutoSize = true
                        };
                        layoutPanel.Controls.Add(labelGitHubText);

                        // Add a hyperlink to GitHub
                        var linkLabelGitHub = new System.Windows.Forms.LinkLabel
                        {
                            Text = "https://github.com/limbo666/BirdyFences",
                            Font = new System.Drawing.Font("Arial", 9),
                            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                            Dock = System.Windows.Forms.DockStyle.Fill,
                            AutoSize = true
                        };
                        linkLabelGitHub.LinkClicked += (sender, e) =>
                        {
                            try
                            {
                                // Open the GitHub link in the default browser
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "https://github.com/limbo666/BirdyFences",
                                    UseShellExecute = true // This ensures the URL is opened in the default browser
                                });
                            }
                            catch (Exception ex)
                            {
                                // Handle any errors (e.g., log or show a message)
                                MessageBox.Show($"Error opening GitHub link: {ex.Message}");
                            }
                        };
                        layoutPanel.Controls.Add(linkLabelGitHub);

                        // Add the layout panel to the form
                        frmAbout.Controls.Add(layoutPanel);

                        // Show the form as a modal dialog
                        frmAbout.ShowDialog();
                    }

                }
                catch (Exception ex)
                {
                    // Handle exceptions
                    System.Windows.Forms.MessageBox.Show($"An error occurred: {ex.Message}", "Error",
                        System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            };
            trayMenu.Items.Add(aboutMenuItem);


            // Initialize and start the TargetChecker
            _targetChecker = new TargetChecker(1000); // Check every 1 second
            _targetChecker.Start();

     

            //// Apply TintValue to all fences on startup
            //foreach (var fence in Application.Current.Windows.OfType<NonActivatingWindow>())
            //{
            //    ApplyTintToFence(fence);
            //}



            // Add "Exit" menu item
            var exitMenuItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exitMenuItem.Click += (s, ev) => System.Windows.Application.Current.Shutdown();
            trayMenu.Items.Add(exitMenuItem);

            // Assign the context menu to the tray icon
            _trayIcon.ContextMenuStrip = trayMenu;





            //      dynamic fencedata = Newtonsoft.Json.JsonConvert.DeserializeObject(File.ReadAllText(exedir + "\\fences.json"));
            void createFence(dynamic fence)
            {
                DockPanel dp = new();
                Border cborder = new() { Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)), CornerRadius = new CornerRadius(6), Child = dp };
                ContextMenu cm = new();
                MenuItem miNF = new() { Header = "New Fence" };
                cm.Items.Add(miNF);
                MenuItem miNP = new() { Header = "New Portal Fence" };
                cm.Items.Add(miNP);
                MenuItem miRF = new() { Header = "Remove Fence" };
                cm.Items.Add(miRF);
                cm.Items.Add(new Separator());
                MenuItem miXT = new() { Header = "Exit" };
                cm.Items.Add(miXT);

                // Use NonActivatingWindow instead of Window
                NonActivatingWindow win = new() { ContextMenu = cm, AllowDrop = true, AllowsTransparency = true, Background = Brushes.Transparent, Title = fence["Title"], ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = cborder, ResizeMode = ResizeMode.CanResize, Width = fence["Width"], Height = fence["Height"], Top = fence["Y"], Left = fence["X"] };

                miRF.Click += (sender, e) =>
                {
                    // Check if the fence is a portal fence
                    bool isPortalFence = fence["ItemsType"].ToString() == "Portal";

                    // Remove the fence from the data
                    fence.Remove();
                    win.Close();

                    // Only delete shortcut files if this is NOT a portal fence
                    if (!isPortalFence)
                    {
                        foreach (dynamic icon in fence["Items"])
                        {
                            string filePath = (string)icon["Filename"];

                            // Check if the file is a shortcut (.lnk)
                            if (Path.GetExtension(filePath).ToLower() == ".lnk" && System.IO.File.Exists(filePath))
                            {
                                try
                                {
                                    System.IO.File.Delete(filePath);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"Error deleting shortcut file: {ex.Message}");
                                }
                            }
                        }
                    }

                    // Save the updated fence data
                    SaveFencesData(fencedata, Path.Combine(exedir, "fences.json"));
                };

                miNF.Click += (sender, e) =>
                {
                    // Get the mouse position in screen coordinates
                    var mousePosition = System.Windows.Forms.Cursor.Position;

                    // Create a new fence at the mouse position
                    Newtonsoft.Json.Linq.JObject fnc = new(
                        new Newtonsoft.Json.Linq.JProperty("Title", "New Fence"),
                        new Newtonsoft.Json.Linq.JProperty("Width", 300),
                        new Newtonsoft.Json.Linq.JProperty("Height", 150),
                        new Newtonsoft.Json.Linq.JProperty("X", mousePosition.X), // Set X to mouse position (screen coordinates)
                        new Newtonsoft.Json.Linq.JProperty("Y", mousePosition.Y), // Set Y to mouse position (screen coordinates)
                        new Newtonsoft.Json.Linq.JProperty("ItemsType", "Data"),
                        new Newtonsoft.Json.Linq.JProperty("Items", new Newtonsoft.Json.Linq.JArray())
                    );

                    // Add the new fence to the data and create it
                    fencedata.Add(fnc);
                    createFence(fnc);

                    // Save the updated fence data
                    SaveFencesData(fencedata, Path.Combine(exedir, "fences.json"));
                };

                miNP.Click += (sender, e) =>
                {
                    // Get the mouse position in screen coordinates
                    var mousePosition = System.Windows.Forms.Cursor.Position;

                    // Open a folder browser dialog to select the portal folder
                    using var dialog = new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description = "Select Folder For Portal",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = true
                    };

                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        // Create a new portal fence at the mouse position
                        Newtonsoft.Json.Linq.JObject fnc = new(
                            new Newtonsoft.Json.Linq.JProperty("Title", "New Portal Fence"),
                            new Newtonsoft.Json.Linq.JProperty("Width", 300),
                            new Newtonsoft.Json.Linq.JProperty("Height", 150),
                            new Newtonsoft.Json.Linq.JProperty("X", mousePosition.X), // Set X to mouse position (screen coordinates)
                            new Newtonsoft.Json.Linq.JProperty("Y", mousePosition.Y), // Set Y to mouse position (screen coordinates)
                            new Newtonsoft.Json.Linq.JProperty("ItemsType", "Portal"),
                            new Newtonsoft.Json.Linq.JProperty("Items", dialog.SelectedPath)
                        );

                        // Add the new portal fence to the data and create it
                        fencedata.Add(fnc);
                        createFence(fnc);

                        // Save the updated fence data
                        SaveFencesData(fencedata, Path.Combine(exedir, "fences.json"));
                    }
                };

                miXT.Click += (sender, e) =>
                {
                    System.Environment.Exit(1);
                };

                WindowChrome.SetWindowChrome(win, new WindowChrome() { CaptionHeight = 0, ResizeBorderThickness = new Thickness(5) });
                Label titlelabel = new() { Content = (string)fence["Title"], Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)), Foreground = Brushes.White, HorizontalContentAlignment = HorizontalAlignment.Center };
                dp.Children.Add(titlelabel);
                TextBox titletb = new() { HorizontalContentAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
                dp.Children.Add(titletb);
                titlelabel.MouseDown += (object sender, MouseButtonEventArgs e) =>
                {
                    if (e.ClickCount == 1)
                    {
                        if (e.LeftButton == MouseButtonState.Pressed)
                        {
                            win.DragMove();
                        }
                    }
                    else
                    {
                        titlelabel.Visibility = Visibility.Collapsed;
                        titletb.Visibility = Visibility.Visible;
                        titletb.Text = (string)titlelabel.Content;
                        titletb.Focus(); // Set focus to the TextBox
                        titletb.SelectAll(); // Select all text in the TextBox
                        win.EnableFocusPrevention(false); // Disable focus prevention
                    }
                };
                titletb.LostFocus += (sender, e) =>
                {
                    titlelabel.Visibility = Visibility.Visible;
                    titletb.Visibility = Visibility.Collapsed;
                    win.EnableFocusPrevention(true); // Re-enable focus prevention
                };
                titletb.KeyDown += (object sender, KeyEventArgs e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        titlelabel.Visibility = Visibility.Visible;
                        titletb.Visibility = Visibility.Collapsed;
                        titlelabel.Content = titletb.Text;
                        fence["Title"] = titletb.Text;
                        SaveFencesData(fencedata, Path.Combine(exedir, "fences.json"));
                        win.EnableFocusPrevention(true); // Re-enable focus prevention
                    }
                    else if (e.Key == Key.Escape)
                    {
                        titlelabel.Visibility = Visibility.Visible;
                        titletb.Visibility = Visibility.Collapsed;
                        win.EnableFocusPrevention(true); // Re-enable focus prevention
                    }
                };
                titlelabel.MouseUp += (object sender, MouseButtonEventArgs e) =>
                {
                    fence["Y"] = win.Top;
                    fence["X"] = win.Left;
                    SaveFencesData(fencedata, Path.Combine(exedir, "fences.json"));
                };
                win.SizeChanged += (sender, e) =>
                {
                    fence["Width"] = win.ActualWidth;
                    fence["Height"] = win.ActualHeight;
                    fence["Y"] = win.Top;
                    fence["X"] = win.Left;
                    SaveFencesData(fencedata, Path.Combine(exedir, "fences.json"));
                };
                DockPanel.SetDock(titlelabel, Dock.Top);
                DockPanel.SetDock(titletb, Dock.Top);
                WrapPanel wpcont = new() { AllowDrop = true };





                void addicon(dynamic icon)
                {
                    StackPanel sp = new() { Margin = new Thickness(5) };
                    sp.Width = 60;

                    // Create the context menu
                    ContextMenu mn = new();

                    // Add the "Run as administrator" option
                    MenuItem miRunAsAdmin = new() { Header = "Run as administrator" };
                    miRunAsAdmin.IsEnabled = IsExecutableFile((string)icon["Filename"]); // Enable only for executables
                    miRunAsAdmin.Click += (sender, e) =>
                    {
                        try
                        {
                            // Get the target path
                            string filePath = (string)icon["Filename"];
                            string targetPath = GetShortcutTarget(filePath);

                            if (!string.IsNullOrEmpty(targetPath))
                            {
                                // Launch the target file as administrator
                                var processStartInfo = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = targetPath,
                                    Verb = "runas", // Run as administrator
                                    UseShellExecute = true
                                };

                                System.Diagnostics.Process.Start(processStartInfo);
                            }
                            else
                            {
                                MessageBox.Show("The target could not be found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error running as administrator: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };

                    // Existing menu items
                    MenuItem miE = new() { Header = "Edit" };
                    MenuItem miM = new() { Header = "Move.." };
                    MenuItem miRemove = new() { Header = "Remove" };

                    // Add the "Find target..." option
                    MenuItem miFindTarget = new() { Header = "Find target..." };
                    miFindTarget.Click += (sender, e) =>
                    {
                        try
                        {
                            // Get the shortcut's target path
                            string shortcutPath = (string)icon["Filename"];
                            string targetPath = GetShortcutTarget(shortcutPath);

                            if (!string.IsNullOrEmpty(targetPath))
                            {
                                // Open the folder containing the target
                                string targetFolder = System.IO.Path.GetDirectoryName(targetPath);
                                if (System.IO.Directory.Exists(targetFolder))
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", $"/select, \"{targetPath}\"");
                                }
                                else
                                {
                                    MessageBox.Show("The target folder does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            }
                            else
                            {
                                MessageBox.Show("The shortcut target could not be found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error finding target: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };

                    // Add the "Copy path" option
                    MenuItem miCopyPath = new() { Header = "Copy path" };

                    // Add the "Folder" sub-option
                    MenuItem miCopyFolder = new() { Header = "Folder" };
                    miCopyFolder.Click += (sender, e) =>
                    {
                        try
                        {
                            // Get the target path
                            string filePath = (string)icon["Filename"];
                            string targetPath = GetShortcutTarget(filePath);

                            if (!string.IsNullOrEmpty(targetPath))
                            {
                                // Get the folder path
                                string folderPath = System.IO.Path.GetDirectoryName(targetPath);

                                // Copy the folder path to the clipboard
                                System.Windows.Clipboard.SetText(folderPath);
                            }
                            else
                            {
                                MessageBox.Show("The target could not be found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error copying folder path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };

                    // Add the "Full path" sub-option
                    MenuItem miCopyFullPath = new() { Header = "Full path" };
                    miCopyFullPath.Click += (sender, e) =>
                    {
                        try
                        {
                            // Get the target path
                            string filePath = (string)icon["Filename"];
                            string targetPath = GetShortcutTarget(filePath);

                            if (!string.IsNullOrEmpty(targetPath))
                            {
                                // Copy the full path to the clipboard
                                System.Windows.Clipboard.SetText(targetPath);
                            }
                            else
                            {
                                MessageBox.Show("The target could not be found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error copying full path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };

                    // Add the sub-items to the "Copy path" menu item
                    miCopyPath.Items.Add(miCopyFolder);
                    miCopyPath.Items.Add(miCopyFullPath);

                    // Add all menu items to the context menu
                    mn.Items.Add(miE);
                    mn.Items.Add(miM);
                    mn.Items.Add(miRemove);
                    mn.Items.Add(new Separator()); // Horizontal line
                    mn.Items.Add(miRunAsAdmin);
                    mn.Items.Add(new Separator()); // Horizontal line
                    mn.Items.Add(miCopyPath); // "Copy path" option
                    mn.Items.Add(miFindTarget); // "Find target..." option

                    // Assign the context menu to the StackPanel
                    sp.ContextMenu = mn;

           



                    Image ico = new() { Width = 40, Height = 40, Margin = new Thickness(5) };
                    string filePath = (string)icon["Filename"];


                    //_targetChecker.AddCheckAction(filePath, () =>
                    //{
                    //    bool pathExists = Directory.Exists(filePath) || System.IO.File.Exists(filePath);
                    //    bool isFolder = Directory.Exists(filePath);

                    //    // Update the icon based on the existence of the file/folder
                    //    Application.Current.Dispatcher.Invoke(() =>
                    //    {
                    //        if (pathExists)
                    //        {
                    //            // Update to existing icon
                    //            ico.Source = isFolder
                    //                ? new BitmapImage(new Uri("pack://application:,,,/folder-White.png"))
                    //                : System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource();
                    //        }
                    //        else
                    //        {
                    //            // Update to missing icon
                    //            ico.Source = isFolder
                    //                ? new BitmapImage(new Uri("pack://application:,,,/folder-WhiteX.png"))
                    //                : new BitmapImage(new Uri("pack://application:,,,/file-WhiteX.png"));
                    //        }
                    //    });
                    //}, Directory.Exists(filePath));

                    _targetChecker.AddCheckAction(filePath, () =>
                    {
                        // Get the original "IsFolder" value from the icon's data
                        bool isFolder = (bool)icon["IsFolder"]; // <-- KEY CHANGE

                        bool pathExists = isFolder
                            ? Directory.Exists(filePath)
                            : System.IO.File.Exists(filePath);

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            if (pathExists)
                            {
                                ico.Source = isFolder
                                    ? new BitmapImage(new Uri("pack://application:,,,/folder-White.png"))
                                    : System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource();
                            }
                            else
                            {
                                ico.Source = isFolder
                                    ? new BitmapImage(new Uri("pack://application:,,,/folder-WhiteX.png"))
                                    : new BitmapImage(new Uri("pack://application:,,,/file-WhiteX.png"));
                            }
                        });
                    }, isFolder: (bool)icon["IsFolder"]); // <-- Pass the original value


                    //_targetChecker.AddCheckAction(filePath, () =>
                    //{
                    //    bool pathExists = Directory.Exists(filePath) || System.IO.File.Exists(filePath);
                    //    bool isFolder = Directory.Exists(filePath);

                    //    // Update the icon based on the existence of the file/folder
                    //    Application.Current.Dispatcher.Invoke(() =>
                    //    {
                    //        if (pathExists)
                    //        {
                    //            // Update to existing icon
                    //            ico.Source = isFolder
                    //                ? new BitmapImage(new Uri("pack://application:,,,/folder-White.png"))
                    //                : System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource();
                    //        }
                    //        else
                    //        {
                    //            // Update to missing icon
                    //            ico.Source = isFolder
                    //                ? new BitmapImage(new Uri("pack://application:,,,/folder-WhiteX.png"))
                    //                : new BitmapImage(new Uri("pack://application:,,,/file-WhiteX.png"));
                    //        }
                    //    });
                    //}, Directory.Exists(filePath));


                    // Check if the item is part of a portal fence
                    bool isPortalFence = fence["ItemsType"]?.ToString() == "Portal";

                    if (isPortalFence)
                    {
                        // Portal fences: treat all items as folders
                        bool isFolder = true;
                        bool pathExists = Directory.Exists(filePath);

                        if (pathExists)
                        {
                            // Existing folder in portal fence
                            ico.Source = new BitmapImage(new Uri("pack://application:,,,/folder-White.png"));
                        }
                        else
                        {
                            // Missing folder in portal fence
                            ico.Source = new BitmapImage(new Uri("pack://application:,,,/folder-WhiteX.png"));
                        }
                    }
                    else
                    {
                        // Regular fences: use IsFolder property
                        bool isFolder = (bool)icon["IsFolder"];
                        bool pathExists = isFolder ? Directory.Exists(filePath) : System.IO.File.Exists(filePath);

                        if (pathExists)
                        {
                            // Existing item
                            if (isFolder)
                            {
                                ico.Source = new BitmapImage(new Uri("pack://application:,,,/folder-White.png"));
                            }
                            else
                            {
                                try
                                {
                                    ico.Source = icon["DisplayIcon"] == null
                                        ? System.Drawing.Icon.ExtractAssociatedIcon(filePath).ToImageSource()
                                        : new BitmapImage(new Uri((string)icon["DisplayIcon"], UriKind.Relative));
                                }
                                catch
                                {
                                    ico.Source = new BitmapImage(new Uri("pack://application:,,,/file-WhiteX.png"));
                                }
                            }
                        }
                        else
                        {
                            // Missing item
                            ico.Source = isFolder
                                ? new BitmapImage(new Uri("pack://application:,,,/folder-WhiteX.png"))
                                : new BitmapImage(new Uri("pack://application:,,,/file-WhiteX.png"));
                        }
                    }

                    sp.Children.Add(ico);




                    // Add the icon label
                    TextBlock lbl = new()
                    {
                        TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.None,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = Brushes.White,
                        MaxWidth = double.MaxValue,
                        Width = double.NaN,
                        TextAlignment = TextAlignment.Center
                    };

                    string displayText = (icon["DisplayName"] == null) ? Path.GetFileNameWithoutExtension((string)icon["Filename"]) : (string)icon["DisplayName"];
                    if (displayText.Length > 20)
                    {
                        displayText = displayText.Substring(0, 20) + "..."; // Add ellipsis
                    }
                    lbl.Text = displayText;

                    sp.Children.Add(lbl);

                    // Handle the "Move" option
                    miM.Click += (sender, e) =>
                    {
                        StackPanel cnt = new();
                        Window wwin = new() { Title = "Move " + (string)icon["Filename"], Content = cnt, Width = 300, Height = 100, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                        ComboBox lv = new();
                        foreach (dynamic icn in fence["Items"])
                        {
                            lv.Items.Add(icn["Filename"]);
                        }
                        cnt.Children.Add(lv);
                        Button btn = new() { Content = "Move" };
                        cnt.Children.Add(btn);
                        btn.Click += (sender, e) =>
                        {
                            try
                            {
                                int id = wpcont.Children.IndexOf(sp);
                                dynamic olddata = fence["Items"][lv.SelectedIndex];
                                fence["Items"][lv.SelectedIndex] = fence["Items"][id];
                                fence["Items"][id] = olddata;
                                System.IO.File.WriteAllText(exedir + "\\fences.json", Newtonsoft.Json.JsonConvert.SerializeObject(fencedata));
                                initcontent();
                                wwin.Close();
                            }
                            catch (Exception f)
                            {
                                MessageBox.Show(f.Message);
                            }
                        };
                        wwin.ShowDialog();
                    };

                    // Handle the "Edit" option
                    miE.Click += (sender, e) =>
                    {
                        StackPanel cnt = new();
                        Window wwin = new() { Title = "Edit " + (string)icon["Filename"], Content = cnt, Width = 450, Height = 200, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                        TextBox createsec(string name, string defaulval)
                        {
                            DockPanel dpp = new();
                            Label lbl = new() { Content = name };
                            dpp.Children.Add(lbl);
                            TextBox tbb = new() { Text = defaulval };
                            dpp.Children.Add(tbb);
                            cnt.Children.Add(dpp);
                            return tbb;
                        }
                        ;
                        int id = wpcont.Children.IndexOf(sp);
                        TextBox tbDN = createsec("Display Name", fence["Items"][id]["DisplayName"] == null ? "{AUTONAME}" : fence["Items"][id]["DisplayName"]);
                        TextBox tbDI = createsec("Display Icon", fence["Items"][id]["DisplayIcon"] == null ? "{AUTOICON}" : fence["Items"][id]["DisplayIcon"]);
                        Button btn = new() { Content = "Apply" };
                        btn.Click += (sender, e) =>
                        {
                            if (tbDN.Text == "{AUTONAME}")
                            {
                                try
                                {
                                    fence["Items"][id]["DisplayName"].Remove();
                                }
                                catch { }
                            }
                            else
                            {
                                fence["Items"][id]["DisplayName"] = tbDN.Text;
                                lbl.Text = tbDN.Text;
                            }
                            if (tbDI.Text == "{AUTOICON}")
                            {
                                try
                                {
                                    fence["Items"][id]["DisplayIcon"].Remove();
                                }
                                catch { }
                            }
                            else
                            {
                                fence["Items"][id]["DisplayIcon"] = tbDI.Text;
                                ico.Source = new BitmapImage(new Uri(tbDN.Text));
                            }
                            System.IO.File.WriteAllText(exedir + "\\fences.json", Newtonsoft.Json.JsonConvert.SerializeObject(fencedata));
                            wwin.Close();
                        };
                        cnt.Children.Add(btn);
                        wwin.ShowDialog();
                    };

                    // Handle the "Remove" option

                    miRemove.Click += (sender, e) =>
                    {
                        // Get the path of the file
                        string filePath = (string)icon["Filename"];

                        // Apply the zoom-out and darken effect
                        var zoomOutAnimation = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            To = 0.3, // Scale down to 30%
                            Duration = TimeSpan.FromMilliseconds(400), // Animation duration
                            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop // Stop the animation after completion
                        };

                        var darkenAnimation = new System.Windows.Media.Animation.ColorAnimation
                        {
                            To = System.Windows.Media.Colors.Black, // Darken to black
                            Duration = TimeSpan.FromMilliseconds(400), // Animation duration
                            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop // Stop the animation after completion
                        };

                        // Apply the zoom-out effect to the StackPanel
                        if (sp.RenderTransform == null || !(sp.RenderTransform is ScaleTransform))
                        {
                            sp.RenderTransform = new ScaleTransform(1, 1);
                            sp.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5); // Center the transform
                        }
    ((ScaleTransform)sp.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, zoomOutAnimation);
                        ((ScaleTransform)sp.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, zoomOutAnimation);

                        // Apply the darken effect to the StackPanel's background
                        if (sp.Background == null || !(sp.Background is System.Windows.Media.SolidColorBrush))
                        {
                            sp.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Transparent);
                        }
    ((System.Windows.Media.SolidColorBrush)sp.Background).BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, darkenAnimation);

                        // Delay the removal of the icon until the animation completes
                        var timer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(400) // Match the animation duration
                        };
                        timer.Tick += (s, ev) =>
                        {
                            timer.Stop();

                            // Remove the icon from the fence
                            icon.Remove();
                            wpcont.Children.Remove(sp);

                            // Delete the file if it's a shortcut
                            if (Path.GetExtension(filePath).ToLower() == ".lnk" && System.IO.File.Exists(filePath))
                            {
                                try
                                {
                                    System.IO.File.Delete(filePath);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show($"Error deleting shortcut file: {ex.Message}");
                                }
                            }

                            // Save the updated fence data
                            System.IO.File.WriteAllText(exedir + "\\fences.json", Newtonsoft.Json.JsonConvert.SerializeObject(fencedata));
                        };
                        timer.Start();
                    };



                    // Add the StackPanel to the WrapPanel or other container
                    wpcont.Children.Add(sp);

                    // Add the click event and animation
                    var p = new Process();
                    p.StartInfo = new ProcessStartInfo((string)icon["Filename"])
                    {
                        UseShellExecute = true
                    };

                    // click event
                    new ClickEventAdder(sp).Click += (sender, e) =>
                    {
                        // Ensure the StackPanel has a RenderTransform
                        if (sp.RenderTransform == null || !(sp.RenderTransform is ScaleTransform))
                        {
                            sp.RenderTransform = new ScaleTransform(1, 1);
                            sp.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5); // Center the transform
                        }

                        // Ensure the StackPanel has a DropShadowEffect
                        if (sp.Effect == null || !(sp.Effect is System.Windows.Media.Effects.DropShadowEffect))
                        {
                            sp.Effect = new System.Windows.Media.Effects.DropShadowEffect
                            {
                                Color = System.Windows.Media.Colors.Yellow, // Glow color
                                ShadowDepth = 0, // No shadow offset
                                BlurRadius = 20, // Glow size
                                Opacity = 0 // Start with no glow
                            };
                        }

                        // Zoom effect
                        var scaleAnimation = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            To = 1.2,
                            Duration = TimeSpan.FromMilliseconds(100),
                            AutoReverse = true,
                            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                        };

                        // Glow effect
                        var glowAnimation = new System.Windows.Media.Animation.DoubleAnimation
                        {
                            To = 1,
                            Duration = TimeSpan.FromMilliseconds(200),
                            AutoReverse = true,
                            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                        };

                        // Apply both animations
                        ((ScaleTransform)sp.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
                        ((ScaleTransform)sp.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
                        ((System.Windows.Media.Effects.DropShadowEffect)sp.Effect).BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, glowAnimation);

                        // Start the process (existing logic)
                        try
                        {
                            p.Start();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message);
                        }
                    };
                }

           





                win.DragOver += (object sender, DragEventArgs e) =>
                {
                    e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
                    //e.Handled = true;
                };
                win.DragEnter += (object sender, DragEventArgs e) =>
                {
                    e.Effects = DragDropEffects.Copy | DragDropEffects.Move;
                    //e.Handled = true;
                };




                win.Drop += (object sender, DragEventArgs e) =>
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    foreach (string dt in files)
                    {
                        if (Path.GetExtension(dt).ToLower() == ".lnk") // Check if it's a shortcut
                        {
                            // Define the "Shortcuts" subfolder path
                            string shortcutsFolder = Path.Combine(exedir, "Shortcuts");

                            // Ensure the "Shortcuts" folder exists
                            if (!Directory.Exists(shortcutsFolder))
                            {
                                Directory.CreateDirectory(shortcutsFolder);
                            }

                            // Generate a unique name for the shortcut
                            string shortcutName = Path.GetFileNameWithoutExtension(dt);
                            string destinationPath = Path.Combine(shortcutsFolder, shortcutName + ".lnk");

                            int counter = 1;
                            while (System.IO.File.Exists(destinationPath))
                            {
                                destinationPath = Path.Combine(shortcutsFolder, $"{shortcutName} ({counter}).lnk");
                                counter++;
                            }

                            // Use ShortcutManager to create a new shortcut
                            ShortcutManager.CreateShortcut(dt, destinationPath);

                            // Add the new shortcut to the fence
                            Newtonsoft.Json.Linq.JObject icon = new(
                                new Newtonsoft.Json.Linq.JProperty("Filename", destinationPath),
                                new Newtonsoft.Json.Linq.JProperty("IsFolder", false) // Shortcuts are not folders
                            );
                            fence["Items"].Add(icon);
                            addicon(icon);
                        }
                        else
                        {
                            bool isFolder = System.IO.Directory.Exists(dt);
                            Newtonsoft.Json.Linq.JObject icon = new(
                                new Newtonsoft.Json.Linq.JProperty("Filename", dt),
                                new Newtonsoft.Json.Linq.JProperty("IsFolder", isFolder) // Add this line
                            );

                            fence["Items"].Add(icon);
                            addicon(icon);
                        }
                    }
                    System.IO.File.WriteAllText(exedir + "\\fences.json", Newtonsoft.Json.JsonConvert.SerializeObject(fencedata));
                };

                // end of windrop


                void initcontent()
                {
                    wpcont.Children.Clear();
                    if (fence["ItemsType"] == "Data")
                    {
                        foreach (dynamic icon in fence["Items"])
                        {
                            addicon(icon);
                        }
                    }
                    else if (fence["ItemsType"] == "Portal")
                    {
                        string dpath = (string)fence["Items"];

                        // Check if the directory exists
                        if (System.IO.Directory.Exists(dpath))
                        {
                            try
                            {
                                // Get directories and files
                                string[] dirs = System.IO.Directory.GetDirectories(dpath);
                                string[] files = System.IO.Directory.GetFiles(dpath);

                                // Add directories to the fence
                                foreach (string dir in dirs)
                                {
                                    Newtonsoft.Json.Linq.JObject icon = new(
                                        new Newtonsoft.Json.Linq.JProperty("Filename", dir),
                                        new Newtonsoft.Json.Linq.JProperty("DisplayIcon", "folder-White.png")
                                    );
                                    addicon(icon);
                                }

                                // Add files to the fence
                                foreach (string file in files)
                                {
                                    Newtonsoft.Json.Linq.JObject icon = new(
                                        new Newtonsoft.Json.Linq.JProperty("Filename", file)
                                    );
                                    addicon(icon);
                                }
                            }
                            catch (Exception ex)
                            {
                                // Handle any errors (e.g., log or show a message)
                                MessageBox.Show($"Error loading portal contents: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else
                        {
                            // If the directory doesn't exist, show a warning or handle it gracefully
                            MessageBox.Show($"The directory '{dpath}' does not exist.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }

                initcontent();
                ScrollViewer wpcontscr = new() { Content = wpcont, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                dp.Children.Add(wpcontscr);
                  win.Show();
                  win.Loaded += (sender, e) => SetWindowLong(new WindowInteropHelper(win).Handle, GWL_HWNDPARENT, hprog);
               

                // Add snapping logic

                win.MouseMove += (sender, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed)
                    {
                        _currentDraggingFence = win;
                    }
                };

                win.MouseUp += (sender, e) =>
                {
                    if (e.ChangedButton == MouseButton.Left)
                    {
                        var (newLeft, newTop) = SnapToClosestFence(win, Application.Current.Windows.OfType<NonActivatingWindow>().ToList());
                        _currentDraggingFence = null;

                        // Update the JSON with the new position
                        fence["X"] = newLeft;
                        fence["Y"] = newTop;
                        string exePath = Assembly.GetEntryAssembly().Location;
                        string exedir = Path.GetDirectoryName(exePath);
                        string jsonFilePath = Path.Combine(exedir, "fences.json");
                        System.IO.File.WriteAllText(jsonFilePath, Newtonsoft.Json.JsonConvert.SerializeObject(fencedata));
                    }
                };



                // win.ShowWithoutActivation();
                //   win.Loaded += (sender, e) => SetWindowLong(new WindowInteropHelper(win).Handle, GWL_HWNDPARENT, hprog);
                // Console.WriteLine($"Fence {fence["Title"]} created and shown.");
                //  MessageBox.Show($"Fence {fence["Title"]} created and shown.","Info", MessageBoxButton.OK);


              //  // Apply TintValue to the new fence
               // ApplyTintToFence(win);
                // Apply TintValue and SelectedColor to the new fence
                ApplyTintAndColorToFence(win);

            }


            foreach (dynamic fence in fencedata)
            {
                createFence(fence);
            }
            // Apply TintValue and SelectedColor to all fences on startup
            foreach (var fence in Application.Current.Windows.OfType<NonActivatingWindow>())
            {
                ApplyTintAndColorToFence(fence);
            }



        }



        protected override void OnExit(ExitEventArgs e)
        {
           // _trayIcon.Dispose();
         //   base.OnExit(e);
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false; // Hide the tray icon
                _trayIcon.Dispose();      // Dispose of the NotifyIcon
            }
            base.OnExit(e);

        }

    }

    internal static class IconUtilities
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        public static ImageSource ToImageSource(this System.Drawing.Icon icon)
        {
            System.Drawing.Bitmap bitmap = icon.ToBitmap();
            IntPtr hBitmap = bitmap.GetHbitmap();

            ImageSource wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            if (!DeleteObject(hBitmap))
            {
                throw new Win32Exception();
            }

            return wpfBitmap;
        }
    }

 
}
