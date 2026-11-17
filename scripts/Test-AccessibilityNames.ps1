<#
.SYNOPSIS
    Checks the running app for controls that assistive technology cannot name.

.DESCRIPTION
    Walks every page through UI Automation and reports each keyboard focusable
    element whose UIA name is empty. A control without a name is announced by
    Narrator as its bare type ("button"), which makes it unusable without sight.

    A button only derives its name from plain string content. Markup content
    (an icon plus a TextBlock, a header built from a StackPanel) leaves the name
    empty, as does a label that merely sits next to the control. Those cases
    need an explicit AutomationProperties.Name.

    The script starts the app when it is not running and leaves it running, so
    the result can be inspected. It never stops an instance it did not start.
    Exit code 1 means at least one element has no name.

    This file is UTF-8 with a BOM on purpose: Windows PowerShell 5.1 reads it as
    ANSI otherwise and then fails to match the German page names below.

.EXAMPLE
    .\scripts\Test-AccessibilityNames.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$ExePath,
    [string[]]$Pages = @('Dashboard', 'Bibliothek', 'Sessions', 'Statistiken', 'Jahresrückblick', 'Einstellungen')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $ExePath = Join-Path $repositoryRoot "YFTimeTracker.App\bin\$Configuration\net10.0-windows10.0.26100.0\win-x64\YFTimeTracker.App.exe"
}

if (-not (Test-Path $ExePath)) {
    throw "Build not found: $ExePath. Run 'dotnet build YFTimeTracker.slnx' first."
}

function Get-AppProcess {
    Get-Process YFTimeTracker.App -ErrorAction SilentlyContinue | Select-Object -First 1
}

function Get-MainWindow {
    param($Process)

    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $Process.Id)
    $AE::RootElement.FindFirst($Scope::Children, $condition)
}

$process = Get-AppProcess
if ($process -and $process.Path -ne $ExePath) {
    throw "Another instance is running: $($process.Path). It holds the single instance mutex, so the build under test cannot start. Close it first."
}

if (-not $process) {
    Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path $ExePath)
    Start-Sleep -Seconds 8
    $process = Get-AppProcess
    if (-not $process) { throw "App did not start: $ExePath" }
}

# With "start minimized" enabled the window stays hidden. Launching again hits
# the single instance path, which shows the window of the running instance.
if ($process.MainWindowHandle -eq 0) {
    Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path $ExePath)
    Start-Sleep -Seconds 5
    $process = Get-AppProcess
}

$window = Get-MainWindow -Process $process
if (-not $window) { throw 'No window found. Open the app window and run the script again.' }

function Get-UnnamedElements {
    param($Window)

    $found = @()
    foreach ($element in $Window.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $current = $element.Current
        if (-not $current.IsKeyboardFocusable) { continue }
        if (-not [string]::IsNullOrWhiteSpace($current.Name)) { continue }

        $type = $current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        $labels = @()
        foreach ($child in $element.FindAll($Scope::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($child.Current.Name) { $labels += $child.Current.Name }
        }
        $context = @()
        $parent = $walker.GetParent($element)
        for ($level = 0; $level -lt 4 -and $null -ne $parent; $level++) {
            if ($parent.Current.Name) { $context += "'$($parent.Current.Name)'" }
            elseif ($parent.Current.AutomationId) { $context += "#$($parent.Current.AutomationId)" }
            $parent = $walker.GetParent($parent)
        }

        $found += [pscustomobject]@{
            Type    = $type
            Id      = $current.AutomationId
            Content = (($labels | Select-Object -First 3) -join ' / ')
            Context = ($context -join ' < ')
        }
    }

    $found
}

$total = 0
foreach ($page in $Pages) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $page)
    $item = $window.FindFirst($Scope::Descendants, $condition)
    if (-not $item) {
        Write-Warning "Navigation item '$page' not found - skipped."
        continue
    }

    $pattern = $null
    if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
        $pattern.Select()
    }
    elseif ($item.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
    }
    else {
        Write-Warning "Navigation item '$page' cannot be invoked - skipped."
        continue
    }

    Start-Sleep -Milliseconds 2000

    $unnamed = @(Get-UnnamedElements -Window $window)
    $total += $unnamed.Count
    Write-Host "$page : $($unnamed.Count) focusable element(s) without a name"
    foreach ($entry in $unnamed) {
        Write-Host "    $($entry.Type) id='$($entry.Id)' content='$($entry.Content)' in $($entry.Context)"
    }
}

Write-Host ''
if ($total -gt 0) {
    Write-Host "$total element(s) without a name. Add AutomationProperties.Name to each one."
    exit 1
}

Write-Host 'Every focusable element has a name.'
