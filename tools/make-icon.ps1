# Draws the FastCraft icon: the same mark as the window icon in App.xaml - a dark
# rounded tile with two amber speed chevrons - in the sizes Windows asks for.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-icon.ps1 -Out Assets/FastCraft.ico
#
# FastCraft.csproj only uses the file when it exists, so a failure here costs the
# exe its icon and nothing else.

param([string]$Out = "Assets/FastCraft.ico")

Add-Type -AssemblyName System.Drawing

function New-MarkBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    # the mark is drawn on a 24 x 24 grid, like the XAML version
    $u = $Size / 24.0

    $inset = [Math]::Max(0.5, $u)
    $radius = [float]($u * 5.5)
    $rect = New-Object System.Drawing.RectangleF(
        [float]$inset, [float]$inset,
        [float]($Size - 2 * $inset), [float]($Size - 2 * $inset))

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc([float]$rect.X, [float]$rect.Y, [float]$diameter, [float]$diameter, 180, 90)
    $path.AddArc([float]($rect.Right - $diameter), [float]$rect.Y, [float]$diameter, [float]$diameter, 270, 90)
    $path.AddArc([float]($rect.Right - $diameter), [float]($rect.Bottom - $diameter), [float]$diameter, [float]$diameter, 0, 90)
    $path.AddArc([float]$rect.X, [float]($rect.Bottom - $diameter), [float]$diameter, [float]$diameter, 90, 90)
    $path.CloseFigure()

    $tile = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 28, 34, 43))
    $graphics.FillPath($tile, $path)

    if ($Size -ge 20) {
        $edge = New-Object System.Drawing.Pen(
            [System.Drawing.Color]::FromArgb(255, 44, 53, 66),
            [float]([Math]::Max(0.6, $u)))
        $graphics.DrawPath($edge, $path)
        $edge.Dispose()
    }

    # front chevron in the accent, back one dimmed
    $chevrons = @(
        @(8.0, 12.6, [System.Drawing.Color]::FromArgb(255, 217, 165, 91)),
        @(13.2, 17.8, [System.Drawing.Color]::FromArgb(255, 140, 110, 67))
    )
    foreach ($chevron in $chevrons) {
        $pen = New-Object System.Drawing.Pen($chevron[2], [float]($u * 2.1))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

        $x0 = [float]($chevron[0] * $u)
        $x1 = [float]($chevron[1] * $u)
        $points = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF($x0, [float](7 * $u))),
            (New-Object System.Drawing.PointF($x1, [float](12 * $u))),
            (New-Object System.Drawing.PointF($x0, [float](17 * $u)))
        )
        $graphics.DrawLines($pen, $points)
        $pen.Dispose()
    }

    $tile.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    return $bitmap
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$blobs = @()
foreach ($size in $sizes) {
    $bitmap = New-MarkBitmap -Size $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs += , $stream.ToArray()
    $stream.Dispose()
    $bitmap.Dispose()
}

$directory = Split-Path -Parent $Out
if ($directory -and -not (Test-Path -LiteralPath $directory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

# an ICO is a 6-byte header, one 16-byte directory entry per image, then the images.
# These entries are PNG-compressed, which Windows 10 and 11 read at every size.
$file = [System.IO.File]::Create($Out)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $dimension = if ($size -ge 256) { 0 } else { $size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$blobs[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $blobs[$i].Length
    }
    foreach ($blob in $blobs) {
        $writer.Write($blob)
    }
}
finally {
    $writer.Flush()
    $writer.Dispose()
    $file.Dispose()
}

Write-Host ("Wrote {0} with {1} sizes." -f $Out, $sizes.Count)
