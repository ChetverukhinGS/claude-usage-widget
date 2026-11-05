' Starts the Claude usage widget without a console window.
' The script is read as UTF-8, so it works with or without a BOM.
Set fso = CreateObject("Scripting.FileSystemObject")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
script = Replace(dir & "\usage-widget.ps1", "'", "''")
cmd = "powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""& ([scriptblock]::Create([IO.File]::ReadAllText('" & script & "', [Text.Encoding]::UTF8)))"""
CreateObject("WScript.Shell").Run cmd, 0, False
