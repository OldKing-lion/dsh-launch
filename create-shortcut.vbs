Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
root = fso.GetParentFolderName(WScript.ScriptFullName)
exe = root & "\publish\DshRepoShell.exe"
ico = root & "\publish\assets\whale-black.ico"
If Not fso.FileExists(exe) Then
  exe = root & "\DshRepoShell.exe"
  ico = root & "\assets\whale-black.ico"
End If
desktop = sh.SpecialFolders("Desktop")
lnkPath = desktop & "\DeepSeek Harness.lnk"
Set s = sh.CreateShortcut(lnkPath)
s.TargetPath = exe
s.WorkingDirectory = fso.GetParentFolderName(exe)
s.IconLocation = ico
s.Description = "Local-repo DeepSeek Harness"
s.Save
WScript.Echo lnkPath
