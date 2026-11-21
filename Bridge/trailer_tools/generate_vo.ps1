# Generates the spoken lines with the Windows built-in speech voices (System.Speech) and writes each line's duration back into the JSON.
# Usage: powershell -File generate_vo.ps1 <dialogue.json> <out_dir>
param([string]$Json, [string]$OutDir)
Add-Type -AssemblyName System.Speech
New-Item -ItemType Directory -Force $OutDir | Out-Null
$doc = Get-Content -Raw -Path $Json | ConvertFrom-Json
$names = @{ david = 'Microsoft David Desktop'; zira = 'Microsoft Zira Desktop'; hazel = 'Microsoft Hazel Desktop' }
$rates = @{ david = -2; zira = -1; hazel = 0 }
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
foreach ($l in $doc.lines) {
  $s = New-Object System.Speech.Synthesis.SpeechSynthesizer
  $s.SelectVoice($names[$l.voice]); $s.Rate = $(if ($l.PSObject.Properties['rate']) { [int]$l.rate } else { $rates[$l.voice] }); $s.Volume = 100
  $path = Join-Path $OutDir ("vo_" + $l.id + ".wav")
  $s.SetOutputToWaveFile($path, $fmt)
  $s.Speak($l.text); $s.Dispose()
  $dur = [math]::Round(((Get-Item $path).Length - 44) / (22050 * 2), 2)
  $l | Add-Member -NotePropertyName dur -NotePropertyValue $dur -Force
  "{0} {1} {2}s : {3}" -f $l.id, $l.voice, $dur, $l.text
}
$doc | ConvertTo-Json -Depth 5 | Set-Content -Path $Json -Encoding UTF8
