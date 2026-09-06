
$ErrorActionPreference = 'SilentlyContinue'
[void][Windows.Gaming.Input.RawGameController,Windows.Gaming.Input,ContentType=WindowsRuntime]
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$out = [Console]::Out

function New-Arrays($c) {
  @{
    b = New-Object bool[] ([Math]::Max(1, $c.ButtonCount))
    s = New-Object Windows.Gaming.Input.GameControllerSwitchPosition[] ([Math]::Max(1, $c.SwitchCount))
    a = New-Object double[] ([Math]::Max(1, $c.AxisCount))
  }
}

$list = @()
$lastEnum = [DateTime]::MinValue

while ($true) {
    if (([DateTime]::UtcNow - $lastEnum).TotalSeconds -ge 2) {
        $raw = [Windows.Gaming.Input.RawGameController]::RawGameControllers
        $candidates = @()
        foreach ($cand in $raw) {
            if ($cand.ButtonCount -gt 0 -and $cand.AxisCount -gt 0) { $candidates += $cand }
        }
        $list = $candidates
        $lastEnum = [DateTime]::UtcNow
    }

    if ($list.Count -eq 0) {
        $out.WriteLine('{"none":1}')
        $out.Flush()
        Start-Sleep -Milliseconds 8
        continue
    }

    for ($i = 0; $i -lt $list.Count; $i++) {
        $c = $list[$i]
        $ar = New-Arrays $c
        $ts = [uint64]0
        try { $ts = $c.GetCurrentReading($ar.b, $ar.s, $ar.a) } catch { continue }

        $vals = New-Object System.Collections.Generic.List[string]
        foreach ($v in $ar.a) { $vals.Add(([Math]::Round($v * 2 - 1, 3)).ToString($inv)) }

        $bits = New-Object System.Collections.Generic.List[string]
        foreach ($b in $ar.b) { if ($b) { $bits.Add('1') } else { $bits.Add('0') } }

        $sw = if ($ar.s.Length -gt 0) { [int]$ar.s[0] } else { 0 }
        $up = if ($sw -eq 1 -or $sw -eq 2 -or $sw -eq 8) { '1' } else { '0' }
        $right = if ($sw -ge 2 -and $sw -le 4) { '1' } else { '0' }
        $down = if ($sw -ge 4 -and $sw -le 6) { '1' } else { '0' }
        $left = if ($sw -ge 6 -and $sw -le 8) { '1' } else { '0' }
        $bits.Add($up); $bits.Add($right); $bits.Add($down); $bits.Add($left)

        $name = ('' + $c.DisplayName).Trim()
        if ($name.Length -gt 60) { $name = $name.Substring(0, 60) }
        $name = $name -replace '["\\]', ''
        $vid = '{0:X4}' -f $c.HardwareVendorId
        $pid_ = '{0:X4}' -f $c.HardwareProductId
        $id = $name + ' (' + $vid + ':' + $pid_ + ')'

        $line = '{"i":' + $i + ',"id":"' + $id + '","vid":"' + $vid + '","pid":"' + $pid_ + '","a":[' + ($vals -join ',') + '],"b":[' + ($bits -join ',') + ']}'
        $out.WriteLine($line)
    }
    $out.Flush()
    Start-Sleep -Milliseconds 8
}
