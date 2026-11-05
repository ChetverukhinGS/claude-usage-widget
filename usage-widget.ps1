# Claude usage widget: a small always-on-top window with the 5-hour and weekly limits.
# Reads ~/.claude/usage-limits.json, which the usage-limits plugin for Claude Code writes.
# Shown only while a Claude desktop window is open. Drag to move, right-click to close.

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$dataFile  = Join-Path $env:USERPROFILE '.claude\usage-limits.json'
$stateFile = Join-Path $env:USERPROFILE '.claude\usage-widget-pos.json'

# One copy at a time
$mutex = New-Object System.Threading.Mutex($false, 'Local\ClaudeUsageWidget')
if (-not $mutex.WaitOne(0)) { return }

[xml]$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    WindowStyle="None" AllowsTransparency="True" Background="Transparent"
    Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
    SizeToContent="WidthAndHeight" Title="Claude limits">
 <Border CornerRadius="8" Background="#E6262624" Padding="10,7,10,7" BorderBrush="#40FFFFFF" BorderThickness="1">
  <StackPanel>
   <Grid Margin="0,0,0,4">
    <Grid.ColumnDefinitions>
     <ColumnDefinition Width="52"/><ColumnDefinition Width="120"/><ColumnDefinition Width="40"/><ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column="0" Text="5 ч" Foreground="#F0EEE6" FontWeight="SemiBold" VerticalAlignment="Center"/>
    <ProgressBar Grid.Column="1" x:Name="Bar5" Height="8" Maximum="100" Background="#33FFFFFF" BorderThickness="0" VerticalAlignment="Center"/>
    <TextBlock Grid.Column="2" x:Name="Pct5" Text="—" Foreground="#F0EEE6" TextAlignment="Right" VerticalAlignment="Center"/>
    <TextBlock Grid.Column="3" x:Name="Rst5" Text="" Foreground="#9C9A92" Margin="8,0,0,0" VerticalAlignment="Center"/>
   </Grid>
   <Grid>
    <Grid.ColumnDefinitions>
     <ColumnDefinition Width="52"/><ColumnDefinition Width="120"/><ColumnDefinition Width="40"/><ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <TextBlock Grid.Column="0" Text="Неделя" Foreground="#F0EEE6" FontWeight="SemiBold" VerticalAlignment="Center"/>
    <ProgressBar Grid.Column="1" x:Name="Bar7" Height="8" Maximum="100" Background="#33FFFFFF" BorderThickness="0" VerticalAlignment="Center"/>
    <TextBlock Grid.Column="2" x:Name="Pct7" Text="—" Foreground="#F0EEE6" TextAlignment="Right" VerticalAlignment="Center"/>
    <TextBlock Grid.Column="3" x:Name="Rst7" Text="" Foreground="#9C9A92" Margin="8,0,0,0" VerticalAlignment="Center"/>
   </Grid>
   <TextBlock x:Name="Note" Text="" Foreground="#9C9A92" FontSize="10" Margin="0,4,0,0"/>
  </StackPanel>
 </Border>
</Window>
'@

$win = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $xaml))
$ui = @{}
foreach ($n in 'Bar5','Pct5','Rst5','Bar7','Pct7','Rst7','Note') { $ui[$n] = $win.FindName($n) }
$win.FontFamily = 'Segoe UI'
$win.FontSize = 12

# Position: last saved, else bottom-right above the taskbar
$wa = [System.Windows.SystemParameters]::WorkArea
$win.Left = $wa.Right - 400; $win.Top = $wa.Bottom - 90
if (Test-Path $stateFile) {
  try { $p = Get-Content $stateFile -Raw | ConvertFrom-Json; $win.Left = [double]$p.left; $win.Top = [double]$p.top } catch {}
}

$win.Add_MouseLeftButtonDown({ try { $win.DragMove() } catch {} ; @{ left = $win.Left; top = $win.Top } | ConvertTo-Json | Set-Content $stateFile -Encoding utf8 })
$menu = New-Object System.Windows.Controls.ContextMenu
$close = New-Object System.Windows.Controls.MenuItem
$close.Header = 'Закрыть виджет'
$close.Add_Click({ $win.Close() })
[void]$menu.Items.Add($close)
$win.ContextMenu = $menu

function Get-Brush([double]$pct) {
  $hex = if ($pct -ge 90) { '#E5484D' } elseif ($pct -ge 70) { '#E2A336' } else { '#4CAF7A' }
  (New-Object System.Windows.Media.BrushConverter).ConvertFromString($hex)
}

function Format-Reset($resetsAt) {
  if (-not $resetsAt) { return '' }
  try { $t = [DateTimeOffset]::Parse($resetsAt) } catch { return '' }
  $ms = ($t - [DateTimeOffset]::Now).TotalMinutes
  if ($ms -le 0) { return 'сброшен' }
  $mins = [int][Math]::Round($ms)
  $d = [Math]::Floor($mins / 1440); $h = [Math]::Floor(($mins % 1440) / 60); $m = $mins % 60
  if ($d -gt 0) { return "сброс через $($d)д $($h)ч" }
  if ($h -gt 0) { return "сброс через $($h)ч $($m)м" }
  return "сброс через $($m)м"
}

function Set-Row($limit, $bar, $pct, $rst) {
  if ($null -eq $limit) { $bar.Value = 0; $pct.Text = '—'; $rst.Text = ''; return }
  $v = [double]$limit.percentUsed
  $bar.Value = [Math]::Min(100, $v)
  $bar.Foreground = Get-Brush $v
  $pct.Text = "$([int][Math]::Round($v))%"
  $rst.Text = Format-Reset $limit.resetsAt
}

function Test-ClaudeOpen {
  [bool](Get-Process -Name 'claude' -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle } | Select-Object -First 1)
}

function Update-Widget {
  if (Test-ClaudeOpen) { if (-not $win.IsVisible) { $win.Show() } } else { if ($win.IsVisible) { $win.Hide() }; return }

  if (-not (Test-Path $dataFile)) {
    $ui.Note.Text = 'Нет данных: отправьте сообщение во вкладке Code'
    return
  }
  try { $snap = Get-Content $dataFile -Raw -Encoding utf8 | ConvertFrom-Json } catch { return }
  Set-Row ($snap.limits | Where-Object kind -eq 'five_hour' | Select-Object -First 1) $ui.Bar5 $ui.Pct5 $ui.Rst5
  Set-Row ($snap.limits | Where-Object kind -eq 'seven_day' | Select-Object -First 1) $ui.Bar7 $ui.Pct7 $ui.Rst7

  $age = [DateTimeOffset]::Now.ToUnixTimeMilliseconds() - [double]$snap.seenAt
  $mins = [int][Math]::Floor($age / 60000)
  $ui.Note.Text = if ($mins -lt 1) { 'обновлено только что' } elseif ($mins -lt 60) { "обновлено $mins мин назад" } else { "обновлено $([int][Math]::Floor($mins / 60)) ч назад" }
}

$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromSeconds(5)
$timer.Add_Tick({ Update-Widget })
$timer.Start()

# Keep the dispatcher running even while the window is hidden
$app = New-Object System.Windows.Application
$app.ShutdownMode = 'OnExplicitShutdown'
$win.Add_Closed({ $timer.Stop(); $app.Shutdown() })
Update-Widget
if (-not $win.IsVisible) { $win.Show(); if (-not (Test-ClaudeOpen)) { $win.Hide() } }
[void]$app.Run()
$mutex.ReleaseMutex()
