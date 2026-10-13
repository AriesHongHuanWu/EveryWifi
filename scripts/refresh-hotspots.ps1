param([string]$SourceFile)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dataDirectory = Join-Path $repoRoot 'data'
$sourceUrl = 'https://itaiwan.gov.tw/ITaiwanDW/GetFile?fileName=IpSelect_tw.json&type=6'
$sourcePage = 'https://data.gov.tw/dataset/5962'
$retrievedOn = [TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTimeOffset]::UtcNow, 'Taipei Standard Time').ToString('yyyy-MM-dd')
if ($SourceFile) {
    $sourceBytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $SourceFile).Path)
} else {
    $download = Invoke-WebRequest -Uri $sourceUrl -TimeoutSec 60
    if ($download.Content -is [byte[]]) {
        $sourceBytes = $download.Content
    } else {
        $sourceBytes = [Text.Encoding]::UTF8.GetBytes([string]$download.Content)
    }
}
$sourceText = [Text.Encoding]::UTF8.GetString($sourceBytes).TrimStart([char]0xFEFF)
$sourceRows = @($sourceText | ConvertFrom-Json)
if ($sourceRows.Count -lt 1000 -or -not $sourceRows[0].PSObject.Properties['Name'] -or -not $sourceRows[0].PSObject.Properties['Area']) {
    throw 'Official download format or row count has changed. Existing snapshot was preserved.'
}
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$hotspots = [Collections.Generic.List[object]]::new()
$invalidCoordinates = 0
foreach ($row in $sourceRows) {
    $key = [string]$row.Area + '|' + [string]$row.Name + '|' + [string]$row.Address
    if (-not $seen.Add($key)) { continue }
    [double]$lat = 0
    [double]$lng = 0
    $coordinateOkay = [double]::TryParse([string]$row.Latitude, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$lat)
    $coordinateOkay = [double]::TryParse([string]$row.Longitude, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$lng) -and $coordinateOkay
    if (-not $coordinateOkay -or $lat -lt 20 -or $lat -gt 28 -or $lng -lt 115 -or $lng -gt 123) {
        $lat = 0; $lng = 0; $invalidCoordinates++
    }
    $hotspots.Add([ordered]@{name=[string]$row.Name;address=[string]$row.Address;city=[string]$row.Area;lat=$lat;lng=$lng;sourceUrl=$sourcePage})
}
$hasher = [Security.Cryptography.SHA256]::Create()
try { $sourceHash = ([BitConverter]::ToString($hasher.ComputeHash($sourceBytes))).Replace('-', '').ToLowerInvariant() }
finally { $hasher.Dispose() }
$metadata = [ordered]@{
    sourceName='數位發展部 iTaiwan 公共區域免費無線上網熱點'
    sourceUrl=$sourcePage
    resourceUrl=$sourceUrl
    retrievedOn=$retrievedOn
    sourceRecordCount=$sourceRows.Count
    uniqueRecordCount=$hotspots.Count
    duplicateRecordCount=($sourceRows.Count - $hotspots.Count)
    invalidCoordinateCount=$invalidCoordinates
    cityCount=@($hotspots.city | Sort-Object -Unique).Count
    sourceSha256=$sourceHash
    licenseName='政府資料開放授權條款 第1版'
    licenseUrl='https://data.gov.tw/license'
    note='Official listed locations, not a live scan or uptime guarantee. Duplicate city/name/address rows removed; coordinates preserved as provided when within Taiwan bounds. Same SSID cannot prove access-point ownership.'
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.Directory]::CreateDirectory($dataDirectory) | Out-Null
[IO.File]::WriteAllText((Join-Path $dataDirectory 'hotspots.json'), (ConvertTo-Json -InputObject $hotspots.ToArray() -Depth 5 -Compress), $utf8)
[IO.File]::WriteAllText((Join-Path $dataDirectory 'hotspots.meta.json'), ($metadata | ConvertTo-Json -Depth 5), $utf8)
Write-Output "iTaiwan snapshot: $($hotspots.Count) unique listed locations in $($metadata.cityCount) counties/cities; retrieved $retrievedOn."
