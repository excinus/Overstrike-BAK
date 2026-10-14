// Overstrike-BAK -- an open-source mod manager for Batman: Arkham Knight, forked from Overstrike by Tkachov.
// This program is free software, and can be redistributed and/or modified by you. It is provided 'as-is', without any warranty.
// For more details, terms and conditions, see GNU General Public License.
// A copy of the that license should come with this program (LICENSE.txt). If not, see <http://www.gnu.org/licenses/>.

using OverstrikeBAK.Utils;
using System;
using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OverstrikeBAK.Suits {
	/// <summary>
	/// Showcase grid thumbnails. Every Showcase item (add-on skins included) ships a small package
	/// Showcase_UI_&lt;Item&gt;_SF.upk whose only texture, Thumb_I1, is the picture the grid shows
	/// (184 x 408, usually DXT1, mips stored inline). Decoded thumbnails are cached as PNG.
	/// </summary>
	internal static class Thumbnails {
		private const uint PackageTag = 0x9E2A83C1;

		private static string CacheDirectory => Path.Combine(App.BaseDirectory, "Cache", "Thumbnails");

		public static BitmapSource Load(string packagePath) {
			try {
				var info = new FileInfo(packagePath);
				if (!info.Exists) return null;

				var cacheName = FileUtils.SanitizeFileName(Path.GetFileNameWithoutExtension(packagePath)) + "_" + info.Length.ToString("x") + "_" + info.LastWriteTimeUtc.Ticks.ToString("x") + ".png";
				var cachePath = Path.Combine(CacheDirectory, cacheName);

				if (File.Exists(cachePath)) {
					var cached = new BitmapImage();
					cached.BeginInit();
					cached.UriSource = new Uri(cachePath);
					cached.CacheOption = BitmapCacheOption.OnLoad;
					cached.EndInit();
					cached.Freeze();
					return cached;
				}

				var bitmap = Extract(File.ReadAllBytes(packagePath));
				if (bitmap == null) return null;

				try {
					Directory.CreateDirectory(CacheDirectory);
					using var stream = new FileStream(cachePath, FileMode.Create);
					var encoder = new PngBitmapEncoder();
					encoder.Frames.Add(BitmapFrame.Create(bitmap));
					encoder.Save(stream);
				} catch {}

				return bitmap;
			} catch {
				return null;
			}
		}

		public static BitmapSource Extract(byte[] package) {
			var data = Decompress(package);

			// Texture2D mip bulk data: [count][size on disk][offset in file (int64)] [data] [SizeX][SizeY]
			for (var p = 0; p + 16 < data.Length; ++p) {
				var size = BitConverter.ToInt32(data, p);
				if (size <= 64 || size != BitConverter.ToInt32(data, p + 4)) continue;
				if (BitConverter.ToInt64(data, p + 8) != p + 16) continue;
				if (p + 16 + (long)size + 8 > data.Length) continue;

				var w = BitConverter.ToInt32(data, p + 16 + size);
				var h = BitConverter.ToInt32(data, p + 20 + size);
				if (w <= 0 || h <= 0 || w > 4096 || h > 4096) continue;

				var pixels = (long)w * h;
				var bgra = (pixels / 2 == size) ? DecodeDxt(data, p + 16, w, h, false)
					: (pixels == size) ? DecodeDxt(data, p + 16, w, h, true)
					: (pixels * 4 == size) ? CopyBgra(data, p + 16, size)
					: null;
				if (bgra == null) continue;

				var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
				bitmap.Freeze();
				return bitmap;
			}

			return null;
		}

		#region package decompression

		/// <summary>Fully compressed packages: rebuild the uncompressed file so offsets inside it are valid.</summary>
		private static byte[] Decompress(byte[] d) {
			if (d.Length < 128 || BitConverter.ToUInt32(d, 0) != PackageTag) return d;

			var namesOffset = BitConverter.ToInt32(d, 29);
			if (namesOffset <= 0) return d;

			// the compressed chunks table: count, then (uncompressed offset, uncompressed size, compressed offset, compressed size)
			var tablePos = -1;
			var limit = Math.Min(d.Length - 24, Math.Min(namesOffset, 4096));
			for (var i = 64; i < limit; ++i) {
				var count = BitConverter.ToInt32(d, i);
				if (count <= 0 || count > 100000) continue;
				if (BitConverter.ToInt32(d, i + 4) != namesOffset) continue;
				var compressedOffset = BitConverter.ToInt32(d, i + 12);
				if (compressedOffset <= 0 || compressedOffset + 4 > d.Length || BitConverter.ToUInt32(d, compressedOffset) != PackageTag) continue;
				tablePos = i;
				break;
			}
			if (tablePos == -1) return d; // not compressed

			var chunks = BitConverter.ToInt32(d, tablePos);
			long total = 0;
			for (var k = 0; k < chunks; ++k) {
				var o = tablePos + 4 + 16 * k;
				total = Math.Max(total, (long)BitConverter.ToInt32(d, o) + BitConverter.ToInt32(d, o + 4));
			}

			var output = new byte[total];
			Array.Copy(d, output, Math.Min(namesOffset, d.Length));

			for (var k = 0; k < chunks; ++k) {
				var o = tablePos + 4 + 16 * k;
				var uncompressedOffset = BitConverter.ToInt32(d, o);
				var compressedOffset = BitConverter.ToInt32(d, o + 8);

				var blockSize = BitConverter.ToInt32(d, compressedOffset + 4);
				var chunkUncompressed = BitConverter.ToInt32(d, compressedOffset + 12);
				var blocks = (chunkUncompressed + blockSize - 1) / blockSize;

				var read = compressedOffset + 16 + blocks * 8;
				var write = uncompressedOffset;
				for (var b = 0; b < blocks; ++b) {
					var bc = BitConverter.ToInt32(d, compressedOffset + 16 + b * 8);
					var bu = BitConverter.ToInt32(d, compressedOffset + 20 + b * 8);

					using var input = new ZLibStream(new MemoryStream(d, read, bc), CompressionMode.Decompress);
					var done = 0;
					while (done < bu) {
						var n = input.Read(output, write + done, bu - done);
						if (n <= 0) break;
						done += n;
					}

					read += bc;
					write += bu;
				}
			}

			return output;
		}

		#endregion
		#region DXT

		private static byte[] CopyBgra(byte[] data, int offset, int size) {
			var result = new byte[size];
			Array.Copy(data, offset, result, 0, size);
			return result;
		}

		private static byte[] DecodeDxt(byte[] data, int offset, int w, int h, bool dxt5) {
			var result = new byte[w * h * 4];
			var blockBytes = dxt5 ? 16 : 8;
			var colors = new byte[4 * 4];
			var alphas = new byte[8];

			var p = offset;
			for (var by = 0; by < (h + 3) / 4; ++by) {
				for (var bx = 0; bx < (w + 3) / 4; ++bx) {
					if (p + blockBytes > data.Length) return result;

					ulong alphaBits = 0;
					if (dxt5) {
						alphas[0] = data[p];
						alphas[1] = data[p + 1];
						if (alphas[0] > alphas[1]) {
							for (var i = 1; i < 7; ++i) alphas[i + 1] = (byte)(((7 - i) * alphas[0] + i * alphas[1]) / 7);
						} else {
							for (var i = 1; i < 5; ++i) alphas[i + 1] = (byte)(((5 - i) * alphas[0] + i * alphas[1]) / 5);
							alphas[6] = 0;
							alphas[7] = 255;
						}
						for (var i = 0; i < 6; ++i) alphaBits |= (ulong)data[p + 2 + i] << (8 * i);
						p += 8;
					}

					var c0 = (ushort)(data[p] | data[p + 1] << 8);
					var c1 = (ushort)(data[p + 2] | data[p + 3] << 8);
					var indices = (uint)(data[p + 4] | data[p + 5] << 8 | data[p + 6] << 16 | data[p + 7] << 24);
					p += 8;

					Expand565(c0, colors, 0);
					Expand565(c1, colors, 4);
					if (c0 > c1 || dxt5) {
						for (var i = 0; i < 3; ++i) {
							colors[8 + i] = (byte)((2 * colors[i] + colors[4 + i]) / 3);
							colors[12 + i] = (byte)((colors[i] + 2 * colors[4 + i]) / 3);
						}
						colors[11] = colors[15] = 255;
					} else {
						for (var i = 0; i < 3; ++i) {
							colors[8 + i] = (byte)((colors[i] + colors[4 + i]) / 2);
							colors[12 + i] = 0;
						}
						colors[11] = 255;
						colors[15] = 0;
					}

					for (var py = 0; py < 4; ++py) {
						for (var px = 0; px < 4; ++px) {
							var x = bx * 4 + px;
							var y = by * 4 + py;
							var n = py * 4 + px;
							if (x >= w || y >= h) continue;

							var ci = (int)((indices >> (2 * n)) & 3) * 4;
							var o = (y * w + x) * 4;
							result[o] = colors[ci + 2];     // B
							result[o + 1] = colors[ci + 1]; // G
							result[o + 2] = colors[ci];     // R
							result[o + 3] = dxt5 ? alphas[(int)((alphaBits >> (3 * n)) & 7)] : colors[ci + 3];
						}
					}
				}
			}

			return result;
		}

		private static void Expand565(ushort c, byte[] colors, int o) {
			var r = (c >> 11) & 31;
			var g = (c >> 5) & 63;
			var b = c & 31;
			colors[o] = (byte)((r << 3) | (r >> 2));
			colors[o + 1] = (byte)((g << 2) | (g >> 4));
			colors[o + 2] = (byte)((b << 3) | (b >> 2));
			colors[o + 3] = 255;
		}

		#endregion
	}
}
