$ErrorActionPreference = 'Stop'
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$src = "C:\Users\administraor\Desktop\launcher\tests\RtfTableTest\Program.cs"
$out = "C:\Users\administraor\Desktop\launcher\tests\RtfTableTest\RtfTableTest.exe"
& $csc /nologo /out:$out /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll $src
if ($LASTEXITCODE -ne 0) { throw "compile failed" }
"compiled ok"
