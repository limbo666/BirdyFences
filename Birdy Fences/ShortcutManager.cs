using IWshRuntimeLibrary;
using System.IO;

namespace Birdy_Fences
{
    public class ShortcutManager
    {
        public static void CreateShortcut(string sourceShortcutPath, string destinationPath)
        {
            // Create a WshShell object
            WshShell shell = new WshShell();

            // Get the source shortcut
            IWshShortcut sourceShortcut = (IWshShortcut)shell.CreateShortcut(sourceShortcutPath);

            // Extract the target path, arguments, and icon location from the source shortcut
            string targetPath = sourceShortcut.TargetPath;
            string arguments = sourceShortcut.Arguments; // Extract arguments directly
            string iconLocation = sourceShortcut.IconLocation;

            // Create a new shortcut in the destination path
            IWshShortcut newShortcut = (IWshShortcut)shell.CreateShortcut(destinationPath);
            newShortcut.TargetPath = targetPath; // Set the target path
            newShortcut.Arguments = arguments;  // Set the arguments
            newShortcut.IconLocation = iconLocation; // Set the icon location
            newShortcut.Save(); // Save the shortcut
        }

        public static void EditShortcut(string shortcutPath, string newTargetPath)
        {
            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);

            // Preserve the existing icon
            string iconLocation = shortcut.IconLocation;

            // Update the target path
            shortcut.TargetPath = newTargetPath;
            shortcut.IconLocation = iconLocation;
            shortcut.Save();
        }
    }
}