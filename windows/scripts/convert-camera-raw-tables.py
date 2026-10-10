"""Repack upstream CRT3 (XZ) planes as raw DEFLATE for the .NET runtime.
No calibration values are changed. Re-run after upstream CameraRawTables.bin changes.
"""
from pathlib import Path
import hashlib, lzma, struct, zlib
root = Path(__file__).resolve().parents[1]
source = root.parent / "Compositor/Resources/CameraRawTables.bin"
data = source.read_bytes()
assert data[:4] == b"CRT3"
size, count, step = struct.unpack("<HHH", data[4:10])
assert (size, count, step) == (17, 763, 3)
planes = lzma.decompress(data[10:])
assert len(planes) == count * size ** 3 * 3
compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
target = root / "src/Compositor.Core/Resources/CameraRawTables.deflate"
target.parent.mkdir(parents=True, exist_ok=True)
target.write_bytes(b"WRT3" + data[4:10] + compressor.compress(planes) + compressor.flush())
print("Upstream SHA-256:", hashlib.sha256(data).hexdigest())
print("Windows SHA-256:", hashlib.sha256(target.read_bytes()).hexdigest())
