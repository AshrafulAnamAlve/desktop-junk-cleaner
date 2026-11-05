SafeClean - a safety-first cleaner for your Windows Temp folder
================================================================

HOW TO RUN

1. Right-click the ZIP and choose "Extract All...". This step matters: SafeClean.exe needs the
   other files beside it, so it will not start from inside the ZIP.
2. Open the extracted folder and double-click SafeClean.exe.
3. If you see "Windows protected your PC", read the next section first.

You do not need to install .NET or anything else, and you do not need administrator rights.
Please do not use "Run as administrator": SafeClean turns cleanup off when it runs that way.


"WINDOWS PROTECTED YOUR PC"

SafeClean is free and has no paid code-signing certificate, so Windows SmartScreen does not
recognise it. The warning does not mean a virus was found, and it does not prove the file is safe.
Check the file first, then decide:

1. Open PowerShell in the folder that holds the ZIP and run (use your ZIP's real name):
       Get-FileHash .\SafeClean-<version>-win-x64.zip -Algorithm SHA256
2. Compare the result with the .sha256 file published next to the ZIP. They must match exactly.
   If they differ, do not run it: the download is damaged or has been changed.
3. If they match and you trust where you got it, click "More info", then "Run anyway".


WHAT IT DOES AND DOES NOT DO

- It starts in Dry Run mode, which only previews. Nothing is deleted unless you switch Real cleanup
  mode on, select files, press the red button and confirm.
- It only looks in your own Temp folder, and only offers files at least 7 days old.
- Deleted files do NOT go to the Recycle Bin. They are gone for good.
- It works offline and sends nothing anywhere.


WHERE YOUR DATA IS, AND HOW TO REMOVE EVERYTHING

Settings, cleanup history and logs:  %LOCALAPPDATA%\SafeClean
(Paste that into the File Explorer address bar to open it.)

To remove SafeClean, close it and delete the folder you extracted. There is no uninstaller
because nothing was installed. To remove your settings, history and logs too, delete
%LOCALAPPDATA%\SafeClean as well. SafeClean adds no startup entries, services or registry settings.


SYSTEM REQUIREMENTS

Windows 11 or Windows 10, 64-bit (x64). About 130 MB of disk space.

Microsoft's support list for .NET 10 names Windows 11 and the long-term (LTS) editions of Windows 10.
Regular Windows 10 22H2 is no longer on that list. SafeClean was tested on Windows 10 Home 22H2
(build 19045), where it works, but Microsoft no longer promises .NET fixes for it.


KNOWN LIMITS

- Only the current user's Temp folder is supported. A scan stops at 250,000 entries and says so.
- Files in use, changed since the scan, or failing any safety rule are skipped and kept.
- Space freed is the total size of the deleted files. Your free disk space may change by a
  slightly different amount.

The other files in this folder are parts of Microsoft's .NET that SafeClean needs, and their licence
notices. The folder is complete as it is: please keep the files together.
