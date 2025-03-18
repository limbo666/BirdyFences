# BirdyFences
![logo1](https://github.com/user-attachments/assets/279284fd-2ba2-4175-b32b-f5868cc70c7b)

BirdyFences is alternative to the StarDock's Fences originaly created by HakanKokcu <br>
This fork has been **significantly enhanced and optimized** for better performance and stability.
Lot of new options added and the program is overall easier to use and manage. 

## Changes in v1.1

1. The `fences.json` file moved to the same path with executable. 
2. The first fence line is created on json file during the first execution.
3. A program icon was added to the executable. The icon updated on version 1.1
4. Error handlers on: move action, program execution, empty json file.
5. Added a minimal about screen.
6. Tray icon to indicate running application.
7. Program exit option on right click and tray context menu.

## Changes in v1.3

1. Better management on new fence creation. The fence is now created where the mouse is located.
2. Shortcuts are not relying in the existence of original shortcuts which are dropped into the fence, this allows the user to get rid of the originals once the fence has the icon.
3. Shortcut execution arguments (if any) are detected and replicated into fence shortcuts.
4. Error handlers added to detect target type.
5. Added visual effects on icon click and icon removal.

## Changes in v1.4

1. Right click options for "Run as administrator" (when applicable), "Copy path", "Find target" options available on icon right click menu.
2. Fixed folder icon appearance which was lost in previous version.
3. Added function to indicate broken links. This works on startup and checks targets also every second.
4. Json format changed to support the new functions. Added routine to update existing `fences.json` files automatically.
5. Fences are not coming in front of other windows when clicked.
6. Fixed missing "delete animation" for icons targeting folders or files.
7. Added options form to allow user to enable/disable snap function, select tint level and select fences base color. Options saved in `options.json` file.
8. Added backup mechanism triggered manually under options window. Backup saves fences settings and shortcuts into backups subfolder.
9. Added option to enable/disable logs for diagnostic purposes.

## Download
Get the latest release from [Release pages](https://github.com/limbo666/BirdyFences/releases) 


## Screenshots
![image](https://github.com/user-attachments/assets/ce3fc0cd-5213-4b6e-8405-2c443578ef95)

![image](https://github.com/user-attachments/assets/2d87c0e7-ec6b-4905-a3d9-c9dd922e5070)

![image](https://github.com/user-attachments/assets/3aa93df9-1a1c-4993-8dac-bd3c918b4bfe)

![image](https://github.com/user-attachments/assets/1cd9a855-8d91-46f8-be56-9e47b2040972)

![image](https://github.com/user-attachments/assets/713f3a7a-ff1a-449d-900e-91f8ab83fd24)




