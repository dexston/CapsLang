<#
.SYNOPSIS
    Local developer and CI script to verify and generate localization artifacts:
    resources/localization/strings_*.resx and src/Core/Localization/Loc.cs from resources/localization/*.strings files.

.DESCRIPTION
    1. Parses all resources/localization/*.strings files and validates syntax.
    2. Performs full verification:
       - 100% key parity across all language files (en.strings, ru.strings, etc.)
       - Placeholder index continuity ({0}, {1}, ...) and parity across languages
       - Source code scan for unreferenced keys (non-blocking warning)
    3. Generates resources/localization/strings_*.resx for each language from the parsed data.
    4. Generates src/Core/Localization/Loc.cs with strongly-typed nested categories and thread-safe language switching.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$locDir = Join-Path $rootDir "resources\localization"
$resDir = Join-Path $rootDir "resources\localization"
$srcDir = Join-Path $rootDir "src"
$locCsPath = Join-Path $srcDir "Core\Localization\Loc.cs"

if (-not (Test-Path $locDir)) {
    Write-Error "Localization directory not found: $locDir"
    exit 1
}

function Unescape-String([string]$s, [ref]$hasError, [ref]$errorToken) {
    if (-not $s) { return "" }
    $sb = [System.Text.StringBuilder]::new($s.Length)
    $i = 0
    while ($i -lt $s.Length) {
        $c = $s[$i]
        if ($c -eq '\') {
            if (($i + 1) -ge $s.Length) {
                $hasError.Value = $true
                $errorToken.Value = '\'
                break
            }
            $next = $s[$i + 1]
            switch ($next) {
                'n'  { [void]$sb.Append("`n"); $i += 2; continue }
                'r'  { [void]$sb.Append("`r"); $i += 2; continue }
                't'  { [void]$sb.Append("`t"); $i += 2; continue }
                '"'  { [void]$sb.Append('"'); $i += 2; continue }
                '\'  { [void]$sb.Append('\'); $i += 2; continue }
                'u'  {
                    if (($i + 5) -lt $s.Length) {
                        $hex = $s.Substring($i + 2, 4)
                        $code = 0
                        if ([int]::TryParse($hex, [System.Globalization.NumberStyles]::HexNumber, $null, [ref]$code)) {
                            [void]$sb.Append([char]$code)
                            $i += 6
                            continue
                        }
                    }
                    $hasError.Value = $true
                    $errorToken.Value = "\u"
                    $i += 2
                    continue
                }
                default {
                    $hasError.Value = $true
                    $errorToken.Value = "\$next"
                    $i += 2
                    continue
                }
            }
        } else {
            [void]$sb.Append($c)
            $i++
        }
    }
    return $sb.ToString()
}

function Get-PlaceholderIndices([string]$val) {
    $indices = [System.Collections.Generic.List[int]]::new()
    $matches = [System.Text.RegularExpressions.Regex]::Matches($val, '\{(\d+)(?:,[^}]+)?(?::[^}]+)?\}')
    foreach ($m in $matches) {
        $idx = [int]$m.Groups[1].Value
        if (-not $indices.Contains($idx)) {
            $indices.Add($idx)
        }
    }
    $indices.Sort()
    return $indices
}

function Parse-StringsFile([string]$filePath, [ref]$totalSyntaxErrors) {
    $dict = [ordered]@{}
    $lines = Get-Content $filePath -Encoding UTF8
    $lineNum = 0
    $syntaxErrors = 0
    foreach ($line in $lines) {
        $lineNum++
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("//") -or $trimmed.StartsWith("#")) {
            continue
        }
        if ($trimmed -match '^\s*"([^"]+)"\s*[=:]\s*"((?:\\.|[^"\\])*)"\s*;\s*(?://.*|#.*)?$') {
            $key = $matches[1]
            $rawVal = $matches[2]

            if ($key -notmatch '^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$') {
                Write-Host "[$($filePath):$($lineNum)] Syntax error: key '$key' contains invalid characters. Keys must contain only English letters, numbers, underscores and dots." -ForegroundColor Red
                $syntaxErrors++
                continue
            }

            if ($dict.Contains($key)) {
                Write-Host "[$($filePath):$($lineNum)] Syntax error: duplicate key '$key'" -ForegroundColor Red
                $syntaxErrors++
                continue
            }

            $hasErr = $false
            $errToken = ""
            $val = Unescape-String $rawVal ([ref]$hasErr) ([ref]$errToken)
            if ($hasErr) {
                Write-Host "[$($filePath):$($lineNum)] Syntax error: invalid escape sequence '$errToken' in line: $line" -ForegroundColor Red
                $syntaxErrors++
            } elseif ([string]::IsNullOrWhiteSpace($val)) {
                Write-Host "[$($filePath):$($lineNum)] Syntax error: empty translation for key '$key'" -ForegroundColor Red
                $syntaxErrors++
            } else {
                $dict[$key] = $val
            }
        } else {
            Write-Host "[$($filePath):$($lineNum)] Syntax error: $line" -ForegroundColor Red
            $syntaxErrors++
        }
    }
    if ($syntaxErrors -gt 0) {
        Write-Host "Found $syntaxErrors syntax error(s) in $filePath" -ForegroundColor Red
        $totalSyntaxErrors.Value += $syntaxErrors
    }
    return $dict
}

# 1. Parse all .strings files
$stringFiles = Get-ChildItem -Path $locDir -Filter "*.strings" | Sort-Object Name
if ($stringFiles.Count -lt 2) {
    Write-Error "Expected at least 2 .strings files in $locDir (e.g. ru.strings and en.strings), found $($stringFiles.Count)."
    exit 1
}

$totalSyntaxErrors = 0
$parsedFiles = [ordered]@{}
foreach ($file in $stringFiles) {
    $parsedFiles[$file.Name] = Parse-StringsFile $file.FullName ([ref]$totalSyntaxErrors)
}
if ($totalSyntaxErrors -gt 0) {
    Write-Error "Found $totalSyntaxErrors syntax error(s) across .strings files!"
    exit 1
}

# 2. Verify Key Parity and Placeholders
Write-Host "Running localization verification..." -ForegroundColor Cyan

$baseFileName = if ($parsedFiles.Contains("en.strings")) { "en.strings" } else { $stringFiles[0].Name }
$baseDict = $parsedFiles[$baseFileName]
$baseKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]$baseDict.Keys)

$hasErrors = $false

# Check placeholder continuity (0, 1, ..., N-1) in base dictionary once
$basePlaceholders = @{}
foreach ($k in $baseDict.Keys) {
    $baseIndices = Get-PlaceholderIndices $baseDict[$k]
    $basePlaceholders[$k] = $baseIndices
    for ($i = 0; $i -lt $baseIndices.Count; $i++) {
        if ($baseIndices[$i] -ne $i) {
            Write-Host "Non-continuous placeholder indices in '$baseFileName' for key '$k': expected {$i} but found {$($baseIndices[$i])}" -ForegroundColor Red
            $hasErrors = $true
            break
        }
    }
}

# Parity across language files
foreach ($fileName in $parsedFiles.Keys) {
    if ($fileName -eq $baseFileName) { continue }

    $currDict = $parsedFiles[$fileName]
    $currKeys = [System.Collections.Generic.HashSet[string]]::new([string[]]$currDict.Keys)

    $missingInCurrent = @($baseKeys | Where-Object { -not $currKeys.Contains($_) })
    $missingInBase = @($currKeys | Where-Object { -not $baseKeys.Contains($_) })

    if ($missingInCurrent.Length -gt 0) {
        Write-Host "Keys in '$baseFileName' missing from '$fileName':" -ForegroundColor Red
        foreach ($k in $missingInCurrent) { Write-Host "  - $k" -ForegroundColor Red }
        $hasErrors = $true
    }

    if ($missingInBase.Length -gt 0) {
        Write-Host "Keys in '$fileName' missing from '$baseFileName':" -ForegroundColor Red
        foreach ($k in $missingInBase) {
            Write-Host "  - $k" -ForegroundColor Red
            $extraIndices = Get-PlaceholderIndices $currDict[$k]
            for ($i = 0; $i -lt $extraIndices.Count; $i++) {
                if ($extraIndices[$i] -ne $i) {
                    Write-Host "Non-continuous placeholder indices in '$fileName' for key '$k': expected {$i} but found {$($extraIndices[$i])}" -ForegroundColor Red
                    break
                }
            }
        }
        $hasErrors = $true
    }

    # Placeholder parity check
    foreach ($k in $baseKeys) {
        if (-not $currKeys.Contains($k)) { continue }

        $baseIndices = $basePlaceholders[$k]
        $currIndices = Get-PlaceholderIndices $currDict[$k]

        # Check match between base and current
        $mismatch = $false
        if ($baseIndices.Count -ne $currIndices.Count) {
            $mismatch = $true
        } else {
            for ($i = 0; $i -lt $baseIndices.Count; $i++) {
                if ($baseIndices[$i] -ne $currIndices[$i]) {
                    $mismatch = $true
                    break
                }
            }
        }

        if ($mismatch) {
            $bStr = ($baseIndices | ForEach-Object { "{$_}" }) -join ", "
            $cStr = ($currIndices | ForEach-Object { "{$_}" }) -join ", "
            Write-Host "Placeholder mismatch for key '$k':" -ForegroundColor Red
            Write-Host "  - '$baseFileName': [$bStr]" -ForegroundColor Red
            Write-Host "  - '$fileName': [$cStr]" -ForegroundColor Red
            $hasErrors = $true
        }
    }
}

if ($hasErrors) {
    Write-Error "Localization parity check failed! Please synchronize keys across all .strings files."
    exit 1
}

# Scan codebase for unreferenced keys (non-blocking warning)
if (Test-Path $srcDir) {
    $codeFiles = @(Get-ChildItem -Path $srcDir -Recurse -Include *.cs, *.xaml | Where-Object { $_.Name -ne 'Loc.cs' })
    if ($codeFiles.Count -gt 0) {
        $allCode = ($codeFiles | Get-Content -Raw) -join "`n"
        $unusedKeys = [System.Collections.Generic.List[string]]::new()
        foreach ($k in $baseKeys) {
            $needle = "Loc.$k"
            if ($allCode.IndexOf($needle, [System.StringComparison]::Ordinal) -lt 0) {
                $unusedKeys.Add($k)
            }
        }
        if ($unusedKeys.Count -gt 0) {
            Write-Host "[Warning] Found $($unusedKeys.Count) localization key(s) not directly referenced in codebase (Loc.*):" -ForegroundColor Yellow
            foreach ($uk in $unusedKeys) {
                Write-Host "  - $uk" -ForegroundColor DarkYellow
            }
        }
    }
}

Write-Host "Localization verification passed: $($baseKeys.Count) keys verified across $($stringFiles.Count) language files." -ForegroundColor Green

# 3. Generate ResX files
if (-not (Test-Path $resDir)) {
    New-Item -ItemType Directory -Path $resDir | Out-Null
}

function Generate-Resx([System.Collections.IDictionary]$dict, [string]$outPath) {
    $sb = [System.Text.StringBuilder]::new()
    $sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>') | Out-Null
    $sb.AppendLine('<root>') | Out-Null
    $sb.AppendLine('  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">') | Out-Null
    $sb.AppendLine('    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />') | Out-Null
    $sb.AppendLine('    <xsd:element name="root" msdata:IsDataSet="true">') | Out-Null
    $sb.AppendLine('      <xsd:complexType>') | Out-Null
    $sb.AppendLine('        <xsd:choice maxOccurs="unbounded">') | Out-Null
    $sb.AppendLine('          <xsd:element name="metadata">') | Out-Null
    $sb.AppendLine('            <xsd:complexType>') | Out-Null
    $sb.AppendLine('              <xsd:sequence>') | Out-Null
    $sb.AppendLine('                <xsd:element name="value" type="xsd:string" minOccurs="0" />') | Out-Null
    $sb.AppendLine('              </xsd:sequence>') | Out-Null
    $sb.AppendLine('              <xsd:attribute name="name" use="required" type="xsd:string" />') | Out-Null
    $sb.AppendLine('              <xsd:attribute name="type" type="xsd:string" />') | Out-Null
    $sb.AppendLine('              <xsd:attribute name="mimetype" type="xsd:string" />') | Out-Null
    $sb.AppendLine('              <xsd:attribute ref="xml:space" />') | Out-Null
    $sb.AppendLine('            </xsd:complexType>') | Out-Null
    $sb.AppendLine('          </xsd:element>') | Out-Null
    $sb.AppendLine('          <xsd:element name="assembly">') | Out-Null
    $sb.AppendLine('            <xsd:complexType>') | Out-Null
    $sb.AppendLine('              <xsd:attribute name="alias" type="xsd:string" />') | Out-Null
    $sb.AppendLine('              <xsd:attribute name="name" type="xsd:string" />') | Out-Null
    $sb.AppendLine('            </xsd:complexType>') | Out-Null
    $sb.AppendLine('          </xsd:element>') | Out-Null
    $sb.AppendLine('          <xsd:element name="data">') | Out-Null
    $sb.AppendLine('            <xsd:complexType>') | Out-Null
    $sb.AppendLine('              <xsd:sequence>') | Out-Null
    $sb.AppendLine('                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />') | Out-Null
    $sb.AppendLine('                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />') | Out-Null
    $sb.AppendLine('              </xsd:sequence>') | Out-Null
    $sb.AppendLine('              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />') | Out-Null
    $sb.AppendLine('              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />') | Out-Null
    $sb.AppendLine('              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />') | Out-Null
    $sb.AppendLine('              <xsd:attribute ref="xml:space" />') | Out-Null
    $sb.AppendLine('            </xsd:complexType>') | Out-Null
    $sb.AppendLine('          </xsd:element>') | Out-Null
    $sb.AppendLine('          <xsd:element name="resheader">') | Out-Null
    $sb.AppendLine('            <xsd:complexType>') | Out-Null
    $sb.AppendLine('              <xsd:sequence>') | Out-Null
    $sb.AppendLine('                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />') | Out-Null
    $sb.AppendLine('              </xsd:sequence>') | Out-Null
    $sb.AppendLine('              <xsd:attribute name="name" type="xsd:string" use="required" />') | Out-Null
    $sb.AppendLine('            </xsd:complexType>') | Out-Null
    $sb.AppendLine('          </xsd:element>') | Out-Null
    $sb.AppendLine('        </xsd:choice>') | Out-Null
    $sb.AppendLine('      </xsd:complexType>') | Out-Null
    $sb.AppendLine('    </xsd:element>') | Out-Null
    $sb.AppendLine('  </xsd:schema>') | Out-Null
    $sb.AppendLine('  <resheader name="resmimetype">') | Out-Null
    $sb.AppendLine('    <value>text/microsoft-resx</value>') | Out-Null
    $sb.AppendLine('  </resheader>') | Out-Null
    $sb.AppendLine('  <resheader name="version">') | Out-Null
    $sb.AppendLine('    <value>2.0</value>') | Out-Null
    $sb.AppendLine('  </resheader>') | Out-Null
    $sb.AppendLine('  <resheader name="reader">') | Out-Null
    $sb.AppendLine('    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>') | Out-Null
    $sb.AppendLine('  </resheader>') | Out-Null
    $sb.AppendLine('  <resheader name="writer">') | Out-Null
    $sb.AppendLine('    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>') | Out-Null
    $sb.AppendLine('  </resheader>') | Out-Null

    foreach ($k in $dict.Keys) {
        $escapedVal = [System.Security.SecurityElement]::Escape($dict[$k])
        $sb.AppendLine("  <data name=`"$k`" xml:space=`"preserve`">") | Out-Null
        $sb.AppendLine("    <value>$escapedVal</value>") | Out-Null
        $sb.AppendLine("  </data>") | Out-Null
    }

    $sb.AppendLine('</root>') | Out-Null
    [System.IO.File]::WriteAllText($outPath, $sb.ToString(), [System.Text.Encoding]::UTF8)
}

foreach ($file in $stringFiles) {
    $code = $file.BaseName.ToLowerInvariant()
    $outResx = Join-Path $resDir "strings_$code.resx"
    $dict = $parsedFiles[$file.Name]
    Generate-Resx $dict $outResx
    Write-Host "Generated: $outResx" -ForegroundColor Cyan
}

# 4. Generate C# Loc.cs
$enStrings = $parsedFiles[$baseFileName]

class LocNode {
    [string]$Name
    [System.Collections.Generic.Dictionary[string, LocNode]]$Children = [System.Collections.Generic.Dictionary[string, LocNode]]::new()
    [System.Collections.Generic.List[psobject]]$Properties = [System.Collections.Generic.List[psobject]]::new()
}

$rootNode = [LocNode]::new()
$rootNode.Name = "Loc"

foreach ($k in $enStrings.Keys) {
    $parts = $k.Split('.')
    $curr = $rootNode
    for ($i = 0; $i -lt ($parts.Length - 1); $i++) {
        $part = $parts[$i]
        if (-not $curr.Children.ContainsKey($part)) {
            $child = [LocNode]::new()
            $child.Name = $part
            $curr.Children[$part] = $child
        }
        $curr = $curr.Children[$part]
    }

    $propName = $parts[-1]
    $resxKey = $k
    $paramCount = $basePlaceholders[$k].Count

    $curr.Properties.Add([pscustomobject]@{
        Name = $propName
        ResxKey = $resxKey
        ParamCount = $paramCount
    })
}

function Render-Node([LocNode]$node, [int]$indent) {
    $ind = " " * $indent
    $sb = [System.Text.StringBuilder]::new()

    foreach ($prop in $node.Properties) {
        if ($prop.ParamCount -gt 0) {
            $argDefs = 0..($prop.ParamCount - 1) | ForEach-Object { "object arg$_" }
            $argList = ($argDefs -join ", ")
            $argPass = (0..($prop.ParamCount - 1) | ForEach-Object { "arg$_" }) -join ", "
            $sb.AppendLine("${ind}public static string $($prop.Name)($argList) => string.Format(CultureInfo.InvariantCulture, Get(`"$($prop.ResxKey)`"), $argPass);") | Out-Null
        } else {
            $sb.AppendLine("${ind}public static string $($prop.Name) => Get(`"$($prop.ResxKey)`");") | Out-Null
        }
    }

    if ($node.Properties.Count -gt 0 -and $node.Children.Count -gt 0) {
        $sb.AppendLine() | Out-Null
    }

    $childCount = 0
    foreach ($childKey in $node.Children.Keys) {
        $childCount++
        $child = $node.Children[$childKey]
        $sb.AppendLine("${ind}public static class $($child.Name)") | Out-Null
        $sb.AppendLine("${ind}{") | Out-Null
        $sb.Append((Render-Node $child ($indent + 4))) | Out-Null
        $sb.AppendLine("${ind}}") | Out-Null
        if ($childCount -lt $node.Children.Count) {
            $sb.AppendLine() | Out-Null
        }
    }

    return $sb.ToString()
}

$cs = [System.Text.StringBuilder]::new()
$cs.AppendLine("// <auto-generated>") | Out-Null
$cs.AppendLine("// This code was generated by scripts/generate-loc.ps1.") | Out-Null
$cs.AppendLine("// Changes to this file may be overwritten.") | Out-Null
$cs.AppendLine("// </auto-generated>") | Out-Null
$cs.AppendLine() | Out-Null
$cs.AppendLine("using System;") | Out-Null
$cs.AppendLine("using System.Globalization;") | Out-Null
$cs.AppendLine("using System.Resources;") | Out-Null
$cs.AppendLine() | Out-Null
$cs.AppendLine("namespace CapsLang") | Out-Null
$cs.AppendLine("{") | Out-Null
$cs.AppendLine("    public static class Loc") | Out-Null
$cs.AppendLine("    {") | Out-Null
$cs.AppendLine("        private static readonly object _syncLock = new object();") | Out-Null
$cs.AppendLine("        private static volatile ResourceManager _rm;") | Out-Null
$cs.AppendLine("        private static readonly ResourceManager _fallback = new ResourceManager(`"CapsLang.Resources.strings_en`", typeof(Program).Assembly);") | Out-Null
$cs.AppendLine() | Out-Null
$cs.AppendLine("        public static event Action LanguageChanged;") | Out-Null
$cs.AppendLine() | Out-Null
$cs.AppendLine("        public static string CurrentLanguageCode => CurrentLanguage.Code;") | Out-Null
$cs.AppendLine("        public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Auto;") | Out-Null
$cs.AppendLine() | Out-Null
$cs.AppendLine("        public static void Initialize(string languagePreference)") | Out-Null
$cs.AppendLine("        {") | Out-Null
$cs.AppendLine("            SetLanguage(languagePreference);") | Out-Null
$cs.AppendLine("        }") | Out-Null
$cs.AppendLine() | Out-Null
$cs.AppendLine("        public static void SetLanguage(string lang)") | Out-Null
$cs.AppendLine("        {") | Out-Null
$cs.AppendLine("            SetLanguage(AppLanguage.FromCode(lang));") | Out-Null
$cs.AppendLine("        }") | Out-Null
$cs.AppendLine() | Out-Null
$cs.AppendLine("        public static void SetLanguage(AppLanguage lang)") | Out-Null
$cs.AppendLine("        {") | Out-Null
$cs.AppendLine("            Action handler;") | Out-Null
$cs.AppendLine("            lock (_syncLock)") | Out-Null
$cs.AppendLine("            {") | Out-Null
$cs.AppendLine("                AppLanguage target = lang ?? AppLanguage.Auto;") | Out-Null
$cs.AppendLine("                if (CurrentLanguage == target && _rm != null)") | Out-Null
$cs.AppendLine("                {") | Out-Null
$cs.AppendLine("                    return;") | Out-Null
$cs.AppendLine("                }") | Out-Null
$cs.AppendLine("                string code = target == AppLanguage.Auto") | Out-Null
$cs.AppendLine("                    ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant()") | Out-Null
$cs.AppendLine("                    : target.Code;") | Out-Null
$cs.AppendLine("                if (AppLanguage.FromCode(code) == AppLanguage.Auto && !string.Equals(code, AppLanguage.Auto.Code, StringComparison.OrdinalIgnoreCase))") | Out-Null
$cs.AppendLine("                {") | Out-Null
$cs.AppendLine("                    code = AppLanguage.English.Code;") | Out-Null
$cs.AppendLine("                }") | Out-Null
$cs.AppendLine("                _rm = string.Equals(code, AppLanguage.English.Code, StringComparison.OrdinalIgnoreCase)") | Out-Null
$cs.AppendLine("                    ? _fallback") | Out-Null
$cs.AppendLine("                    : new ResourceManager(`"CapsLang.Resources.strings_`" + code, typeof(Program).Assembly);") | Out-Null
$cs.AppendLine("                CurrentLanguage = target;") | Out-Null
$cs.AppendLine("                handler = LanguageChanged;") | Out-Null
$cs.AppendLine("            }") | Out-Null
$cs.AppendLine("            handler?.Invoke();") | Out-Null
$cs.AppendLine("        }") | Out-Null
$cs.AppendLine() | Out-Null
$cs.AppendLine("        private static string Get(string key) => _rm?.GetString(key) ?? _fallback.GetString(key) ?? key;") | Out-Null
$cs.AppendLine() | Out-Null
$cs.Append((Render-Node $rootNode 8)) | Out-Null
$cs.AppendLine("    }") | Out-Null
$cs.AppendLine("}") | Out-Null

[System.IO.File]::WriteAllText($locCsPath, $cs.ToString(), [System.Text.Encoding]::UTF8)
Write-Host "Generated: $locCsPath" -ForegroundColor Green
